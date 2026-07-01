using System.Runtime.Serialization;

namespace DSDaemon.Messages {
    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/DispatcherComms.MessagesFromDispatcher")]
    public struct DispatcherSignalMessage {
        [DataMember] public int SignalID;
        [DataMember] public ESignalIndication SignalIndication;
        [DataMember] public bool AutomaticWorking;
    }
}
