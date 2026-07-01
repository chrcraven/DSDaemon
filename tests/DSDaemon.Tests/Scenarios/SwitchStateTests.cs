using System;
using System.Collections.Generic;
using DSDaemon.Messages;
using DSDaemon.Tests.Helpers;
using Xunit;

namespace DSDaemon.Tests.Scenarios {
    /// <summary>
    /// Switch state scenarios: interlock errors, reversed switches, unlocked switches.
    /// Interlock errors (mismatched crossover pair) are always PTC-critical.
    /// </summary>
    public class SwitchStateTests {

        // ── PtcMonitor switch queries ─────────────────────────────────────────

        [Fact]
        public void NoInterlockErrors_ReturnsEmpty() {
            var m = new PtcMonitor();
            Assert.Empty(m.GetInterlockErrors(0));
        }

        [Fact]
        public void InterlockErrors_ReturnsSwitchIds() {
            var m = new PtcMonitor();
            m.UpdateInterlockErrors(MessageBuilder.InterlockErrors(route: 0, 10, 11));

            var errors = m.GetInterlockErrors(0);
            Assert.Equal(2, errors.Count);
            Assert.Contains(10, errors);
            Assert.Contains(11, errors);
        }

        [Fact]
        public void InterlockErrors_AreRouteIsolated() {
            var m = new PtcMonitor();
            m.UpdateInterlockErrors(MessageBuilder.InterlockErrors(route: 0, 10));

            Assert.Single(m.GetInterlockErrors(0));
            Assert.Empty(m.GetInterlockErrors(1));
        }

        [Fact]
        public void InterlockErrors_UpdateClears_WhenEmptyMessageReceived() {
            var m = new PtcMonitor();
            m.UpdateInterlockErrors(MessageBuilder.InterlockErrors(route: 0, 10, 11));
            Assert.Equal(2, m.GetInterlockErrors(0).Count);

            m.UpdateInterlockErrors(MessageBuilder.InterlockErrors(route: 0)); // empty list
            Assert.Empty(m.GetInterlockErrors(0));
        }

        [Fact]
        public void NoReversedSwitches_ReturnsEmpty() {
            var m = new PtcMonitor();
            Assert.Empty(m.GetReversedSwitches(0));
        }

        [Fact]
        public void ReversedSwitches_ReturnsSwitchIds() {
            var m = new PtcMonitor();
            m.UpdateReversedSwitches(MessageBuilder.ReversedSwitches(route: 0, 5, 7, 9));

            var reversed = m.GetReversedSwitches(0);
            Assert.Equal(3, reversed.Count);
        }

        [Fact]
        public void NoUnlockedSwitches_ReturnsEmpty() {
            var m = new PtcMonitor();
            Assert.Empty(m.GetUnlockedSwitches(0));
        }

        [Fact]
        public void UnlockedSwitches_ReturnsSwitchIds() {
            var m = new PtcMonitor();
            m.UpdateUnlockedSwitches(MessageBuilder.UnlockedSwitches(route: 0, 20, 21));

            var unlocked = m.GetUnlockedSwitches(0);
            Assert.Equal(2, unlocked.Count);
        }

        // ── DispatcherCallback display ────────────────────────────────────────

        [Fact]
        public void Callback_InterlockErrors_LogsRed() {
            var logs = new List<(string Text, ConsoleColor Color)>();
            var cb   = new DispatcherCallback((msg, c) => logs.Add((msg, c)));

            cb.SetInterlockErrorSwitches(MessageBuilder.InterlockErrors(route: 0, 10, 11));

            Assert.Single(logs);
            Assert.Equal(ConsoleColor.Red, logs[0].Color);
            Assert.Contains("SW-ERR", logs[0].Text);
        }

        [Fact]
        public void Callback_EmptyInterlockErrors_ProducesNoOutput() {
            // Empty error list means the situation resolved — no need to log.
            var logs = new List<(string Text, ConsoleColor Color)>();
            var cb   = new DispatcherCallback((msg, c) => logs.Add((msg, c)));

            cb.SetInterlockErrorSwitches(MessageBuilder.InterlockErrors(route: 0));

            Assert.Empty(logs);
        }

        [Fact]
        public void Callback_ReversedSwitches_LogsMagenta() {
            var logs = new List<(string Text, ConsoleColor Color)>();
            var cb   = new DispatcherCallback((msg, c) => logs.Add((msg, c)));

            cb.SetReversedSwitches(MessageBuilder.ReversedSwitches(route: 0, 5));

            Assert.Single(logs);
            Assert.Equal(ConsoleColor.Magenta, logs[0].Color);
        }

        [Fact]
        public void Callback_UnlockedSwitches_LogsDarkCyan() {
            var logs = new List<(string Text, ConsoleColor Color)>();
            var cb   = new DispatcherCallback((msg, c) => logs.Add((msg, c)));

            cb.SetUnlockedSwitches(MessageBuilder.UnlockedSwitches(route: 0, 20));

            Assert.Single(logs);
            Assert.Equal(ConsoleColor.DarkCyan, logs[0].Color);
        }

        [Fact]
        public void Callback_EmptyUnlockedSwitches_ProducesNoOutput() {
            var logs = new List<(string Text, ConsoleColor Color)>();
            var cb   = new DispatcherCallback((msg, c) => logs.Add((msg, c)));

            cb.SetUnlockedSwitches(MessageBuilder.UnlockedSwitches(route: 0));

            Assert.Empty(logs);
        }

        // ── Scenario: crossover interlocked in opposing directions ────────────

        [Fact]
        public void CrossoverInterlockError_BothSwitchesReported() {
            // A crossover has two switches; both must be reported when mis-aligned.
            var m = new PtcMonitor();
            m.UpdateInterlockErrors(new InterlockErrorSwitchesMessage {
                Route = 0,
                InterlockErrorSwitches = new List<int> { 42, 43 }, // paired crossover switches
            });

            var errors = m.GetInterlockErrors(0);
            Assert.Equal(2, errors.Count);
        }
    }
}
