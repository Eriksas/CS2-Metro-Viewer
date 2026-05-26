using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CS2_Metro
{
    [DataContract]
    public class TransportDebugDumpDocument
    {
        [DataMember(Order = 1)]
        public string dumpVersion { get; set; }

        [DataMember(Order = 2)]
        public string exportedAtUtc { get; set; }

        [DataMember(Order = 3)]
        public string jsonPath { get; set; }

        [DataMember(Order = 4)]
        public string textPath { get; set; }

        [DataMember(Order = 5)]
        public int sampleLimitPerCandidateType { get; set; }

        [DataMember(Order = 6)]
        public TransportDebugEnvironment environment { get; set; }

        [DataMember(Order = 7)]
        public List<TransportCandidateGroup> candidateGroups { get; set; } = new List<TransportCandidateGroup>();

        [DataMember(Order = 8)]
        public List<string> warnings { get; set; } = new List<string>();

        [DataMember(Order = 9)]
        public List<string> exceptions { get; set; } = new List<string>();
    }

    [DataContract]
    public class TransportDebugEnvironment
    {
        [DataMember(Order = 1)]
        public bool hasUpdateSystem { get; set; }

        [DataMember(Order = 2)]
        public bool hasWorld { get; set; }

        [DataMember(Order = 3)]
        public bool worldIsCreated { get; set; }

        [DataMember(Order = 4)]
        public string worldName { get; set; }

        [DataMember(Order = 5)]
        public string gameMode { get; set; }

        [DataMember(Order = 6)]
        public string gameState { get; set; }

        [DataMember(Order = 7)]
        public string shouldUpdateWorld { get; set; }

        [DataMember(Order = 8)]
        public string isGameLoading { get; set; }

        [DataMember(Order = 9)]
        public int totalEntityCount { get; set; }

        [DataMember(Order = 10)]
        public int candidateEntityCount { get; set; }

        [DataMember(Order = 11)]
        public int candidateComponentTypeCount { get; set; }
    }

    [DataContract]
    public class TransportCandidateGroup
    {
        [DataMember(Order = 1)]
        public string componentType { get; set; }

        [DataMember(Order = 2)]
        public string componentNamespace { get; set; }

        [DataMember(Order = 3)]
        public string componentCategory { get; set; }

        [DataMember(Order = 4)]
        public int totalEntityCount { get; set; }

        [DataMember(Order = 5)]
        public List<string> matchedKeywords { get; set; } = new List<string>();

        [DataMember(Order = 6)]
        public List<TransportEntitySample> samples { get; set; } = new List<TransportEntitySample>();

        [DataMember(Order = 7)]
        public List<string> errors { get; set; } = new List<string>();
    }

    [DataContract]
    public class TransportEntitySample
    {
        [DataMember(Order = 1)]
        public string entity { get; set; }

        [DataMember(Order = 2)]
        public int entityIndex { get; set; }

        [DataMember(Order = 3)]
        public int entityVersion { get; set; }

        [DataMember(Order = 4)]
        public string entityName { get; set; }

        [DataMember(Order = 5)]
        public List<string> componentTypes { get; set; } = new List<string>();

        [DataMember(Order = 6)]
        public List<ComponentReadResult> componentData { get; set; } = new List<ComponentReadResult>();
    }

    [DataContract]
    public class ComponentReadResult
    {
        [DataMember(Order = 1)]
        public string componentType { get; set; }

        [DataMember(Order = 2)]
        public string category { get; set; }

        [DataMember(Order = 3)]
        public string summary { get; set; }

        [DataMember(Order = 4)]
        public int? bufferLength { get; set; }

        [DataMember(Order = 5)]
        public List<FieldReadResult> fields { get; set; } = new List<FieldReadResult>();

        [DataMember(Order = 6)]
        public List<string> bufferSamples { get; set; } = new List<string>();

        [DataMember(Order = 7)]
        public string error { get; set; }
    }

    [DataContract]
    public class FieldReadResult
    {
        [DataMember(Order = 1)]
        public string name { get; set; }

        [DataMember(Order = 2)]
        public string value { get; set; }
    }
}

