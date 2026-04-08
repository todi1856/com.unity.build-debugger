using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Unity.Android.Gradle.Manifest;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.PackageManager.UI;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.BuildDebugger
{
    public class MainWindow : EditorWindow
    {
        [MenuItem("Window/Analysis/Build Debugger")]
        public static MainWindow Open()
        {
            var wnd = GetWindow<MainWindow>("Build Debugger");
            wnd.Focus();
            wnd.Show();
            return wnd;
        }

        private UserSettings m_UserSettings;
        private BuildGraphView m_GraphView;
        private ListView m_NodeListView;
        private VisualElement m_NodeExtraInformation;
        private Label m_NodeExtraInformationLabel;
        private ListView m_PortsListView;
        private ToolbarSearchField m_SearchField;
        private TabView m_TabView;
        private Label m_StatusBar;

        Dictionary<int, DagNode> m_NodeCache = new Dictionary<int, DagNode>();

        public void OnEnable()
        {
            m_UserSettings = UserSettings.GetOrLoad();
        }

        public void OnDisable()
        {
            UserSettings.Save();
        }

        public void CreateGUI()
        {
            LoadUI();
        }

        private void LoadUI()
        {
            var r = rootVisualElement;
            r.Clear();
            // Load UXML
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Utilities.ResolveUIPath("Main.uxml"));

            // Clone into root
            visualTree.CloneTree(r);

            // Query elements
            var loadBtn = r.Q<ToolbarButton>("loadButton");
            loadBtn.clicked += LoadDagJson;
            var autoLoad = r.Q<ToolbarToggle>("toolbarToggleLoadOnBuild");
            autoLoad.value = m_UserSettings.AutoLoadPlayerDagJson;
            autoLoad.RegisterValueChangedCallback(evt =>
            {
                m_UserSettings.AutoLoadPlayerDagJson = evt.newValue;
            });

            var ignoreNodePlayer = r.Q<ToolbarToggle>("toolbarIgnoreNodePlayer");
            ignoreNodePlayer.value = m_UserSettings.IgnoreNodePlayer;
            ignoreNodePlayer.RegisterValueChangedCallback(evt =>
            {
                m_UserSettings.IgnoreNodePlayer = evt.newValue;
            });


            // TODO: No public why of accessing bee why.
            var emitBeeWhy = r.Q<ToolbarToggle>("toolbarToggleEmitBeeWhy");
            emitBeeWhy.style.display = DisplayStyle.None;

            var graphContainer = r.Q<VisualElement>("graphContainer");

            // Create and add GraphView
            m_GraphView = new BuildGraphView();
            m_GraphView.style.flexGrow = 1;
            graphContainer.Add(m_GraphView);


            // List build nodes
            {
                m_NodeListView = r.Q<ListView>("listViewNodes");
                m_NodeListView.makeItem = () =>
                {
                    var label = new Label();
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                    return label;
                };

                m_NodeListView.bindItem = (element, i) =>
                {
                    var node = m_NodeListView.itemsSource[i] as BuildNode;
                    var label = element.Q<Label>();
                    label.text = node.title;
                };

                m_NodeListView.selectionChanged += (objects) => FilterPorts();

                m_NodeExtraInformation = r.Q<VisualElement>("extraInformation");
                m_NodeExtraInformationLabel = r.Q<Label>("extraInformationLabel");
                m_PortsListView = r.Q<ListView>("listViewPorts");
                m_SearchField = r.Q<ToolbarSearchField>("toolbarSearchFieldNodes");
                m_SearchField.RegisterValueChangedCallback(evt => FilterBuildNodesAndPorts());
                m_TabView = r.Q<TabView>("tabView");
                m_TabView.activeTabChanged += (oldTab, newTab) => FilterBuildNodesAndPorts();
            }


            var jumpToNode = r.Q<Button>("btnJumpToNode");
            jumpToNode.clicked += JumpToNode;
            var jumpToPrevious = r.Q<Button>("btnJumpToPrevious");
            jumpToPrevious.clicked += JumpToPreviousNode;

            var dbgElements = r.Q<VisualElement>("dbgElements");
            dbgElements.visible = Unsupported.IsDeveloperMode();
            dbgElements.Add(new ToolbarButton(() =>
            {
                LoadUI();
            })
            { text = "Reload UI" });

            dbgElements.Add(new ToolbarButton(() =>
            {
                InfoWindow.Open(File.ReadAllText(Utilities.ResolveUIPath("Main.uxml")));
            })
            { text = "Info window" });

            m_StatusBar = r.Q<Label>("labelStatusBar");
        }

        private void M_TabView_activeTabChanged(Tab arg1, Tab arg2)
        {
            throw new NotImplementedException();
        }

        private void JumpToNode()
        {   
            if (m_NodeListView.selectedIndex < 0 || m_NodeListView.selectedIndex >= m_NodeListView.itemsSource.Count)
                return;
            string title = (m_NodeListView.itemsSource[m_NodeListView.selectedIndex] as BuildNode).title;
            m_GraphView.FocusNodeByTitle(title);
        }


        private void JumpToPreviousNode()
        {
            var previousNode = m_GraphView.PopNode();
            if (previousNode == null)
                return;
            m_GraphView.FocusNode(previousNode, false, true);
        }

        private void LoadDagJson()
        {
            var path = EditorUtility.OpenFilePanel("Select Dag JSON", m_UserSettings.LastDagJsonPath, "dag.json");
            if (string.IsNullOrEmpty(path))
                return;
            m_UserSettings.LastDagJsonPath = Path.GetDirectoryName(path);
            var data = LoadDagJson(path);
            ConstructGraph(data, false);
            FilterBuildNodesAndPorts();
            SetStatusBarMessage($"Loaded \"{path}\" with {m_GraphView.BuildNodes.Count} build nodes.");
        }

        private bool PortMatchesSearch(Port port)
        {
            return port.portName.Contains(m_SearchField.value, StringComparison.OrdinalIgnoreCase);
        }

        private void FilterBuildNodesAndPorts()
        {
            FilterBuildNodes();
            FilterPorts();
        }

        private void FilterBuildNodes()
        {
            m_NodeExtraInformation.style.display = m_TabView.selectedTabIndex == 0 ? DisplayStyle.None : DisplayStyle.Flex;

            void SetItems(List<BuildNode> nodes)
            {
                m_NodeListView.itemsSource = nodes;
                m_NodeListView.Rebuild();
            }

            if (string.IsNullOrEmpty(m_SearchField.value))
            {
                SetItems(m_GraphView.BuildNodes.Values.Where(n => n.visible).ToList());
                return;
            }

            var filteredNodes = new HashSet<BuildNode>();

            switch (m_TabView.selectedTabIndex)
            {
                case 0: // Build Nodes
                    foreach (var node in m_GraphView.BuildNodes.Values)
                    {
                        if (node.visible && node.title.Contains(m_SearchField.value, StringComparison.OrdinalIgnoreCase))
                            filteredNodes.Add(node);
                    }
                    break;
                case 1: // Inputs
                case 2: // Outputs
                    foreach (var node in m_GraphView.BuildNodes.Values)
                    {
                        if (!node.visible)
                            continue;
                        foreach (var port in m_TabView.selectedTabIndex == 1 ? node.EnumerateInputPorts() : node.EnumerateOutputPorts())
                        {
                            if (PortMatchesSearch(port))
                                filteredNodes.Add(node);
                        }
                    }
                    break;
            }

            SetItems(filteredNodes.ToList());
        }

        private void FilterPorts()
        {
            if (m_TabView.selectedTabIndex == 0)
                return;

            void SetItems(List<string> nodes)
            {
                var title = m_TabView.selectedTabIndex == 1 ? "Inputs" : "Outputs";
                m_NodeExtraInformationLabel.text = $"<b>{title} ({nodes.Count}):</b>";
                m_PortsListView.itemsSource = nodes;
                m_PortsListView.Rebuild();
            }

            var node = m_NodeListView.selectedItem as BuildNode;
            if (node == null)
            {
                SetItems(Array.Empty<string>().ToList());
                return;
            }
            IEnumerable<Port> ports = null;
            switch (m_TabView.selectedTabIndex)
            {
                case 1: // Inputs
                    ports = node.EnumerateInputPorts();
                    break;
                case 2: // Outputs
                    ports = node.EnumerateOutputPorts();
                    break;
            }

            var filteredPorts = new List<string>();
            foreach (var port in ports)
            {
                if (PortMatchesSearch(port))
                    filteredPorts.Add(port.portName);
            }

            m_PortsListView.itemsSource = filteredPorts;
            m_PortsListView.Rebuild();

            SetItems(filteredPorts);
        }

        internal void LoadDagJsonAndTundra(string dagPath, string tundraPath)
        {
            var data = LoadDagJson(dagPath);
            var tundraEntries = LoadTundraJson(tundraPath);

            var cache = new Dictionary<int, DagNode>();
            foreach (var node in data.Nodes)
            {
                node.WasModifiedDuringBuild = false;
                cache.Add(node.DebugActionIndex, node);
            }

            foreach (var entry in tundraEntries)
            {
                if (cache.TryGetValue(entry, out var node))
                {
                    node.WasModifiedDuringBuild = true;
                }
                else
                    Utilities.LogWarning($"No node found for index {entry}.");
            }

            ConstructGraph(data, true);
            FilterBuildNodesAndPorts();
            SetStatusBarMessage(@$"Loaded ""{dagPath}"" with {m_GraphView.BuildNodes.Count} build nodes.
Loaded ""{tundraPath}"", {tundraEntries.Count} build nodes were modified.");
        }

        internal DagFile LoadDagJson(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            string rawJson = File.ReadAllText(path);
            var data = JsonUtility.FromJson<DagFile>(rawJson);

            m_NodeCache = new Dictionary<int, DagNode>();
            foreach (var node in data.Nodes)
            {
                if (m_NodeCache.ContainsKey(node.DebugActionIndex))
                    throw new Exception($"Duplicate DebugActionIndex found: {node.DebugActionIndex}");
                m_NodeCache[node.DebugActionIndex] = node;
            }

            var depthCache = new Dictionary<int, int>();
            foreach (var node in data.Nodes)
            {
                node.Depth = CalculateDepdencyChainDepth(node, depthCache);
            }

            return data;
        }

        internal static List<int> LoadTundraJson(string path)
        {
            return File.ReadLines(path)
                .Select(line => JsonUtility.FromJson<TundraLogEntry>(line))
                .Where(entry => entry.msg.Equals("runNodeAction"))
                .Select(entry => entry.index)
                .ToList();
        }

        internal void ConstructGraph(DagFile data, bool markModifiedNodes)
        {
            var start = DateTime.Now;
            m_GraphView.PopulateFromData(data, m_UserSettings.IgnoreNodePlayer, markModifiedNodes);
            var end = DateTime.Now;
            Utilities.Log($"Graph construction took {(end - start).TotalSeconds} seconds.");
        }

        private int CalculateDepdencyChainDepth(DagNode node, Dictionary<int, int> depthCache)
        {
            if (depthCache.TryGetValue(node.DebugActionIndex, out var cachedDepth))
                return cachedDepth;
            if (node.ToBuildDependencies == null || node.ToBuildDependencies.Count == 0)
            {
                depthCache[node.DebugActionIndex] = 0;
                return 0;
            }

            int maxDepth = 0;
            foreach (var depIndex in node.ToBuildDependencies)
            {
                if (m_NodeCache.TryGetValue(depIndex, out var depNode))
                {
                    int depDepth = CalculateDepdencyChainDepth(depNode, depthCache);
                    maxDepth = Mathf.Max(maxDepth, depDepth + 1);
                }
            }
            depthCache[node.DebugActionIndex] = maxDepth;
            return maxDepth;
        }

        public void SetStatusBarMessage(string message)
        {
            m_StatusBar.text = $"<b>{message}</b>";
        }
    }
}