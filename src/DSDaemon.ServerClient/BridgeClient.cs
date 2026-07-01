using DSDaemon.Grpc;
using Grpc.Core;
using Grpc.Net.Client;

namespace DSDaemon.ServerClient {
    /// <summary>
    /// Thin wrapper around the generated gRPC clients for DSDaemon.Server.
    ///
    /// Not yet wired into the local agent's Program.cs — this is scaffolding
    /// for the future "WCF-to-gRPC proxy" mode, where DispatcherCallback
    /// would translate each IDispatcher callback into an AgentEvent and call
    /// SendEventAsync, and something reading ReadCommandsAsync would
    /// translate each ServerCommand back into the matching
    /// IDispatcherCommander call, instead of driving PtcMonitor /
    /// RouteDiscoveryEngine locally as DSDaemon does today.
    /// </summary>
    public sealed class BridgeClient : IAsyncDisposable {
        private readonly GrpcChannel _channel;
        private readonly DispatcherBridge.DispatcherBridgeClient _bridge;
        private readonly SessionRegistry.SessionRegistryClient _registry;

        private AsyncDuplexStreamingCall<AgentEvent, ServerCommand>? _stream;

        public string? SessionId { get; private set; }

        public BridgeClient(string serverAddress) {
            _channel  = GrpcChannel.ForAddress(serverAddress);
            _bridge   = new DispatcherBridge.DispatcherBridgeClient(_channel);
            _registry = new SessionRegistry.SessionRegistryClient(_channel);
        }

        /// <summary>Opens a session, then the streaming call carrying that session's id.</summary>
        public async Task ConnectAsync(string agentToken, string worldLabel, string agentVersion) {
            var reply = await _registry.OpenSessionAsync(new OpenSessionRequest {
                AgentToken   = agentToken,
                WorldLabel   = worldLabel,
                AgentVersion = agentVersion,
            });
            SessionId = reply.SessionId;

            var headers = new Metadata { { "session-id", SessionId } };
            _stream = _bridge.Stream(headers);
        }

        public Task SendEventAsync(AgentEvent evt) {
            if (_stream == null) throw new InvalidOperationException("Call ConnectAsync first.");
            return _stream.RequestStream.WriteAsync(evt);
        }

        public IAsyncEnumerable<ServerCommand> ReadCommandsAsync(CancellationToken ct = default) {
            if (_stream == null) throw new InvalidOperationException("Call ConnectAsync first.");
            return _stream.ResponseStream.ReadAllAsync(ct);
        }

        public async ValueTask DisposeAsync() {
            if (_stream != null) {
                await _stream.RequestStream.CompleteAsync();
                _stream.Dispose();
            }
            if (SessionId != null)
                await _registry.CloseSessionAsync(new CloseSessionRequest { SessionId = SessionId });

            _channel.Dispose();
        }
    }
}
