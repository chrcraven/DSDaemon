using System;
using System.Collections.Generic;
using DSDaemon.Messages;
using DSDaemon.Tests.Helpers;
using Xunit;

namespace DSDaemon.Tests.Scenarios {
    /// <summary>
    /// Authority conflict: a route has occupied blocks AND at least one signal
    /// at Proceed or Fleet.  All-Stop with occupancy is expected (train is held).
    /// </summary>
    public class SignalConflictTests {

        // ── PtcMonitor.EvaluateRoute ──────────────────────────────────────────

        [Fact]
        public void EmptyRoute_NoConflict() {
            var m = new PtcMonitor();
            Assert.False(m.EvaluateRoute(0).IsConflict);
        }

        [Fact]
        public void OccupiedBlocksOnly_NoConflict() {
            // Occupied blocks + all signals Stop = expected scenario (train held at red).
            var m = new PtcMonitor();
            m.UpdateOccupiedBlocks(MessageBuilder.OccupiedBlocks(0, new[] { 100 }));
            m.UpdateSignals(MessageBuilder.AllStop(0, 10));

            Assert.False(m.EvaluateRoute(0).IsConflict);
        }

        [Fact]
        public void ProceedSignalsOnly_NoConflict() {
            // Cleared signals with no trains — normal operations.
            var m = new PtcMonitor();
            m.UpdateOccupiedBlocks(MessageBuilder.OccupiedBlocks(0, Array.Empty<int>()));
            m.UpdateSignals(MessageBuilder.Signals(0, new[] {
                ESignalIndication.Proceed, ESignalIndication.Proceed }));

            Assert.False(m.EvaluateRoute(0).IsConflict);
        }

        [Fact]
        public void OccupiedPlusProceedSignal_IsConflict() {
            var m = new PtcMonitor();
            m.UpdateOccupiedBlocks(MessageBuilder.OccupiedBlocks(0, new[] { 100 }));
            m.UpdateSignals(MessageBuilder.Signals(0, new[] {
                ESignalIndication.Stop,
                ESignalIndication.Proceed }));

            var status = m.EvaluateRoute(0);
            Assert.True(status.HasOccupiedBlocks);
            Assert.True(status.HasProceedSignals);
            Assert.True(status.IsConflict);
        }

        [Fact]
        public void OccupiedPlusFleetSignal_IsConflict() {
            // Fleet (automatic working) also constitutes a cleared signal.
            var m = new PtcMonitor();
            m.UpdateOccupiedBlocks(MessageBuilder.OccupiedBlocks(0, new[] { 200 }));
            m.UpdateSignals(MessageBuilder.Signals(0, new[] { ESignalIndication.Fleet }));

            Assert.True(m.EvaluateRoute(0).IsConflict);
        }

        [Fact]
        public void OccupiedPlusFlagBySignal_NoConflict() {
            // FlagBy = restricting indication, not a clear — not an authority conflict.
            var m = new PtcMonitor();
            m.UpdateOccupiedBlocks(MessageBuilder.OccupiedBlocks(0, new[] { 100 }));
            m.UpdateSignals(MessageBuilder.Signals(0, new[] { ESignalIndication.FlagBy }));

            Assert.False(m.EvaluateRoute(0).IsConflict);
        }

        [Fact]
        public void MultipleSignals_OnlyOneProceedTriggers() {
            var m = new PtcMonitor();
            m.UpdateOccupiedBlocks(MessageBuilder.OccupiedBlocks(0, new[] { 100 }));
            m.UpdateSignals(MessageBuilder.Signals(0, new[] {
                ESignalIndication.Stop,
                ESignalIndication.Stop,
                ESignalIndication.Stop,
                ESignalIndication.Proceed }));  // just one cleared signal is enough

            Assert.True(m.EvaluateRoute(0).IsConflict);
        }

        [Fact]
        public void ConflictsAreRouteIsolated() {
            // Route 0 has conflict; route 1 does not.
            var m = new PtcMonitor();
            m.UpdateOccupiedBlocks(MessageBuilder.OccupiedBlocks(0, new[] { 100 }));
            m.UpdateSignals(MessageBuilder.Signals(0, new[] { ESignalIndication.Proceed }));

            m.UpdateOccupiedBlocks(MessageBuilder.OccupiedBlocks(1, Array.Empty<int>()));
            m.UpdateSignals(MessageBuilder.AllStop(1, 5));

            Assert.True(m.EvaluateRoute(0).IsConflict);
            Assert.False(m.EvaluateRoute(1).IsConflict);
        }

        [Fact]
        public void ConflictResolves_AfterBlocksCleared() {
            var m = new PtcMonitor();
            m.UpdateOccupiedBlocks(MessageBuilder.OccupiedBlocks(0, new[] { 100 }));
            m.UpdateSignals(MessageBuilder.Signals(0, new[] { ESignalIndication.Proceed }));
            Assert.True(m.EvaluateRoute(0).IsConflict);

            // Train clears the block.
            m.UpdateOccupiedBlocks(MessageBuilder.OccupiedBlocks(0, Array.Empty<int>()));
            Assert.False(m.EvaluateRoute(0).IsConflict);
        }

        // ── DispatcherCallback display ─────────────────────────────────────────

        [Fact]
        public void Callback_ConflictingSignalUpdate_LogsRedWithTag() {
            var logs = new List<(string Text, ConsoleColor Color)>();
            var monitor = new PtcMonitor();
            var cb      = new DispatcherCallback((msg, c) => logs.Add((msg, c)), monitor);

            // Prime the monitor with an occupied block on route 0.
            monitor.UpdateOccupiedBlocks(MessageBuilder.OccupiedBlocks(0, new[] { 100 }));

            // Now receive a signal update that includes a Proceed — triggers conflict.
            cb.SetSignals(MessageBuilder.Signals(0, new[] {
                ESignalIndication.Stop, ESignalIndication.Proceed }));

            Assert.Equal(ConsoleColor.Red, logs[0].Color);
            Assert.Contains("AUTHORITY CONFLICT", logs[0].Text);
        }

        [Fact]
        public void Callback_AllStopWithOccupancy_NoConflictTag() {
            var logs = new List<(string Text, ConsoleColor Color)>();
            var monitor = new PtcMonitor();
            var cb      = new DispatcherCallback((msg, c) => logs.Add((msg, c)), monitor);

            monitor.UpdateOccupiedBlocks(MessageBuilder.OccupiedBlocks(0, new[] { 100 }));
            cb.SetSignals(MessageBuilder.AllStop(0, 5));

            Assert.DoesNotContain("AUTHORITY CONFLICT", logs[0].Text);
        }
    }
}
