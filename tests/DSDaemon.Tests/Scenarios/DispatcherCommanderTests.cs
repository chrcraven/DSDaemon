using DSDaemon;
using DSDaemon.Messages;
using DSDaemon.Tests.Helpers;
using Xunit;

namespace DSDaemon.Tests.Scenarios {
    /// <summary>
    /// Verifies DispatcherCommander translates each command into the exact
    /// WCF message IRun8 expects.
    /// </summary>
    public class DispatcherCommanderTests {

        [Fact]
        public void Hold_SendsHoldAITrainMessage_WithHoldTrue() {
            var run8 = new FakeRun8();
            new DispatcherCommander(run8).Hold(42);

            var msg = Assert.Single(run8.Sent);
            var hold = Assert.IsType<HoldAITrainMessage>(msg);
            Assert.Equal(42, hold.TrainID);
            Assert.True(hold.HoldTrain);
        }

        [Fact]
        public void Release_SendsHoldAITrainMessage_WithHoldFalse() {
            var run8 = new FakeRun8();
            new DispatcherCommander(run8).Release(42);

            var hold = Assert.IsType<HoldAITrainMessage>(Assert.Single(run8.Sent));
            Assert.Equal(42, hold.TrainID);
            Assert.False(hold.HoldTrain);
        }

        [Fact]
        public void ChangeSignal_SendsDispatcherSignalMessage() {
            var run8 = new FakeRun8();
            new DispatcherCommander(run8).ChangeSignal(101, ESignalIndication.Proceed, automaticWorking: true);

            var sig = Assert.IsType<DispatcherSignalMessage>(Assert.Single(run8.Sent));
            Assert.Equal(101, sig.SignalID);
            Assert.Equal(ESignalIndication.Proceed, sig.SignalIndication);
            Assert.True(sig.AutomaticWorking);
        }

        [Fact]
        public void ChangeSignal_DefaultsAutomaticWorkingToFalse() {
            var run8 = new FakeRun8();
            new DispatcherCommander(run8).ChangeSignal(101, ESignalIndication.Stop);

            var sig = Assert.IsType<DispatcherSignalMessage>(Assert.Single(run8.Sent));
            Assert.False(sig.AutomaticWorking);
        }

        [Fact]
        public void ThrowSwitch_SendsDispatcherSwitchMessage() {
            var run8 = new FakeRun8();
            new DispatcherCommander(run8).ThrowSwitch(55, ESwitchState.Reverse);

            var sw = Assert.IsType<DispatcherSwitchMessage>(Assert.Single(run8.Sent));
            Assert.Equal(55, sw.SwitchID);
            Assert.Equal(ESwitchState.Reverse, sw.SwitchState);
        }

        [Fact]
        public void Stop_SendsStopAITrainMessage() {
            var run8 = new FakeRun8();
            new DispatcherCommander(run8).Stop(7);

            var msg = Assert.IsType<StopAITrainMessage>(Assert.Single(run8.Sent));
            Assert.Equal(7, msg.TrainID);
        }

        [Fact]
        public void Recrew_SendsAIRecrewTrainMessage() {
            var run8 = new FakeRun8();
            new DispatcherCommander(run8).Recrew(9);

            var msg = Assert.IsType<AIRecrewTrainMessage>(Assert.Single(run8.Sent));
            Assert.Equal(9, msg.TrainID);
        }

        [Fact]
        public void Relinquish_DefaultsToTrue() {
            var run8 = new FakeRun8();
            new DispatcherCommander(run8).Relinquish(3);

            var msg = Assert.IsType<RelinquishAITrainMessage>(Assert.Single(run8.Sent));
            Assert.Equal(3, msg.TrainID);
            Assert.True(msg.RelinquishTrain);
        }

        [Fact]
        public void Relinquish_CanBeSetToFalse() {
            var run8 = new FakeRun8();
            new DispatcherCommander(run8).Relinquish(3, relinquish: false);

            var msg = Assert.IsType<RelinquishAITrainMessage>(Assert.Single(run8.Sent));
            Assert.False(msg.RelinquishTrain);
        }
    }
}
