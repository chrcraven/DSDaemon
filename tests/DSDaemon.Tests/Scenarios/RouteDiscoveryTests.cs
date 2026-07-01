using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DSDaemon;
using DSDaemon.Discovery;
using DSDaemon.Messages;
using DSDaemon.Tests.Helpers;
using Xunit;

namespace DSDaemon.Tests.Scenarios {
    /// <summary>
    /// Route discovery: state machine transitions, adjacency recording,
    /// and timeout behaviour.
    /// </summary>
    public class RouteDiscoveryTests {

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>Records every command issued by the engine, including the
        /// autonomous signal/switch clearing used to aid a released scout.</summary>
        private sealed class CapturingCommander : IDispatcherCommander {
            public List<(int TrainId, bool IsHold)> Commands = new();
            public List<(int SignalId, ESignalIndication Indication, bool Auto)> SignalsChanged = new();
            public List<(int SwitchId, ESwitchState State)> SwitchesThrown = new();

            public void Hold(int id)    => Commands.Add((id, true));
            public void Release(int id) => Commands.Add((id, false));

            public void ChangeSignal(int signalId, ESignalIndication indication, bool automaticWorking = false) =>
                SignalsChanged.Add((signalId, indication, automaticWorking));

            public void ThrowSwitch(int switchId, ESwitchState state) =>
                SwitchesThrown.Add((switchId, state));

            public void Stop(int trainId) { }
            public void Recrew(int trainId) { }
            public void Relinquish(int trainId, bool relinquish = true) { }
        }

        private static (RouteDiscoveryEngine engine, RouteMap map, CapturingCommander cmdr) Make(
            TimeSpan timeout = default) {
            var map  = new RouteMap();
            var cmdr = new CapturingCommander();
            var eng  = new RouteDiscoveryEngine(map, cmdr, timeout);
            return (eng, map, cmdr);
        }

        private static TrainData AI(int id, int block, bool held = false) =>
            TrainBuilder.Build(trainId: id, blockId: block, engineer: EEngineerType.AI, held: held);

        private static TrainData Player(int id, int block) =>
            TrainBuilder.Build(trainId: id, blockId: block, engineer: EEngineerType.Player);

        // ── Initializing state ────────────────────────────────────────────────

        [Fact]
        public void InitialState_IsInitializing() {
            var (eng, _, _) = Make();
            Assert.Equal(DiscoveryState.Initializing, eng.State);
        }

        [Fact]
        public void AiTrain_IsHeld_InInitializing() {
            var (eng, _, cmdr) = Make();
            eng.OnTrainDataReceived(AI(1, 100));

            Assert.Single(cmdr.Commands);
            Assert.Equal((1, true), cmdr.Commands[0]);
        }

        [Fact]
        public void PlayerTrain_IsNotHeld() {
            var (eng, _, cmdr) = Make();
            eng.OnTrainDataReceived(Player(1, 100));

            Assert.Empty(cmdr.Commands);
        }

        [Fact]
        public void SameAiTrain_HoldSentOnlyOnce() {
            var (eng, _, cmdr) = Make();
            eng.OnTrainDataReceived(AI(1, 100));
            eng.OnTrainDataReceived(AI(1, 100)); // duplicate

            Assert.Single(cmdr.Commands); // only one Hold
        }

        // ── Start() transition ────────────────────────────────────────────────

        [Fact]
        public void Start_DoesNotWaitForRun8Confirmation_ReleasesScout() {
            var (eng, _, _) = Make();
            // Run8 has not echoed HoldingForDispatcher=true for this train — on a
            // real session it never does for dispatcher-issued AI holds, so the
            // engine must not wait for it and should release anyway.
            eng.OnTrainDataReceived(AI(1, 100, held: false));
            eng.Start();

            Assert.Equal(DiscoveryState.Released, eng.State);
            Assert.Equal(1, eng.ScoutTrainId);
        }

        [Fact]
        public void Start_WithHeldTrain_ScoutIsReleased() {
            var (eng, _, cmdr) = Make();
            eng.OnTrainDataReceived(AI(1, 100, held: true)); // confirmed held
            eng.Start();

            Assert.Equal(DiscoveryState.Released, eng.State);
            Assert.Equal(1, eng.ScoutTrainId);
            // Commands: Hold (from OnTrainDataReceived) then Release (from TrySelectScout).
            Assert.Equal(2, cmdr.Commands.Count);
            Assert.Equal((1, false), cmdr.Commands[1]);
        }

        [Fact]
        public void TrainConfirmedHeld_AfterStart_TriggersScout() {
            var (eng, _, cmdr) = Make();
            eng.Start(); // no trains yet → WaitingToSelect
            eng.OnTrainDataReceived(AI(1, 100, held: true));

            Assert.Equal(DiscoveryState.Released, eng.State);
            Assert.Equal(1, eng.ScoutTrainId);
        }

        // ── Block transition recording ────────────────────────────────────────

        [Fact]
        public void ScoutBlockChange_RecordsAdjacency() {
            var (eng, map, _) = Make();
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.Start(); // releases scout from block 100

            // Scout moves to block 200.
            eng.OnTrainDataReceived(AI(1, 200, held: false));

            Assert.Equal(1, map.GetAdjacencyConfidence(0, 100, 200));
            Assert.Equal(1, map.GetAdjacencyConfidence(0, 200, 100)); // reverse edge too
        }

        [Fact]
        public void ScoutBlockChange_ScoutReHeld() {
            var (eng, _, cmdr) = Make();
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.Start();

            eng.OnTrainDataReceived(AI(1, 200, held: false));

            // Commands: Hold(init), Release(scout), Hold(re-hold after move),
            // Release(re-scouted immediately — it's the only train around, so it
            // keeps exploring rather than sitting held forever).
            Assert.Equal(4, cmdr.Commands.Count);
            Assert.Equal((1, true), cmdr.Commands[2]);
            Assert.Equal((1, false), cmdr.Commands[3]);
        }

        [Fact]
        public void ScoutBlockChange_LoneTrain_ImmediatelyRescouted() {
            var (eng, _, _) = Make();
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.Start();
            eng.OnTrainDataReceived(AI(1, 200, held: false));

            // With no other train to give a turn to, the lone train keeps
            // exploring rather than being left held indefinitely.
            Assert.Equal(DiscoveryState.Released, eng.State);
            Assert.Equal(1, eng.ScoutTrainId);
        }

        [Fact]
        public void TwoTrains_SecondBecomesScout_AfterFirstMoves() {
            var (eng, _, cmdr) = Make();
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.OnTrainDataReceived(AI(2, 200, held: true));
            eng.Start(); // picks train #1 as scout (first found)

            // Train #1 moves; the engine re-holds #1 and, since #2 is still
            // eligible, picks #2 as the next scout in the same synchronous call
            // (round-robin skips the just-finished scout when another train is
            // available — see TrySelectScout).
            eng.OnTrainDataReceived(AI(1, 150, held: false));

            Assert.Equal(DiscoveryState.Released, eng.State);
            Assert.Equal(2, eng.ScoutTrainId);
        }

        [Fact]
        public void SameBlock_NoTransition_EngineStaysReleased() {
            var (eng, map, _) = Make();
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.Start();

            // Scout is reported back at the same block — not a transition.
            eng.OnTrainDataReceived(AI(1, 100, held: false));

            Assert.Equal(DiscoveryState.Released, eng.State);
            Assert.Equal(0, map.GetAdjacencyConfidence(0, 100, 100));
        }

        // ── Route tracking ────────────────────────────────────────────────────

        [Fact]
        public void AdjacencyOnRoute1_RecordedForRoute1() {
            var (eng, map, _) = Make();
            eng.OnOccupiedBlocksUpdated(new OccupiedBlocksMessage {
                Route = 1,
                OccupiedBlocks = new List<int> { 500 },
            });
            eng.OnTrainDataReceived(AI(1, 500, held: true));
            eng.Start();

            eng.OnTrainDataReceived(AI(1, 501, held: false));

            Assert.Equal(1, map.GetAdjacencyConfidence(1, 500, 501));
            Assert.Equal(0, map.GetAdjacencyConfidence(0, 500, 501)); // not on route 0
        }

        [Fact]
        public void CompositeBlockId_RouteDecodedFromBlockId_WhenNoOccupiedBlocksSeen() {
            // Live Run8 data has been observed encoding TrainData.BlockID as
            // route*1000 + localBlock (e.g. 110231 for route 110, block 231),
            // while SetOccupiedBlocks reports the raw local block number (231).
            // Without a matching _blockRoute entry, the engine should decode the
            // route straight from the composite BlockID so auto-clearing (which
            // matches on msg.Route) still fires for the scout's real route.
            var (eng, _, cmdr) = Make();
            eng.OnTrainDataReceived(AI(1, 110231, held: true));
            eng.Start(); // releases scout on route 110 (decoded from 110231 / 1000)

            eng.OnSignalsUpdated(new SignalsMessage {
                Route = 110,
                Signals = new List<ESignalIndication> { ESignalIndication.Stop },
            });

            var change = Assert.Single(cmdr.SignalsChanged);
            Assert.Equal((0, ESignalIndication.Proceed, true), change);
        }

        // ── Autonomous signal/switch clearing (aiding the released scout) ──────

        [Fact]
        public void ReleasedScout_StopSignalOnItsRoute_IsClearedToProceed() {
            var (eng, _, cmdr) = Make();
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.Start(); // releases scout on route 0

            eng.OnSignalsUpdated(new SignalsMessage {
                Route = 0,
                Signals = new List<ESignalIndication> { ESignalIndication.Proceed, ESignalIndication.Stop },
            });

            var change = Assert.Single(cmdr.SignalsChanged);
            Assert.Equal((1, ESignalIndication.Proceed, true), change);
        }

        [Fact]
        public void ReleasedScout_StopSignalOnOtherRoute_IsIgnored() {
            var (eng, _, cmdr) = Make();
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.Start(); // scout is on route 0

            eng.OnSignalsUpdated(new SignalsMessage {
                Route = 1,
                Signals = new List<ESignalIndication> { ESignalIndication.Stop },
            });

            Assert.Empty(cmdr.SignalsChanged);
        }

        [Fact]
        public void ReleasedScout_SameStopSignal_OnlyClearedOnce() {
            var (eng, _, cmdr) = Make();
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.Start();

            var msg = new SignalsMessage { Route = 0, Signals = new List<ESignalIndication> { ESignalIndication.Stop } };
            eng.OnSignalsUpdated(msg);
            eng.OnSignalsUpdated(msg); // repeat callback, e.g. from a periodic push

            Assert.Single(cmdr.SignalsChanged);
        }

        [Fact]
        public void NotReleased_SignalsUpdated_NoCommandIssued() {
            var (eng, _, cmdr) = Make();
            // Still Initializing — no scout released yet.
            eng.OnSignalsUpdated(new SignalsMessage {
                Route = 0,
                Signals = new List<ESignalIndication> { ESignalIndication.Stop },
            });

            Assert.Empty(cmdr.SignalsChanged);
        }

        [Fact]
        public void ReleasedScout_InterlockErrorOnItsRoute_IsUnlocked() {
            var (eng, _, cmdr) = Make();
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.Start();

            eng.OnInterlockErrorsUpdated(new InterlockErrorSwitchesMessage {
                Route = 0,
                InterlockErrorSwitches = new List<int> { 42 },
            });

            var thrown = Assert.Single(cmdr.SwitchesThrown);
            Assert.Equal((42, ESwitchState.Unlock), thrown);
        }

        [Fact]
        public void ReleasedScout_SameInterlockError_OnlyUnlockedOnce() {
            var (eng, _, cmdr) = Make();
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.Start();

            var msg = new InterlockErrorSwitchesMessage { Route = 0, InterlockErrorSwitches = new List<int> { 42 } };
            eng.OnInterlockErrorsUpdated(msg);
            eng.OnInterlockErrorsUpdated(msg);

            Assert.Single(cmdr.SwitchesThrown);
        }

        [Fact]
        public void ScoutReleased_RouteAlreadyReportedStop_ClearedImmediatelyOnRelease() {
            // Run8 only pushes SetSignals on change. If the route already went
            // Stop before the scout was released (the common real-world case —
            // that's exactly what's blocking the train), no fresh SetSignals
            // callback will ever arrive to trigger the reactive auto-clear.
            // Scout selection must seed the clear from the cached state instead.
            var (eng, _, cmdr) = Make();
            eng.OnSignalsUpdated(new SignalsMessage {
                Route = 0,
                Signals = new List<ESignalIndication> { ESignalIndication.Stop },
            });
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.Start(); // releases scout on route 0

            var change = Assert.Single(cmdr.SignalsChanged);
            Assert.Equal((0, ESignalIndication.Proceed, true), change);
        }

        [Fact]
        public void ScoutReleased_RouteAlreadyReportedInterlockError_UnlockedImmediatelyOnRelease() {
            var (eng, _, cmdr) = Make();
            eng.OnInterlockErrorsUpdated(new InterlockErrorSwitchesMessage {
                Route = 0,
                InterlockErrorSwitches = new List<int> { 42 },
            });
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.Start();

            var thrown = Assert.Single(cmdr.SwitchesThrown);
            Assert.Equal((42, ESwitchState.Unlock), thrown);
        }

        [Fact]
        public void ScoutReleased_CachedStopOnOtherRoute_IsIgnored() {
            var (eng, _, cmdr) = Make();
            eng.OnSignalsUpdated(new SignalsMessage {
                Route = 1,
                Signals = new List<ESignalIndication> { ESignalIndication.Stop },
            });
            eng.OnTrainDataReceived(AI(1, 100, held: true)); // scout ends up on route 0
            eng.Start();

            Assert.Empty(cmdr.SignalsChanged);
        }

        [Fact]
        public void NewScout_ClearsAutoClearTrackingFromPreviousScout() {
            var (eng, _, cmdr) = Make();
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.OnTrainDataReceived(AI(2, 200, held: true));
            eng.Start(); // scout #1 released

            eng.OnSignalsUpdated(new SignalsMessage { Route = 0, Signals = new List<ESignalIndication> { ESignalIndication.Stop } });
            Assert.Single(cmdr.SignalsChanged);

            // Scout #1 moves and is re-held; #2 becomes the new scout.
            eng.OnTrainDataReceived(AI(1, 150, held: false));
            eng.OnTrainDataReceived(AI(2, 200, held: true));
            Assert.Equal(2, eng.ScoutTrainId);

            // Same signal index reported Stop again for the new scout's run —
            // should be cleared again since tracking was reset.
            eng.OnSignalsUpdated(new SignalsMessage { Route = 0, Signals = new List<ESignalIndication> { ESignalIndication.Stop } });
            Assert.Equal(2, cmdr.SignalsChanged.Count);
        }

        // ── Incremental persistence ─────────────────────────────────────────────

        [Fact]
        public void RecordedAdjacency_InvokesOnAdjacencyRecordedCallback() {
            var map  = new RouteMap();
            var cmdr = new CapturingCommander();
            int saveCount = 0;
            var eng = new RouteDiscoveryEngine(map, cmdr, onAdjacencyRecorded: () => saveCount++);

            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.Start(); // releases scout from block 100
            Assert.Equal(0, saveCount); // no edge recorded yet

            eng.OnTrainDataReceived(AI(1, 200, held: false)); // records blk 100 -> 200
            Assert.Equal(1, saveCount);
        }

        [Fact]
        public void SameBlock_NoTransition_DoesNotInvokeOnAdjacencyRecorded() {
            var map  = new RouteMap();
            var cmdr = new CapturingCommander();
            int saveCount = 0;
            var eng = new RouteDiscoveryEngine(map, cmdr, onAdjacencyRecorded: () => saveCount++);

            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.Start();
            eng.OnTrainDataReceived(AI(1, 100, held: false)); // same block, not a transition

            Assert.Equal(0, saveCount);
        }

        // ── Timeout ───────────────────────────────────────────────────────────

        [Fact]
        public async Task ScoutTimeout_LoneTrain_ImmediatelyRescouted() {
            var (eng, _, cmdr) = Make(timeout: TimeSpan.FromMilliseconds(30));
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.Start(); // releases scout

            await Task.Delay(100); // let timeout fire

            // With no other train around, a timed-out scout is immediately
            // re-released rather than left held forever.
            Assert.Equal(DiscoveryState.Released, eng.State);
            Assert.Equal(1, eng.ScoutTrainId);
            // Commands: Hold, Release, Hold (timeout re-hold), Release (re-scouted).
            Assert.Equal(4, cmdr.Commands.Count);
            Assert.Equal((1, true), cmdr.Commands[2]);
            Assert.Equal((1, false), cmdr.Commands[3]);
        }

        [Fact]
        public async Task ScoutTimeout_OtherTrainAvailable_BecomesNextScout() {
            var (eng, _, cmdr) = Make(timeout: TimeSpan.FromMilliseconds(30));
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.OnTrainDataReceived(AI(2, 200, held: true));
            eng.Start(); // releases scout #1

            await Task.Delay(100); // let #1's timeout fire

            Assert.Equal(DiscoveryState.Released, eng.State);
            Assert.Equal(2, eng.ScoutTrainId);
        }

        [Fact]
        public async Task ScoutTimeout_NoMapEntry_Recorded() {
            var (eng, map, _) = Make(timeout: TimeSpan.FromMilliseconds(30));
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.Start();

            await Task.Delay(100);

            // Timeout with no block change — nothing to record.
            Assert.Empty(map.Routes);
        }

        // ── RouteMap persistence ──────────────────────────────────────────────

        [Fact]
        public void RouteMap_RecordAdjacency_BothDirections() {
            var map = new RouteMap();
            map.RecordAdjacency(0, 10, 20);

            Assert.Equal(1, map.GetAdjacencyConfidence(0, 10, 20));
            Assert.Equal(1, map.GetAdjacencyConfidence(0, 20, 10));
        }

        [Fact]
        public void RouteMap_ConfidenceAccumulates() {
            var map = new RouteMap();
            map.RecordAdjacency(0, 10, 20);
            map.RecordAdjacency(0, 10, 20);
            map.RecordAdjacency(0, 10, 20);

            Assert.Equal(3, map.GetAdjacencyConfidence(0, 10, 20));
        }

        [Fact]
        public void RouteMap_Block0_IgnoredOnBothSides() {
            var map = new RouteMap();
            map.RecordAdjacency(0, 0, 100);
            map.RecordAdjacency(0, 100, 0);

            Assert.Empty(map.Routes);
        }

        [Fact]
        public void RouteMap_SameBlock_Ignored() {
            var map = new RouteMap();
            map.RecordAdjacency(0, 55, 55);

            Assert.Empty(map.Routes);
        }

        [Fact]
        public void RouteMap_AdjacentBlocks_ReturnsEdges() {
            var map = new RouteMap();
            map.RecordAdjacency(0, 10, 20);
            map.RecordAdjacency(0, 10, 30);

            var adj = map.GetAdjacentBlocks(0, 10);
            Assert.Equal(2, adj.Count);
            Assert.True(adj.ContainsKey(20));
            Assert.True(adj.ContainsKey(30));
        }

        [Fact]
        public void RouteMap_UnknownRoute_ReturnsEmptyAdjacency() {
            var map = new RouteMap();
            var adj = map.GetAdjacentBlocks(99, 1);
            Assert.Empty(adj);
        }
    }
}
