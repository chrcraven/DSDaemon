using System.Runtime.Serialization;

namespace DSDaemon.Messages {
    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/DispatcherComms.MessagesFromRun8")]
    public struct RadioTextMessage {
        [DataMember(IsRequired = true)] public int Channel;
        [DataMember(IsRequired = true)] public string Text;

        public override string ToString() => $"Radio(ch={Channel} \"{Text}\")";
    }
}
