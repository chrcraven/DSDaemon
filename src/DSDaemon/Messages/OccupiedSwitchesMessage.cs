using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DSDaemon.Messages {
    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/DispatcherComms.MessagesFromRun8")]
    public struct OccupiedSwitchesMessage {
        [DataMember] public int Route;
        [DataMember] public List<int> OccupiedSwitches;

        public override string ToString() =>
            $"OccupiedSwitches(route={Route} count={OccupiedSwitches?.Count ?? 0})";
    }
}
