using System;
using System.Collections.Generic;

namespace Unity.BuildDebugger
{
    [Serializable]
    public class DagFile
    {
        public List<DagNode> Nodes;
    }

    [Serializable]
    public class DagNode
    {
        public string Annotation;
        public string DisplayName;

        public string ActionType;
        public string Action;

        public List<string> Inputs;
        public List<int> InputFlags;

        public List<string> Outputs;
        public List<int> OutputFlags;

        public List<int> ToBuildDependencies;

        public int DebugActionIndex;

        // Optional fields (not always present)
        public int PayloadOffset;
        public int PayloadLength;
        public string PayloadDebugContentSnippet;

        public bool RunBeforeDependentNodeLeafInputIsCalculated;

        [NonSerialized]
        public int Depth;

        public override string ToString()
        {
            return $"DagNode: {DisplayName} (Action: {ActionType}:{Action})";
        }
    }
}