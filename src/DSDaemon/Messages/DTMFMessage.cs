using System.Runtime.Serialization;

namespace DSDaemon.Messages {
    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/DispatcherComms.MessagesFromRun8")]
    public struct DTMFMessage {
        [DataMember(IsRequired = false)] public int Channel;
        [DataMember(IsRequired = false)] public EDTMFType DTMFType;
        [DataMember(IsRequired = false)] public string Tone;
        [DataMember(IsRequired = false)] public string TowerDescription;

        public override string ToString() =>
            $"DTMF(ch={Channel} type={DTMFType} tone={Tone} tower={TowerDescription})";
    }
}
