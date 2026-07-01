using DSDaemon.Contracts;
using DSDaemon.Messages;

namespace DSDaemon {

    public interface ITrainCommander {
        void Hold(int trainId);
        void Release(int trainId);
    }

    /// <summary>
    /// Wraps BeginHoldAITrain so discovery logic doesn't touch IRun8 directly.
    /// HoldTrain=true pauses at next signal; HoldTrain=false resumes.
    /// </summary>
    public sealed class TrainCommander : ITrainCommander {
        private readonly IRun8 _channel;
        public TrainCommander(IRun8 channel) => _channel = channel;

        public void Hold(int trainId) =>
            _channel.BeginHoldAITrain(
                new HoldAITrainMessage { TrainID = trainId, HoldTrain = true }, null, null);

        public void Release(int trainId) =>
            _channel.BeginHoldAITrain(
                new HoldAITrainMessage { TrainID = trainId, HoldTrain = false }, null, null);
    }
}
