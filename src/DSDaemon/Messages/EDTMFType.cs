using System.Runtime.Serialization;

namespace DSDaemon.Messages {
    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/DispatcherComms.MessagesFromDispatcher")]
    public enum EDTMFType {
        [EnumMember] None,
        [EnumMember] DispatchTone,
        [EnumMember] EmergencyTone,
    }
}
