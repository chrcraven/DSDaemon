using System;
using System.Collections.Generic;
using DSDaemon;
using DSDaemon.Messages;
using DSDaemon.Tests.Helpers;
using Xunit;

namespace DSDaemon.Tests.Scenarios {
    /// <summary>
    /// Verifies CommandConsole parses operator input and dispatches it to the
    /// commander, without touching an actual console.
    /// </summary>
    public class CommandConsoleTests {

        private sealed class CapturingCommander : IDispatcherCommander {
            public List<string> Calls = new();
            public void Hold(int trainId)    => Calls.Add($"Hold({trainId})");
            public void Release(int trainId) => Calls.Add($"Release({trainId})");
            public void Stop(int trainId)    => Calls.Add($"Stop({trainId})");
            public void Recrew(int trainId)  => Calls.Add($"Recrew({trainId})");
            public void Relinquish(int trainId, bool relinquish = true) =>
                Calls.Add($"Relinquish({trainId},{relinquish})");
            public void ChangeSignal(int signalId, ESignalIndication indication, bool automaticWorking = false) =>
                Calls.Add($"ChangeSignal({signalId},{indication},{automaticWorking})");
            public void ThrowSwitch(int switchId, ESwitchState state) =>
                Calls.Add($"ThrowSwitch({switchId},{state})");
        }

        private static (CapturingCommander cmdr, PtcMonitor monitor, List<string> logLines) Make() {
            var cmdr = new CapturingCommander();
            var monitor = new PtcMonitor();
            var lines = new List<string>();
            return (cmdr, monitor, lines);
        }

        private static bool Run(string line, CapturingCommander cmdr, PtcMonitor monitor, List<string> logLines) =>
            CommandConsole.Execute(line, cmdr, monitor, (msg, _) => logLines.Add(msg));

        [Fact]
        public void Signal_ParsesIdAndIndication() {
            var (cmdr, monitor, log) = Make();
            Run("signal 42 proceed", cmdr, monitor, log);

            Assert.Equal(new[] { "ChangeSignal(42,Proceed,False)" }, cmdr.Calls);
        }

        [Fact]
        public void Signal_CaseInsensitive_WithAutoFlag() {
            var (cmdr, monitor, log) = Make();
            Run("SIGNAL 42 STOP auto", cmdr, monitor, log);

            Assert.Equal(new[] { "ChangeSignal(42,Stop,True)" }, cmdr.Calls);
        }

        [Fact]
        public void Signal_UnknownIndication_DoesNotDispatch() {
            var (cmdr, monitor, log) = Make();
            Run("signal 42 bogus", cmdr, monitor, log);

            Assert.Empty(cmdr.Calls);
        }

        [Fact]
        public void Signal_MissingArgs_DoesNotDispatch() {
            var (cmdr, monitor, log) = Make();
            Run("signal 42", cmdr, monitor, log);

            Assert.Empty(cmdr.Calls);
        }

        [Fact]
        public void Switch_ParsesIdAndState() {
            var (cmdr, monitor, log) = Make();
            Run("switch 7 reverse", cmdr, monitor, log);

            Assert.Equal(new[] { "ThrowSwitch(7,Reverse)" }, cmdr.Calls);
        }

        [Theory]
        [InlineData("hold", "Hold(5)")]
        [InlineData("release", "Release(5)")]
        [InlineData("stop", "Stop(5)")]
        [InlineData("recrew", "Recrew(5)")]
        public void TrainOrders_DispatchCorrectly(string cmd, string expected) {
            var (cmdr, monitor, log) = Make();
            Run($"{cmd} 5", cmdr, monitor, log);

            Assert.Equal(new[] { expected }, cmdr.Calls);
        }

        [Fact]
        public void Relinquish_DefaultsOn() {
            var (cmdr, monitor, log) = Make();
            Run("relinquish 8", cmdr, monitor, log);

            Assert.Equal(new[] { "Relinquish(8,True)" }, cmdr.Calls);
        }

        [Fact]
        public void Relinquish_Off_SetsFalse() {
            var (cmdr, monitor, log) = Make();
            Run("relinquish 8 off", cmdr, monitor, log);

            Assert.Equal(new[] { "Relinquish(8,False)" }, cmdr.Calls);
        }

        [Fact]
        public void InvalidTrainId_LogsError_DoesNotThrow() {
            var (cmdr, monitor, log) = Make();
            var quit = Run("hold notanumber", cmdr, monitor, log);

            Assert.False(quit);
            Assert.Empty(cmdr.Calls);
            Assert.Contains(log, l => l.Contains("Invalid number"));
        }

        [Fact]
        public void UnknownCommand_LogsAndDoesNotThrow() {
            var (cmdr, monitor, log) = Make();
            var quit = Run("frobnicate", cmdr, monitor, log);

            Assert.False(quit);
            Assert.Empty(cmdr.Calls);
            Assert.Contains(log, l => l.Contains("Unknown command"));
        }

        [Theory]
        [InlineData("quit")]
        [InlineData("exit")]
        [InlineData("QUIT")]
        public void Quit_ReturnsTrue(string line) {
            var (cmdr, monitor, log) = Make();
            Assert.True(Run(line, cmdr, monitor, log));
        }

        [Fact]
        public void Help_DoesNotDispatchOrQuit() {
            var (cmdr, monitor, log) = Make();
            var quit = Run("help", cmdr, monitor, log);

            Assert.False(quit);
            Assert.Empty(cmdr.Calls);
            Assert.NotEmpty(log);
        }

        [Fact]
        public void EmptyLine_IsNoOp() {
            var (cmdr, monitor, log) = Make();
            var quit = Run("   ", cmdr, monitor, log);

            Assert.False(quit);
            Assert.Empty(cmdr.Calls);
            Assert.Empty(log);
        }
    }
}
