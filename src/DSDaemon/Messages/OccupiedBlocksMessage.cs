using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DSDaemon.Messages {
    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/DispatcherComms.MessagesFromRun8")]
    public struct OccupiedBlocksMessage {
        [DataMember] public int Route;
        [DataMember] public List<int> OccupiedBlocks;
        [DataMember] public List<int> OpenManualSwitchBlocks;

        public override string ToString() =>
            $"OccupiedBlocks(route={Route} occupied={OccupiedBlocks?.Count ?? 0} openManual={OpenManualSwitchBlocks?.Count ?? 0})";
    }
}
