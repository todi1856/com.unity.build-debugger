using NUnit;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.BuildDebugger
{
    public class BuildGraphView : GraphView
    {
        int m_MaxDepth = 0;

        Dictionary<int, BuildNode> m_BuildNodeCache = new Dictionary<int, BuildNode>();
        Dictionary<int, DagNode> m_DagNodeCache = new Dictionary<int, DagNode>();
        IVisualElementScheduledItem m_Animator;
        BuildNode m_CurrentNode;
        LinkedList<BuildNode> m_Queue;

        public IReadOnlyDictionary<int, BuildNode> BuildNodes => m_BuildNodeCache;

        public void PushNode(BuildNode node)
        {
            m_Queue.AddFirst(node);

            if (m_Queue.Count > 10)
                m_Queue.RemoveLast();
        }

        public BuildNode PopNode()
        {
            if (m_Queue.Count == 0)
                return null;
            var value = m_Queue.First.Value;
            m_Queue.RemoveFirst();
            return value;
        }

        public BuildGraphView()
        {
            GridBackground grid = new GridBackground();
            grid.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(Utilities.ResolveUIPath("MyGridStyle.uss")));
            // 2. Add it to the GraphView at index 0 (the very back)
            Insert(0, grid);

            // 3. Make it stretch to fill the entire container
            grid.StretchToParentSize();

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            Insert(0, grid);
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            // No menus
        }

        public void FocusNodeByTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
                return;

            // 1. Find the first node that contains the search string (case-insensitive)
            var targetNode = nodes.ToList().Cast<Node>()
                .FirstOrDefault(n => n.title.ToLower().Contains(title.ToLower()));

            FocusNode(targetNode, true, true);
        }

        public void FocusNode(Node node, bool memorizeCurrentNode, bool animate)
        {
            if (node == null)
                return;

            if (m_CurrentNode != null && memorizeCurrentNode)
                PushNode(m_CurrentNode);

            m_CurrentNode = node as BuildNode;

            var startTranslation = contentViewContainer.resolvedStyle.translate;
            var startScale = contentViewContainer.resolvedStyle.scale.value;

            ClearSelection();
            AddToSelection(node);
            FrameSelection();

            if (animate)
            {
                // Dumb way of animating things
                AnimateFrame(
                    startTranslation,
                    startScale,
                    contentViewContainer.resolvedStyle.translate,
                    contentViewContainer.resolvedStyle.scale.value);
            }
        }

        public void AnimateFrame( 
            Vector3 startTranslation,
            Vector3 startScale,
            Vector3 targetTranslation, 
            Vector3 targetScale, float duration = 1.0f)
        {
            float startTime = Time.realtimeSinceStartup;

            if (m_Animator != null)
                m_Animator.Pause();
            m_Animator = schedule.Execute(() =>
            {
                float t = (Time.realtimeSinceStartup - startTime) / duration;
                t = Mathf.Clamp01(t);

                // Optional easing
                t = Mathf.SmoothStep(0, 1, t);

                var pos = Vector3.Lerp(startTranslation, targetTranslation, t);
                var scl = Vector3.Lerp(startScale, targetScale, t);

                UpdateViewTransform(pos, scl);

                if (t >= 1f)
                {
                    m_Animator.Pause();
                    m_Animator = null;
                }
            }).Every(16);
         }

        private Port GetOutputPort(DependencyType type, Direction direction, string path, int actionIndex, out BuildNode parent)
        {
            foreach (var node in m_BuildNodeCache.Values)
            {
                var port = node.GetPort(type, direction, path, actionIndex);
                if (port != null)
                {
                    parent = node;
                    return port;
                }
            }
            parent = null;
            return null;
        }

        public void PopulateFromData(DagFile data, bool ignorePlayerNode, bool markModifiedNodes)
        {
            m_CurrentNode = null;
            m_Queue = new LinkedList<BuildNode>();

            foreach (var node in data.Nodes)
                m_MaxDepth = Mathf.Max(m_MaxDepth, node.Depth + 1);

            // 1. Clear existing elements
            DeleteElements(graphElements);

            // 2. Map to keep track of created nodes by their JSON ID
            m_BuildNodeCache = new Dictionary<int, BuildNode>();
            m_DagNodeCache = new Dictionary<int, DagNode>();

            foreach (var n in data.Nodes)
                m_DagNodeCache.Add(n.DebugActionIndex, n);

            int maxNodes = data.Nodes.Count;
            int currentNode = 0;
            try
            {
                currentNode = 0;
                foreach (var n in data.Nodes)
                {
                    if (n.Annotation.Equals("all_tundra_nodes"))
                    {
                        Utilities.LogWarning("Skipping node with annotation 'all_tundra_nodes' as it is not relevant for visualization.");
                        continue;
                    }
                    if (ignorePlayerNode && n.Annotation.Equals("Player"))
                    {
                        Utilities.LogWarning("Skipping node with annotation 'Player' as per user settings.");
                        continue;
                    }

                    EditorUtility.DisplayProgressBar($"Creating Node ({currentNode}/{maxNodes})", n.Annotation, (float)currentNode / maxNodes);
                    currentNode++;

                    var node = BuildNode.Create(this, n, m_DagNodeCache);
                    if (markModifiedNodes && n.WasModifiedDuringBuild)
                        node.Color = Color.red;
                    AddElement(node);
                    m_BuildNodeCache.Add(n.DebugActionIndex, node);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            void ConnectPorts(BuildNode inputParent, Port input, BuildNode outputParent, Port output)
            {
                if (output == null || input == null)
                    return;

                var edge = output.ConnectTo(input);
                AddElement(edge);
            }


            try
            {
                currentNode = 0;
                // Connecting input to outputs
                foreach (var buildNode in m_BuildNodeCache.Values)
                {
                    EditorUtility.DisplayProgressBar($"Connecting Nodes ({currentNode}/{maxNodes})", buildNode.DagNode.Annotation, (float)currentNode / maxNodes);
                    currentNode++;
                    foreach (var d in buildNode.DagNode.ToBuildDependencies)
                    {
                        var input = buildNode.GetPort(DependencyType.BuildNode, Direction.Input, null, d);
                        var output = GetOutputPort(DependencyType.BuildNode, Direction.Output, null, d, out var outputPortParent);
                        ConnectPorts(buildNode, input, outputPortParent, output);
                    }

                    foreach (var path in buildNode.DagNode.Inputs)
                    {
                        var input = buildNode.GetPort(DependencyType.File, Direction.Input, path, -1);
                        var output = GetOutputPort(DependencyType.File, Direction.Output, path, -1, out var outputPortParent);
                        ConnectPorts(buildNode, input, outputPortParent, output);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // By collapsing all nodes before the first resize, we can ensure that the initial layout is more compact and easier to read, especially for large graphs.
            // Users can then expand individual nodes as needed to explore details.
            foreach (var buildNode in m_BuildNodeCache.Values)
            {
                buildNode.expanded = false;
            }

            RegisterCallback<GeometryChangedEvent>(OnGraphViewResized);
            schedule.Execute(() => OnGraphViewResized(new GeometryChangedEvent())).ExecuteLater(500);
        }

        private void OnGraphViewResized(GeometryChangedEvent evt)
        {
            UnregisterCallback<GeometryChangedEvent>(OnGraphViewResized);

            var start = DateTime.Now;
            try
            {
                var maxWidthsPerDepth = new float[m_MaxDepth];
                foreach (var n in nodes)
                {
                    var dagNode = ((BuildNode)n).DagNode;
                    var width = n.layout.width;
                    if (width > maxWidthsPerDepth[dagNode.Depth])
                        maxWidthsPerDepth[dagNode.Depth] = width;
                }

                var verticalOffsetCache = new Dictionary<int, float>();
                var horizontalOffsetsPerDepth = new float[m_MaxDepth];
                for (int i = 0; i < maxWidthsPerDepth.Length; i++)
                {
                    if (i == 0)
                        horizontalOffsetsPerDepth[i] = 0;
                    else
                        horizontalOffsetsPerDepth[i] = horizontalOffsetsPerDepth[i - 1] + maxWidthsPerDepth[i - 1] + 20;
                }

                foreach (var n in nodes)
                {
                    var dagNode = ((BuildNode)n).DagNode;
                    var height = n.layout.height;
                    var width = n.layout.width;

                    var position = new Vector2(horizontalOffsetsPerDepth[dagNode.Depth], 0);
                    if (verticalOffsetCache.TryGetValue(dagNode.Depth, out var cachedOffset))
                        position.y += cachedOffset;

                    verticalOffsetCache[dagNode.Depth] = position.y + height + 50;
                    n.SetPosition(new Rect(position, new Vector2(width, height)));
                }

                FocusNode(m_BuildNodeCache.Values.FirstOrDefault(), true, false);
            }
            finally
            {
                var end = DateTime.Now;
                Utilities.Log($"Graph resizing took {(end - start).TotalSeconds} seconds.");
            }
        }
    }
}