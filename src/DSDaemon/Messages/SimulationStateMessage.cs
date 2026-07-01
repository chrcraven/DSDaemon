using System;
using System.Runtime.Serialization;

namespace DSDaemon.Messages {
    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/DispatcherComms.MessagesFromRun8")]
    public struct SimulationStateMessage {
        [DataMember(IsRequired = true)] public bool IsClient;
        [DataMember(IsRequired = true)] public DateTime SimulationTime;

        public override string ToString() =>
            $"SimState(time={SimulationTime:yyyy-MM-dd HH:mm:ss} isClient={IsClient})";
    }
}
