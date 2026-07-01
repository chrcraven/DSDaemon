using System.Runtime.Serialization;

namespace DSDaemon.Messages {
    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/DispatcherComms.MessagesFromRun8")]
    public enum EDispatcherPermission {
        [EnumMember] NoChange,
        [EnumMember] Granted,
        [EnumMember] Rescinded,
        [EnumMember] Observer,
    }
}
