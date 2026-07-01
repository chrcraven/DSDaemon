using DSDaemon.Grpc;
using DSDaemon.Server.Sessions;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace DSDaemon.Server.Services {
    public sealed class SessionRegistryService : SessionRegistry.SessionRegistryBase {
        private readonly SessionManager _sessions;
        private readonly ILogger<SessionRegistryService> _log;

        public SessionRegistryService(SessionManager sessions, ILogger<SessionRegistryService> log) {
            _sessions = sessions;
            _log      = log;
        }

        public override Task<OpenSessionReply> OpenSession(OpenSessionRequest request, ServerCallContext context) {
            // TODO: validate request.AgentToken against real per-install credentials
            // before trusting a session — this scaffold accepts any token.
            var session = _sessions.Create(request.WorldLabel);
            _log.LogInformation(
                "Session {SessionId} opened for world '{WorldLabel}' (agent v{Version})",
                session.SessionId, request.WorldLabel, request.AgentVersion);

            return Task.FromResult(new OpenSessionReply { SessionId = session.SessionId });
        }

        public override Task<CloseSessionReply> CloseSession(CloseSessionRequest request, ServerCallContext context) {
            _sessions.Remove(request.SessionId);
            _log.LogInformation("Session {SessionId} closed", request.SessionId);
            return Task.FromResult(new CloseSessionReply());
        }
    }
}
