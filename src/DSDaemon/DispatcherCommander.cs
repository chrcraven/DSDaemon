using DSDaemon.Contracts;
using DSDaemon.Messages;

namespace DSDaemon {

    public interface ITrainCommander {
        void Hold(int trainId);
        void Release(int trainId);
    }

    /// <summary>
    /// Full outbound command surface: signals, switches, and AI train orders.
    /// Extends ITrainCommander so callers that only need Hold/Release (e.g. code
    /// depending on the narrower interface) still work against it unchanged.
    /// RouteDiscoveryEngine itself takes the full interface — it also clears
    /// signals/switches autonomously to aid its released scout.
    /// </summary>
    public interface IDispatcherCommander : ITrainCommander {
        void ChangeSignal(int signalId, ESignalIndication indication, bool automaticWorking = false);
        void ThrowSwitch(int switchId, ESwitchState state);
        void Stop(int trainId);
        void Recrew(int trainId);
        void Relinquish(int trainId, bool relinquish = true);
    }

    /// <summary>
    /// Wraps IRun8's dispatcher→Run8 commands (signals, switches, AI train orders)
    /// so callers never touch the WCF channel directly.
    /// </summary>
    public sealed class DispatcherCommander : IDispatcherCommander {
        private readonly IRun8 _channel;
        public DispatcherCommander(IRun8 channel) => _channel = channel;

        public void Hold(int trainId) =>
            _channel.BeginHoldAITrain(
                new HoldAITrainMessage { TrainID = trainId, HoldTrain = true }, null, null);

        public void Release(int trainId) =>
            _channel.BeginHoldAITrain(
                new HoldAITrainMessage { TrainID = trainId, HoldTrain = false }, null, null);

        public void ChangeSignal(int signalId, ESignalIndication indication, bool automaticWorking = false) =>
            _channel.BeginChangeSignal(
                new DispatcherSignalMessage {
                    SignalID         = signalId,
                    SignalIndication = indication,
                    AutomaticWorking = automaticWorking,
                }, null, null);

        public void ThrowSwitch(int switchId, ESwitchState state) =>
            _channel.BeginThrowSwitch(
                new DispatcherSwitchMessage { SwitchID = switchId, SwitchState = state }, null, null);

        public void Stop(int trainId) =>
            _channel.BeginStopAITrain(new StopAITrainMessage { TrainID = trainId }, null, null);

        public void Recrew(int trainId) =>
            _channel.BeginAIRecrewTrain(new AIRecrewTrainMessage { TrainID = trainId }, null, null);

        public void Relinquish(int trainId, bool relinquish = true) =>
            _channel.BeginRelinquishAITrain(
                new RelinquishAITrainMessage { TrainID = trainId, RelinquishTrain = relinquish }, null, null);
    }
}
