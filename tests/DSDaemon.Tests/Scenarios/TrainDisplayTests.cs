using System;
using System.Collections.Generic;
using DSDaemon.Messages;
using DSDaemon.Tests.Helpers;
using Xunit;

namespace DSDaemon.Tests.Scenarios {
    /// <summary>
    /// Train display: colour coding by engineer type and PTC status, and the
    /// content of the formatted log line.
    /// </summary>
    public class TrainDisplayTests {

        [Fact]
        public void PlayerTrain_LogsGreen() {
            var logs = Capture(out var cb);
            cb.UpdateTrainData(new TrainDataMessage {
                Train = TrainBuilder.Build(engineer: EEngineerType.Player, engineerName: "ChrisR"),
            });

            Assert.Equal(ConsoleColor.Green, logs[0].Color);
        }

        [Fact]
        public void AiTrain_LogsDarkGreen() {
            var logs = Capture(out var cb);
            cb.UpdateTrainData(new TrainDataMessage {
                Train = TrainBuilder.Build(engineer: EEngineerType.AI),
            });

            Assert.Equal(ConsoleColor.DarkGreen, logs[0].Color);
        }

        [Fact]
        public void UnmannedTrain_LogsDarkGray() {
            var logs = Capture(out var cb);
            cb.UpdateTrainData(new TrainDataMessage {
                Train = TrainBuilder.Build(engineer: EEngineerType.None),
            });

            Assert.Equal(ConsoleColor.DarkGray, logs[0].Color);
        }

        [Fact]
        public void HeldTrain_LogsMagentaWithHeldTag() {
            var logs = Capture(out var cb);
            cb.UpdateTrainData(new TrainDataMessage {
                Train = TrainBuilder.Build(held: true),
            });

            Assert.Equal(ConsoleColor.Magenta, logs[0].Color);
            Assert.Contains("[HELD]", logs[0].Text);
        }

        [Fact]
        public void NormalTrain_HasNoPtcTag() {
            var logs = Capture(out var cb);
            cb.UpdateTrainData(new TrainDataMessage {
                Train = TrainBuilder.Build(speedMph: 30f, speedLimitMph: 45, held: false),
            });

            Assert.DoesNotContain("OVERSPEED", logs[0].Text);
            Assert.DoesNotContain("[HELD]", logs[0].Text);
        }

        [Fact]
        public void LogLine_ContainsTrainSymbolAndBlockId() {
            var logs = Capture(out var cb);
            cb.UpdateTrainData(new TrainDataMessage {
                Train = TrainBuilder.Build(trainId: 7, symbol: "M-BNSF42", blockId: 999),
            });

            Assert.Contains("#7",       logs[0].Text);
            Assert.Contains("M-BNSF42", logs[0].Text);
            Assert.Contains("999",      logs[0].Text);
        }

        [Fact]
        public void LogLine_ContainsSpeedAndLimit() {
            var logs = Capture(out var cb);
            cb.UpdateTrainData(new TrainDataMessage {
                Train = TrainBuilder.Build(speedMph: 32.5f, speedLimitMph: 50),
            });

            Assert.Contains("32.5", logs[0].Text);
            Assert.Contains("50",   logs[0].Text);
        }

        [Fact]
        public void MonitorAccumulatesTrain_AfterCallback() {
            var monitor = new PtcMonitor();
            var cb      = new DispatcherCallback((_, _) => { }, monitor);

            cb.UpdateTrainData(new TrainDataMessage {
                Train = TrainBuilder.Build(trainId: 42, blockId: 300),
            });

            Assert.True(monitor.Trains.ContainsKey(42));
            Assert.Equal(300, monitor.Trains[42].BlockID);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static List<(string Text, ConsoleColor Color)> Capture(out DispatcherCallback cb) {
            var logs = new List<(string, ConsoleColor)>();
            cb = new DispatcherCallback((msg, color) => logs.Add((msg, color)));
            return logs;
        }
    }
}
