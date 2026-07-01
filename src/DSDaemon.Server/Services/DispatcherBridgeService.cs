using DSDaemon.Grpc;
using DSDaemon.Server.Sessions;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace DSDaemon.Server.Services {
    /// <summary>
    /// Relays one agent's event/command stream. The heavy lifting (PTC
    /// evaluation, route-discovery scouting decisions) doesn't live here yet —
    /// today this just folds inbound events into the session's state
    /// (SessionState.Apply) and drains whatever commands land in the
    /// session's outbound queue back to the agent. Server-side logic that
    /// wants to command Run8 enqueues onto SessionState.Outbound; it doesn't
    /// need to know which gRPC call is servicing that session.
    /// </summary>
    public sealed class DispatcherBridgeService : DispatcherBridge.DispatcherBridgeBase {
        private const string SessionIdHeader = "session-id";

        private readonly SessionManager _sessions;
        private readonly ILogger<DispatcherBridgeService> _log;

        public DispatcherBridgeService(SessionManager sessions, ILogger<DispatcherBridgeService> log) {
            _sessions = sessions;
            _log      = log;
        }

        public override async Task Stream(
            IAsyncStreamReader<AgentEvent> requestStream,
            IServerStreamWriter<ServerCommand> responseStream,
            ServerCallContext context) {

            var sessionId = context.RequestHeaders.GetValue(SessionIdHeader);
            if (sessionId == null || !_sessions.TryGet(sessionId, out var session))
                throw new RpcException(new Status(StatusCode.Unauthenticated,
                    $"Missing or unknown '{SessionIdHeader}' — call OpenSession first"));

            _log.LogInformation("Session {SessionId} stream opened", sessionId);
            var pump = PumpCommandsAsync(session, responseStream, context.CancellationToken);

            try {
                await foreach (var evt in requestStream.ReadAllAsync(context.CancellationToken))
                    session.Apply(evt);
            } finally {
                _sessions.Remove(sessionId);
                _log.LogInformation("Session {SessionId} stream closed", sessionId);
                try { await pump; } catch (OperationCanceledException) { /* expected on disconnect */ }
            }
        }

        private static async Task PumpCommandsAsync(
            SessionState session,
            IServerStreamWriter<ServerCommand> responseStream,
            CancellationToken ct) {
            await foreach (var command in session.Outbound.Reader.ReadAllAsync(ct))
                await responseStream.WriteAsync(command);
        }
    }
}
