using System;
using System.ServiceModel;
using DSDaemon.Messages;

namespace DSDaemon.Contracts {
    /// <summary>
    /// Service contract exposed by Run8. Matches iecc8/IRun8.cs wire-for-wire.
    /// Name and SessionMode must be identical to what Run8 registers.
    /// </summary>
    [ServiceContract(
        CallbackContract = typeof(IDispatcher),
        Name = "IWCFRun8",
        SessionMode = SessionMode.Required)]
    public interface IRun8 {
        [OperationContract(AsyncPattern = true, IsOneWay = true)]
        IAsyncResult BeginAIRecrewTrain(AIRecrewTrainMessage pMessage, AsyncCallback cb, object state);
        void EndAIRecrewTrain(IAsyncResult result);

        [OperationContract(AsyncPattern = true, IsOneWay = true)]
        IAsyncResult BeginChangeSignal(DispatcherSignalMessage pMessage, AsyncCallback cb, object state);
        void EndChangeSignal(IAsyncResult result);

        [OperationContract(AsyncPattern = true, IsOneWay = true)]
        IAsyncResult BeginDispatcherConnected(AsyncCallback cb, object state);
        void EndDispatcherConnected(IAsyncResult result);

        [OperationContract(AsyncPattern = true, IsOneWay = true)]
        IAsyncResult BeginHoldAITrain(HoldAITrainMessage pMessage, AsyncCallback cb, object state);
        void EndHoldAITrain(IAsyncResult result);

        [OperationContract(AsyncPattern = true, IsOneWay = true)]
        IAsyncResult BeginPing(AsyncCallback cb, object state);
        void EndPing(IAsyncResult result);

        [OperationContract(AsyncPattern = true, IsOneWay = true)]
        IAsyncResult BeginRadioText(RadioTextMessage pMessage, AsyncCallback cb, object state);
        void EndRadioText(IAsyncResult result);

        [OperationContract(AsyncPattern = true, IsOneWay = true)]
        IAsyncResult BeginRelinquishAITrain(RelinquishAITrainMessage pMessage, AsyncCallback cb, object state);
        void EndRelinquishAITrain(IAsyncResult result);

        [OperationContract(AsyncPattern = true, IsOneWay = true)]
        IAsyncResult BeginStopAITrain(StopAITrainMessage pMessage, AsyncCallback cb, object state);
        void EndStopAITrain(IAsyncResult result);

        [OperationContract(AsyncPattern = true, IsOneWay = true)]
        IAsyncResult BeginThrowSwitch(DispatcherSwitchMessage pMessage, AsyncCallback cb, object state);
        void EndThrowSwitch(IAsyncResult result);

        [OperationContract(AsyncPattern = true, IsOneWay = true)]
        IAsyncResult BeginTransportPlayer(TransportPlayerMessage pMessage, AsyncCallback cb, object state);
        void EndTransportPlayer(IAsyncResult result);

        [OperationContract(AsyncPattern = true, IsOneWay = true)]
        IAsyncResult BeginTransportPlayerToBlock(TransportPlayerToBlockMessage pMessage, AsyncCallback cb, object state);
        void EndTransportPlayerToBlock(IAsyncResult result);
    }
}
