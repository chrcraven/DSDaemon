using System.Collections.Concurrent;
using System.Threading.Channels;
using DSDaemon.Grpc;

namespace DSDaemon.Server.Sessions {
    /// <summary>
    /// Live, per-session state for one connected agent (one Run8 world instance).
    /// Never shared or merged across sessions — see the session-isolation note in
    /// proto/dsdaemon.proto. This is a scaffold: it records the latest event of
    /// each kind so a session's state can be inspected/tested, but doesn't yet
    /// replicate PtcMonitor's evaluation logic or RouteDiscoveryEngine's scouting
    /// state machine — that logic still needs to move here from the local agent.
    /// </summary>
    public sealed class SessionState {
        public string SessionId { get; }
        public string WorldLabel { get; }
        public DateTimeOffset ConnectedAtUtc { get; } = DateTimeOffset.UtcNow;

        private readonly ConcurrentDictionary<int, TrainData> _trains = new();
        private readonly ConcurrentDictionary<int, Signals> _signalsByRoute = new();
        private readonly ConcurrentDictionary<int, OccupiedBlocks> _occupiedBlocksByRoute = new();

        /// <summary>
        /// Commands queued for this session's agent. DispatcherBridgeService's
        /// stream-write loop drains this and writes each command to the gRPC
        /// response stream for this session only.
        /// </summary>
        public Channel<ServerCommand> Outbound { get; } = Channel.CreateUnbounded<ServerCommand>();

        public IReadOnlyDictionary<int, TrainData> Trains => _trains;
        public IReadOnlyDictionary<int, Signals> SignalsByRoute => _signalsByRoute;
        public IReadOnlyDictionary<int, OccupiedBlocks> OccupiedBlocksByRoute => _occupiedBlocksByRoute;

        public SessionState(string sessionId, string worldLabel) {
            SessionId  = sessionId;
            WorldLabel = worldLabel;
        }

        /// <summary>
        /// Folds one inbound AgentEvent into this session's state. Additional
        /// payload kinds (radio, DTMF, permission, switch messages) are relayed
        /// through as events without persistent state for now.
        /// </summary>
        public void Apply(AgentEvent evt) {
            switch (evt.PayloadCase) {
                case AgentEvent.PayloadOneofCase.Train:
                    _trains[evt.Train.TrainId] = evt.Train;
                    break;
                case AgentEvent.PayloadOneofCase.Signals:
                    _signalsByRoute[evt.Signals.Route] = evt.Signals;
                    break;
                case AgentEvent.PayloadOneofCase.OccupiedBlocks:
                    _occupiedBlocksByRoute[evt.OccupiedBlocks.Route] = evt.OccupiedBlocks;
                    break;
            }
        }

        public void Enqueue(ServerCommand command) => Outbound.Writer.TryWrite(command);

        public void Close() => Outbound.Writer.TryComplete();
    }
}
