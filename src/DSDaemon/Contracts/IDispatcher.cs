using System.ServiceModel;
using DSDaemon.Messages;

namespace DSDaemon.Contracts {
    /// <summary>
    /// Callback contract that Run8 invokes to push state changes to the dispatcher.
    /// Mirrors iecc8/IDispatcher.cs exactly so the WCF wire contract matches.
    /// </summary>
    public interface IDispatcher {
        [OperationContract(IsOneWay = true)]
        void DTMF(DTMFMessage pMessage);

        [OperationContract(IsOneWay = true)]
        void PermissionUpdate(DispatcherPermissionMessage pMessage);

        [OperationContract(IsOneWay = true)]
        void Ping();

        [OperationContract(IsOneWay = true)]
        void RadioText(RadioTextMessage pMessage);

        [OperationContract(IsOneWay = true)]
        void SendSimulationState(SimulationStateMessage pMessage);

        [OperationContract(IsOneWay = true)]
        void SetInterlockErrorSwitches(InterlockErrorSwitchesMessage pMessage);

        [OperationContract(IsOneWay = true)]
        void SetOccupiedSwitches(OccupiedSwitchesMessage pMessage);

        [OperationContract(IsOneWay = true)]
        void SetOccupiedBlocks(OccupiedBlocksMessage pMessage);

        [OperationContract(IsOneWay = true)]
        void SetReversedSwitches(ReversedSwitchesMessage pMessage);

        [OperationContract(IsOneWay = true)]
        void SetSignals(SignalsMessage pMessage);

        [OperationContract(IsOneWay = true)]
        void SetUnlockedSwitches(UnlockedSwitchesMessage pMessage);

        [OperationContract(IsOneWay = true)]
        void UpdateTrainData(TrainDataMessage pMessage);
    }
}
