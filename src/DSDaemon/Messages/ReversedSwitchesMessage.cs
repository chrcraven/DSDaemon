using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DSDaemon.Messages {
    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/DispatcherComms.MessagesFromRun8")]
    public struct ReversedSwitchesMessage {
        [DataMember] public int Route;
        [DataMember] public List<int> ReversedSwitches;

        public override string ToString() =>
            $"ReversedSwitches(route={Route} reversed=[{string.Join(",", ReversedSwitches ?? new List<int>())}])";
    }
}
