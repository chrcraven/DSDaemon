using System.Runtime.Serialization;

namespace DSDaemon.Messages {
    // Namespace is MessagesFromDispatcher because signal indications flow dispatcher→Run8
    // (the dispatcher sets signals); Run8 echoes them back in SetSignals callbacks.
    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/DispatcherComms.MessagesFromDispatcher")]
    public enum ESignalIndication {
        [EnumMember] Stop,
        [EnumMember] Proceed,
        [EnumMember] Fleet,
        [EnumMember] FlagBy,
    }
}
