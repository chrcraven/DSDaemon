using System.Collections.Concurrent;
using System.Linq;

namespace DSDaemon.Server.Sessions {
    /// <summary>
    /// Registry of live sessions, keyed by the opaque session_id handed out by
    /// SessionRegistryService.OpenSession. One entry per connected agent (one
    /// Run8 world instance). In-memory only for this scaffold — a real
    /// deployment would want to survive a server restart without losing live
    /// PTC state, but that's a later concern; the discovered route topology
    /// (not modeled here yet) is the part that actually needs durable storage.
    /// </summary>
    public sealed class SessionManager {
        private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

        public SessionState Create(string worldLabel) {
            var sessionId = Guid.NewGuid().ToString("N");
            var state = new SessionState(sessionId, worldLabel);
            _sessions[sessionId] = state;
            return state;
        }

        public bool TryGet(string sessionId, out SessionState state) =>
            _sessions.TryGetValue(sessionId, out state!);

        public void Remove(string sessionId) {
            if (_sessions.TryRemove(sessionId, out var state))
                state.Close();
        }

        public IReadOnlyCollection<SessionState> ActiveSessions => _sessions.Values.ToList();
    }
}
