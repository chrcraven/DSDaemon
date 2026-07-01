using System.Runtime.Serialization;

namespace DSDaemon.Messages {
    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/DispatcherComms.MessagesFromRun8")]
    public struct TrainDataMessage {
        [DataMember(IsRequired = true)] public TrainData Train;

        public override string ToString() => Train.ToString();
    }
}
