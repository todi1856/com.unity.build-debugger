using UnityEngine.UIElements;

namespace Unity.BuildDebugger
{
    public class PortInformation : VisualElement
    {
        public DependencyType DependencyType { get; private set; }

        // Data for DependencyType.File
        public string Path { get; private set; }

        // Data for DependencyType.BuildNode
        public int ActionIndex { get; private set; }

        public BuildNode Owner { get; private set; }

        public PortInformation(BuildNode owner, DependencyType dependencyType, string path, int actionIndex)
        {
            Owner = owner;
            DependencyType = dependencyType;
            Path = path;
            ActionIndex = actionIndex;
        }
    }
}