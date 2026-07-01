using System;
using System.Collections.Generic;
using DSDaemon.Messages;
using DSDaemon.Tests.Helpers;
using Xunit;

namespace DSDaemon.Tests.Scenarios {
    /// <summary>
    /// Block occupancy: two trains in the same block is a collision risk.
    /// Also covers the display path through DispatcherCallback.
    /// </summary>
    public class BlockOccupancyTests {

        // ── PtcMonitor.FindMultiOccupiedBlocks ────────────────────────────────

        [Fact]
        public void SingleTrainInBlock_NoConflict() {
            var m = new PtcMonitor();
            m.UpdateTrain(TrainBuilder.Build(trainId: 1, blockId: 100));

            Assert.Empty(m.FindMultiOccupiedBlocks());
        }

        [Fact]
        public void TwoTrainsInDifferentBlocks_NoConflict() {
            var m = new PtcMonitor();
            m.UpdateTrain(TrainBuilder.Build(trainId: 1, blockId: 100));
            m.UpdateTrain(TrainBuilder.Build(trainId: 2, blockId: 200));

            Assert.Empty(m.FindMultiOccupiedBlocks());
        }

        [Fact]
        public void TwoTrainsInSameBlock_OneConflict() {
            var m = new PtcMonitor();
            m.UpdateTrain(TrainBuilder.Build(trainId: 1, blockId: 100));
            m.UpdateTrain(TrainBuilder.Build(trainId: 2, blockId: 100));

            var conflicts = m.FindMultiOccupiedBlocks();
            Assert.Single(conflicts);
            Assert.Equal(100, conflicts[0].BlockId);
            Assert.Contains(1, conflicts[0].TrainIds);
            Assert.Contains(2, conflicts[0].TrainIds);
        }

        [Fact]
        public void ThreeTrainsInSameBlock_SingleConflictWithAllIds() {
            var m = new PtcMonitor();
            m.UpdateTrain(TrainBuilder.Build(trainId: 1, blockId: 100));
            m.UpdateTrain(TrainBuilder.Build(trainId: 2, blockId: 100));
            m.UpdateTrain(TrainBuilder.Build(trainId: 3, blockId: 100));

            var conflicts = m.FindMultiOccupiedBlocks();
            Assert.Single(conflicts);
            Assert.Equal(3, conflicts[0].TrainIds.Count);
        }

        [Fact]
        public void TwoConflictsInDifferentBlocks_BothReported() {
            var m = new PtcMonitor();
            m.UpdateTrain(TrainBuilder.Build(trainId: 1, blockId: 100));
            m.UpdateTrain(TrainBuilder.Build(trainId: 2, blockId: 100));
            m.UpdateTrain(TrainBuilder.Build(trainId: 3, blockId: 200));
            m.UpdateTrain(TrainBuilder.Build(trainId: 4, blockId: 200));

            Assert.Equal(2, m.FindMultiOccupiedBlocks().Count);
        }

        [Fact]
        public void TrainInBlockZero_ExcludedFromConflictCheck() {
            // BlockID 0 = "not yet placed" in Run8; should not cause a false conflict.
            var m = new PtcMonitor();
            m.UpdateTrain(TrainBuilder.Build(trainId: 1, blockId: 0));
            m.UpdateTrain(TrainBuilder.Build(trainId: 2, blockId: 0));

            Assert.Empty(m.FindMultiOccupiedBlocks());
        }

        [Fact]
        public void UpdateTrain_LatestDataWins_MovedTrainClearsOldConflict() {
            var m = new PtcMonitor();
            m.UpdateTrain(TrainBuilder.Build(trainId: 1, blockId: 100));
            m.UpdateTrain(TrainBuilder.Build(trainId: 2, blockId: 100));
            Assert.Single(m.FindMultiOccupiedBlocks());

            // Train 2 advances to block 200.
            m.UpdateTrain(TrainBuilder.Build(trainId: 2, blockId: 200));
            Assert.Empty(m.FindMultiOccupiedBlocks());
        }

        // ── DispatcherCallback display ────────────────────────────────────────

        [Fact]
        public void Callback_OccupiedBlocks_LogsYellow() {
            var logs = new List<(string Text, ConsoleColor Color)>();
            var cb   = new DispatcherCallback((m, c) => logs.Add((m, c)));

            cb.SetOccupiedBlocks(MessageBuilder.OccupiedBlocks(route: 0, blocks: new[] { 101, 102 }));

            Assert.Single(logs);
            Assert.Equal(ConsoleColor.Yellow, logs[0].Color);
            Assert.Contains("occupied=2", logs[0].Text);
        }

        [Fact]
        public void Callback_EmptyOccupiedBlocks_LogsDarkGray() {
            var logs = new List<(string Text, ConsoleColor Color)>();
            var cb   = new DispatcherCallback((m, c) => logs.Add((m, c)));

            cb.SetOccupiedBlocks(MessageBuilder.OccupiedBlocks(route: 0, blocks: Array.Empty<int>()));

            Assert.Equal(ConsoleColor.DarkGray, logs[0].Color);
        }

        [Fact]
        public void Callback_SmallBlockList_IncludesBlockIds() {
            var logs = new List<(string Text, ConsoleColor Color)>();
            var cb   = new DispatcherCallback((m, c) => logs.Add((m, c)));

            cb.SetOccupiedBlocks(MessageBuilder.OccupiedBlocks(route: 1, blocks: new[] { 501, 502 }));

            Assert.Contains("501", logs[0].Text);
            Assert.Contains("502", logs[0].Text);
        }
    }
}
