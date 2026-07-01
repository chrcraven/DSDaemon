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

        /// <summary>Minimal mock that records every Hold/Release call.</summary>
        private sealed class CapturingCommander : ITrainCommander {
            public List<(int TrainId, bool IsHold)> Commands = new();
            public void Hold(int id)    => Commands.Add((id, true));
            public void Release(int id) => Commands.Add((id, false));
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
        public void Start_WithNoConfirmedTrains_StaysWaiting() {
            var (eng, _, _) = Make();
            // See an AI train but don't confirm it held yet.
            eng.OnTrainDataReceived(AI(1, 100, held: false));
            eng.Start();

            Assert.Equal(DiscoveryState.WaitingToSelect, eng.State);
            Assert.Null(eng.ScoutTrainId);
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

            // Commands: Hold(init), Release(scout), Hold(re-hold after move).
            Assert.Equal(3, cmdr.Commands.Count);
            Assert.Equal((1, true), cmdr.Commands[2]);
        }

        [Fact]
        public void ScoutBlockChange_ReturnsToWaiting_WithNoNextScout() {
            var (eng, _, _) = Make();
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.Start();
            eng.OnTrainDataReceived(AI(1, 200, held: false));

            Assert.Equal(DiscoveryState.WaitingToSelect, eng.State);
            Assert.Null(eng.ScoutTrainId);
        }

        [Fact]
        public void TwoTrains_SecondBecomesScout_AfterFirstMoves() {
            var (eng, _, cmdr) = Make();
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.OnTrainDataReceived(AI(2, 200, held: true));
            eng.Start(); // picks train #1 as scout (first found)

            // Train #1 moves; train #2 is still held → becomes next scout.
            eng.OnTrainDataReceived(AI(1, 150, held: false));
            eng.OnTrainDataReceived(AI(2, 200, held: true)); // re-confirm #2 still held

            // After block transition #1→150, engine re-holds #1 and immediately
            // tries to select #2 as the next scout — but #2's data was set before
            // the transition, so a subsequent confirmed-held message is needed.
            // Simulate that now:
            eng.OnTrainDataReceived(AI(2, 200, held: true));

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

        // ── Timeout ───────────────────────────────────────────────────────────

        [Fact]
        public async Task ScoutTimeout_ScoutReHeld() {
            var (eng, _, cmdr) = Make(timeout: TimeSpan.FromMilliseconds(30));
            eng.OnTrainDataReceived(AI(1, 100, held: true));
            eng.Start(); // releases scout

            await Task.Delay(100); // let timeout fire

            Assert.Equal(DiscoveryState.WaitingToSelect, eng.State);
            // Commands: Hold, Release, Hold (timeout re-hold).
            Assert.Equal(3, cmdr.Commands.Count);
            Assert.Equal((1, true), cmdr.Commands[2]);
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
