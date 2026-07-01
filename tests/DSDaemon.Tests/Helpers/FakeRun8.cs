using System;
using System.Collections.Generic;
using System.Threading;
using DSDaemon.Contracts;
using DSDaemon.Messages;

namespace DSDaemon.Tests.Helpers {
    /// <summary>
    /// Records every outbound IRun8 command issued against it. The Begin*/End*
    /// APM pattern isn't exercised over the wire in tests — we only care that
    /// DispatcherCommander sends the right message.
    /// </summary>
    internal sealed class FakeRun8 : IRun8 {
        public List<object> Sent = new();

        private static IAsyncResult Noop(AsyncCallback? cb, object? state) {
            var result = new FakeAsyncResult(state);
            cb?.Invoke(result);
            return result;
        }

        public IAsyncResult BeginAIRecrewTrain(AIRecrewTrainMessage pMessage, AsyncCallback cb, object state) {
            Sent.Add(pMessage);
            return Noop(cb, state);
        }
        public void EndAIRecrewTrain(IAsyncResult result) { }

        public IAsyncResult BeginChangeSignal(DispatcherSignalMessage pMessage, AsyncCallback cb, object state) {
            Sent.Add(pMessage);
            return Noop(cb, state);
        }
        public void EndChangeSignal(IAsyncResult result) { }

        public IAsyncResult BeginDispatcherConnected(AsyncCallback cb, object state) => Noop(cb, state);
        public void EndDispatcherConnected(IAsyncResult result) { }

        public IAsyncResult BeginHoldAITrain(HoldAITrainMessage pMessage, AsyncCallback cb, object state) {
            Sent.Add(pMessage);
            return Noop(cb, state);
        }
        public void EndHoldAITrain(IAsyncResult result) { }

        public IAsyncResult BeginPing(AsyncCallback cb, object state) => Noop(cb, state);
        public void EndPing(IAsyncResult result) { }

        public IAsyncResult BeginRadioText(RadioTextMessage pMessage, AsyncCallback cb, object state) {
            Sent.Add(pMessage);
            return Noop(cb, state);
        }
        public void EndRadioText(IAsyncResult result) { }

        public IAsyncResult BeginRelinquishAITrain(RelinquishAITrainMessage pMessage, AsyncCallback cb, object state) {
            Sent.Add(pMessage);
            return Noop(cb, state);
        }
        public void EndRelinquishAITrain(IAsyncResult result) { }

        public IAsyncResult BeginStopAITrain(StopAITrainMessage pMessage, AsyncCallback cb, object state) {
            Sent.Add(pMessage);
            return Noop(cb, state);
        }
        public void EndStopAITrain(IAsyncResult result) { }

        public IAsyncResult BeginThrowSwitch(DispatcherSwitchMessage pMessage, AsyncCallback cb, object state) {
            Sent.Add(pMessage);
            return Noop(cb, state);
        }
        public void EndThrowSwitch(IAsyncResult result) { }

        public IAsyncResult BeginTransportPlayer(TransportPlayerMessage pMessage, AsyncCallback cb, object state) {
            Sent.Add(pMessage);
            return Noop(cb, state);
        }
        public void EndTransportPlayer(IAsyncResult result) { }

        public IAsyncResult BeginTransportPlayerToBlock(TransportPlayerToBlockMessage pMessage, AsyncCallback cb, object state) {
            Sent.Add(pMessage);
            return Noop(cb, state);
        }
        public void EndTransportPlayerToBlock(IAsyncResult result) { }

        private sealed class FakeAsyncResult : IAsyncResult {
            public FakeAsyncResult(object? state) => AsyncState = state;
            public object? AsyncState { get; }
            public WaitHandle AsyncWaitHandle => throw new NotSupportedException();
            public bool CompletedSynchronously => true;
            public bool IsCompleted => true;
        }
    }
}
