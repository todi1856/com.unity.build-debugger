using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.MemoryProfiler;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.BuildDebugger
{
    public class BuildNode : Node
    {
        private BuildGraphView m_Parent;
        private IReadOnlyDictionary<int, DagNode> m_DagNodeCache;
        public DagNode DagNode { get; }
        public Label Title { get; }

        public Color Color 
        { 
            set
            {
                style.color = value;
                Title.style.color = value;
            }
        }

        Dictionary<string, Port> m_InputPortsAsPaths = new Dictionary<string, Port>();
        Dictionary<string, Port> m_OutputPortsAsPaths = new Dictionary<string, Port>();
        Dictionary<int, Port> m_InputPortsAsBuildNodes = new Dictionary<int, Port>();
        Dictionary<int, Port> m_OutputPortsAsBuildNodes = new Dictionary<int, Port>();

        public IEnumerable<Port> EnumerateInputPorts()
        {
            foreach (var port in m_InputPortsAsPaths.Values)
                yield return port;
            foreach (var port in m_InputPortsAsBuildNodes.Values)
                yield return port;
        }

        public IEnumerable<Port> EnumerateOutputPorts()
        {
            foreach (var port in m_OutputPortsAsPaths.Values)
                yield return port;
            foreach (var port in m_OutputPortsAsBuildNodes.Values)
                yield return port;
        }

        public IEnumerable<Port> EnumerateAllPorts()
        {
            foreach (var port in EnumerateInputPorts())
                yield return port;
            foreach (var port in EnumerateOutputPorts())
                yield return port;
        }

        private BuildNode(BuildGraphView parent, DagNode node, IReadOnlyDictionary<int, DagNode> dagNodeCache)
        {
            m_Parent = parent;
            DagNode = node;
            m_DagNodeCache = dagNodeCache;
            Title = this.Q<Label>();
        }

        public static BuildNode Create(BuildGraphView parent, DagNode dagNode, IReadOnlyDictionary<int, DagNode> dagNodeCache)
        {
            var node = new BuildNode(parent, dagNode, dagNodeCache) { title = $"({dagNode.DebugActionIndex}){dagNode.Annotation}" };
            node.tooltip = dagNode.Action;

            for (int i = 0; i < dagNode.ToBuildDependencies.Count; i++)
            {
                var idx = dagNode.ToBuildDependencies[i];
                node.AddInputPortBuildNode(node, idx);
            }

            for (int i = 0; i < dagNode.Inputs.Count; i++)
                node.AddInputPortFile(node, dagNode.Inputs[i]);

            node.AddOutputPortBuildNode(node, dagNode.DebugActionIndex);
            for (int i = 0; i < dagNode.Outputs.Count; i++)
                node.AddOutputPortFile(node, dagNode.Outputs[i]);

            node.RefreshExpandedState();
            node.RefreshPorts();

            return node;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (evt.target is Node)
            {
                evt.menu.AppendAction("Copy All", action =>
                {
                    var builder = new StringBuilder();
                    builder.AppendLine($"Node: {title}");
                    builder.AppendLine("Inputs:");
                    foreach (var port in EnumerateInputPorts())
                    {
                        builder.AppendLine($"  - {port.portName}");
                    }
                    builder.AppendLine("Outputs:");
                    foreach (var port in EnumerateOutputPorts())
                    {
                        builder.AppendLine($"  - {port.portName}");
                    }

                    EditorGUIUtility.systemCopyBuffer = builder.ToString();
                });
                evt.menu.AppendAction("Copy Action", action =>
                {
                    EditorGUIUtility.systemCopyBuffer = DagNode.Action;
                });
                evt.menu.AppendAction("Copy Dag Node", action =>
                {
                    EditorGUIUtility.systemCopyBuffer = JsonUtility.ToJson(DagNode, true);
                });

                evt.menu.AppendAction("Why this node was built?", action =>
                {
                    InfoWindow.Open(BeeUtilities.ExecuteBeeWhyStandalone(DagNode.Annotation));
                });
            }
        }

        private string GetBuildNodeName(int actionIndex)
        {
            if (m_DagNodeCache.TryGetValue(actionIndex, out var node))
                return node.Annotation;
            throw new System.ArgumentException($"No node found in cache with action index {actionIndex}");
        }

        public Port AddInputPortFile(BuildNode parent, string path)
        {
            var port = AddPort(path, Direction.Input);
            port.Add(new PortInformation(parent, DependencyType.File, path, -1));
            if (!m_InputPortsAsPaths.TryAdd(path, port))
                Debug.LogWarning($"Input port with name {path} already exists in node {DagNode.Annotation}");
            return port;
        }

        public Port AddInputPortBuildNode(BuildNode parent, int actionIndex)
        {
            var path = GetBuildNodeName(actionIndex);
            var port = AddPort($"({actionIndex}){path}", Direction.Input);
            port.Add(new PortInformation(parent,DependencyType.BuildNode, null, actionIndex));
            if (!m_InputPortsAsBuildNodes.TryAdd(actionIndex, port))
                Debug.LogWarning($"Input port with index {actionIndex} already exists in node {DagNode.Annotation}");
            return port;
        }

        public Port AddOutputPortFile(BuildNode parent, string path)
        {
            var port = AddPort(path, Direction.Output);
            port.Add(new PortInformation(parent,DependencyType.File, path, -1));
            if (!m_OutputPortsAsPaths.TryAdd(path, port))
                Debug.LogWarning($"Output port with name {path} already exists in node {DagNode.Annotation}");
            return port;
        }

        public Port AddOutputPortBuildNode(BuildNode parent, int actionIndex)
        {
            // The build node name matches output build node name
            // So no need to duplicate it
            var path = $"({actionIndex})Output";//  GetBuildNodeName(actionIndex);
            var port = AddPort(path, Direction.Output);
            port.Add(new PortInformation(parent, DependencyType.BuildNode, null, actionIndex));
            if (!m_OutputPortsAsBuildNodes.TryAdd(actionIndex, port))
                Debug.LogWarning($"Output port with index {actionIndex} already exists in node {DagNode.Annotation}");
            return port;
        }

        public Port AddPort(string name, Direction direction)
        {
            var orient = Orientation.Horizontal;
            var cap = Port.Capacity.Multi;
            var type = typeof(float);

            var port = InstantiatePort(orient, direction, cap, type);

            // Initialize port details
            {
                port.portName = name;
                var l = port.Q<Label>();
                var container = new VisualElement();
                container.style.flexDirection = FlexDirection.Row;
                container.AddToClassList("connectorText");
                var btn = new Button();
                btn.style.marginBottom = btn.style.marginTop = 0;
                btn.clicked += () =>
                {
                    var connections = port.connections.ToList();
                    if (connections.Count == 0)
                        return;

                    static void FocusConnectedNode(BuildGraphView parent, Port port, Node targetNode, Direction direction)
                    {
                        var portInfo = port.Q<PortInformation>();
                        parent.PushNode(portInfo.Owner);
                        parent.FocusNode(targetNode, false);
                    }

                    if (connections.Count == 1)
                    {
                        var node = direction == Direction.Input ?
                            connections[0].output.Q<PortInformation>().Owner :
                            connections[0].input.Q<PortInformation>().Owner;
                        FocusConnectedNode(m_Parent, port, node, direction);
                    }
                    else
                    {
                        var menu = new GenericMenu();
                        foreach (var connection in connections)
                        {
                            var node = direction == Direction.Input ?
                                connection.output.Q<PortInformation>().Owner :
                                connection.input.Q<PortInformation>().Owner;
                            menu.AddItem(new GUIContent(node.title.Replace('/', '\\')), false, () =>
                            {
                                FocusConnectedNode(m_Parent, port, node, direction);
                            });
                        }

                        menu.ShowAsContext();
                    }
                };

                if (direction == Direction.Input)
                {
                    btn.text = "<";
                    container.Add(btn);
                    container.Add(l);
                }
                else
                {
                    btn.text = ">";
                    container.Add(l);
                    container.Add(btn);
                }
                port.Add(container);
            }

            switch (direction)
            {
                case Direction.Input:
                    inputContainer.Add(port);
                    break;
                case Direction.Output:
                    outputContainer.Add(port);
                    break;
            }
            return port;
        }

        public Port GetPort(DependencyType dependencyType, Direction direction, string path, int actionIndex)
        {
            switch (dependencyType)
            {
                case DependencyType.File:
                    switch (direction)
                    {
                        case Direction.Input:
                            if (m_InputPortsAsPaths.TryGetValue(path, out var inputPort))
                                return inputPort;
                            break;
                        case Direction.Output:
                            if (m_OutputPortsAsPaths.TryGetValue(path, out var outputPort))
                                return outputPort;
                            break;
                    }
                    break;
                case DependencyType.BuildNode:
                    switch (direction)
                    {
                        case Direction.Input:
                            if (m_InputPortsAsBuildNodes.TryGetValue(actionIndex, out var inputPort))
                                return inputPort;
                            break;
                        case Direction.Output:
                            if (m_OutputPortsAsBuildNodes.TryGetValue(actionIndex, out var outputPort))
                                return outputPort;
                            break;
                    }
                    break;
                default:
                    throw new System.ArgumentException($"Unknown dependency type {dependencyType}");
            }


            return null;
        }
    }
}