using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DSDaemon.Messages {
    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/DispatcherComms.MessagesFromRun8")]
    public struct UnlockedSwitchesMessage {
        [DataMember] public int Route;
        [DataMember] public List<int> UnlockedSwitches;

        public override string ToString() =>
            $"UnlockedSwitches(route={Route} count={UnlockedSwitches?.Count ?? 0})";
    }
}
