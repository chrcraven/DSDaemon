using System;
using System.Collections.Generic;
using DSDaemon.Messages;
using DSDaemon.Tests.Helpers;
using Xunit;

namespace DSDaemon.Tests.Scenarios {
    /// <summary>
    /// PTC speed enforcement: a train must not exceed TrainSpeedLimitMPH.
    /// Reverse movement uses the absolute value of TrainSpeedMph.
    /// A limit of 0 means unknown — enforcement is suppressed.
    /// </summary>
    public class OverspeedTests {

        // ── PtcMonitor.EvaluateTrain (pure logic) ────────────────────────────

        [Fact]
        public void TrainExactlyAtLimit_NotOverspeed() {
            var t = TrainBuilder.Build(speedMph: 45f, speedLimitMph: 45);
            Assert.False(PtcMonitor.EvaluateTrain(t).IsOverspeed);
        }

        [Fact]
        public void TrainOneAboveLimit_IsOverspeed() {
            var t = TrainBuilder.Build(speedMph: 45.1f, speedLimitMph: 45);
            Assert.True(PtcMonitor.EvaluateTrain(t).IsOverspeed);
        }

        [Fact]
        public void TrainWellBelowLimit_NotOverspeed() {
            var t = TrainBuilder.Build(speedMph: 20f, speedLimitMph: 45);
            Assert.False(PtcMonitor.EvaluateTrain(t).IsOverspeed);
        }

        [Fact]
        public void TrainGoingBackwardsBelowLimit_NotOverspeed() {
            // Negative speed = reversing. Abs(-20) = 20 < 45 → fine.
            var t = TrainBuilder.Build(speedMph: -20f, speedLimitMph: 45);
            Assert.False(PtcMonitor.EvaluateTrain(t).IsOverspeed);
        }

        [Fact]
        public void TrainGoingBackwardsOverLimit_IsOverspeed() {
            // Abs(-55) = 55 > 45 → overspeed even in reverse.
            var t = TrainBuilder.Build(speedMph: -55f, speedLimitMph: 45);
            Assert.True(PtcMonitor.EvaluateTrain(t).IsOverspeed);
        }

        [Fact]
        public void TrainWithZeroLimit_NeverOverspeed() {
            // Limit 0 = not yet reported by Run8; suppress enforcement.
            var t = TrainBuilder.Build(speedMph: 999f, speedLimitMph: 0);
            Assert.False(PtcMonitor.EvaluateTrain(t).IsOverspeed);
        }

        [Fact]
        public void StationaryTrain_NeverOverspeed() {
            var t = TrainBuilder.Build(speedMph: 0f, speedLimitMph: 45);
            Assert.False(PtcMonitor.EvaluateTrain(t).IsOverspeed);
        }

        // ── DispatcherCallback display ────────────────────────────────────────

        [Fact]
        public void Callback_OverspeedTrain_WritesRedWithPtcTag() {
            var logs = Capture(out var cb);
            cb.UpdateTrainData(new TrainDataMessage {
                Train = TrainBuilder.Build(speedMph: 55f, speedLimitMph: 45),
            });

            Assert.Single(logs);
            Assert.Equal(ConsoleColor.Red, logs[0].Color);
            Assert.Contains("PTC OVERSPEED", logs[0].Text);
        }

        [Fact]
        public void Callback_TrainAtLimit_NoOverspeeedTag() {
            var logs = Capture(out var cb);
            cb.UpdateTrainData(new TrainDataMessage {
                Train = TrainBuilder.Build(speedMph: 45f, speedLimitMph: 45),
            });

            Assert.DoesNotContain("OVERSPEED", logs[0].Text);
        }

        [Fact]
        public void Callback_OverspeedBeatsHeld_UsesRedNotMagenta() {
            // A held overspeed train: overspeed takes colour priority.
            var logs = Capture(out var cb);
            cb.UpdateTrainData(new TrainDataMessage {
                Train = TrainBuilder.Build(speedMph: 60f, speedLimitMph: 45, held: true),
            });

            Assert.Equal(ConsoleColor.Red, logs[0].Color);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static List<(string Text, ConsoleColor Color)> Capture(out DispatcherCallback cb) {
            var logs = new List<(string, ConsoleColor)>();
            cb = new DispatcherCallback((msg, color) => logs.Add((msg, color)));
            return logs;
        }
    }
}
