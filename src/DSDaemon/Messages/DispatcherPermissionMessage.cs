using System.Runtime.Serialization;

namespace DSDaemon.Messages {
    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/DispatcherComms.MessagesFromRun8")]
    public struct DispatcherPermissionMessage {
        [DataMember] public bool AIPermission;
        [DataMember] public EDispatcherPermission Permission;

        public override string ToString() =>
            $"Permission({Permission}, AI={AIPermission})";
    }
}
