using DSDaemon.Grpc;
using DSDaemon.Server.Sessions;
using Xunit;

namespace DSDaemon.Server.Tests {
    /// <summary>
    /// SessionManager/SessionState: registry bookkeeping and the
    /// per-session isolation guarantee the server design depends on
    /// (see the session-isolation note in proto/dsdaemon.proto).
    /// </summary>
    public class SessionManagerTests {

        [Fact]
        public void Create_ReturnsRetrievableSession() {
            var mgr     = new SessionManager();
            var session = mgr.Create("Test World");

            Assert.True(mgr.TryGet(session.SessionId, out var found));
            Assert.Same(session, found);
            Assert.Equal("Test World", session.WorldLabel);
        }

        [Fact]
        public void Create_AssignsUniqueSessionIds() {
            var mgr = new SessionManager();
            var a   = mgr.Create("A");
            var b   = mgr.Create("B");

            Assert.NotEqual(a.SessionId, b.SessionId);
        }

        [Fact]
        public void TryGet_UnknownId_ReturnsFalse() {
            var mgr = new SessionManager();
            Assert.False(mgr.TryGet("does-not-exist", out _));
        }

        [Fact]
        public void Remove_UnregistersAndClosesOutboundChannel() {
            var mgr     = new SessionManager();
            var session = mgr.Create("A");

            mgr.Remove(session.SessionId);

            Assert.False(mgr.TryGet(session.SessionId, out _));
            Assert.False(session.Outbound.Writer.TryWrite(new ServerCommand()));
        }

        [Fact]
        public void ActiveSessions_ReflectsCurrentRegistrations() {
            var mgr = new SessionManager();
            var a   = mgr.Create("A");
            mgr.Create("B");
            mgr.Remove(a.SessionId);

            Assert.Single(mgr.ActiveSessions);
        }

        [Fact]
        public void Apply_TrainEvent_UpdatesTrainsBySessionOnly() {
            var mgr      = new SessionManager();
            var sessionA = mgr.Create("A");
            var sessionB = mgr.Create("B");

            sessionA.Apply(new AgentEvent { Train = new TrainData { TrainId = 5, BlockId = 100 } });

            Assert.True(sessionA.Trains.ContainsKey(5));
            Assert.False(sessionB.Trains.ContainsKey(5)); // isolation: never leaks across sessions
        }

        [Fact]
        public void Apply_SignalsEvent_KeyedByRoute() {
            var mgr     = new SessionManager();
            var session = mgr.Create("A");

            session.Apply(new AgentEvent {
                Signals = new Signals { Route = 3, Indications = { SignalIndication.Stop } },
            });

            Assert.True(session.SignalsByRoute.ContainsKey(3));
            Assert.Equal(SignalIndication.Stop, session.SignalsByRoute[3].Indications[0]);
        }

        [Fact]
        public void Apply_OccupiedBlocksEvent_KeyedByRoute() {
            var mgr     = new SessionManager();
            var session = mgr.Create("A");

            session.Apply(new AgentEvent {
                OccupiedBlocks = new OccupiedBlocks { Route = 1, Blocks = { 100, 101 } },
            });

            Assert.True(session.OccupiedBlocksByRoute.ContainsKey(1));
            Assert.Equal(2, session.OccupiedBlocksByRoute[1].Blocks.Count);
        }

        [Fact]
        public void Enqueue_MakesCommandReadableFromOutbound() {
            var mgr     = new SessionManager();
            var session = mgr.Create("A");

            session.Enqueue(new ServerCommand { Stop = new StopAiTrain { TrainId = 7 } });

            Assert.True(session.Outbound.Reader.TryRead(out var command));
            Assert.Equal(7, command.Stop.TrainId);
        }
    }
}
