using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DSDaemon.Messages {
    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/DispatcherComms.MessagesFromRun8")]
    public struct InterlockErrorSwitchesMessage {
        [DataMember] public int Route;
        [DataMember] public List<int> InterlockErrorSwitches;

        public override string ToString() =>
            $"InterlockErrors(route={Route} count={InterlockErrorSwitches?.Count ?? 0})";
    }
}
