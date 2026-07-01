using System.Collections.Generic;
using DSDaemon.Messages;

namespace DSDaemon.Tests.Helpers {
    /// <summary>
    /// Factory helpers for infrastructure messages used in scenario tests.
    /// </summary>
    internal static class MessageBuilder {
        public static OccupiedBlocksMessage OccupiedBlocks(
            int route, IEnumerable<int> blocks, IEnumerable<int>? openManual = null
        ) => new OccupiedBlocksMessage {
            Route               = route,
            OccupiedBlocks      = new List<int>(blocks),
            OpenManualSwitchBlocks = new List<int>(openManual ?? System.Array.Empty<int>()),
        };

        public static SignalsMessage Signals(int route, IEnumerable<ESignalIndication> indications) =>
            new SignalsMessage {
                Route   = route,
                Signals = new List<ESignalIndication>(indications),
            };

        public static SignalsMessage AllStop(int route, int count) =>
            Signals(route, System.Linq.Enumerable.Repeat(ESignalIndication.Stop, count));

        public static InterlockErrorSwitchesMessage InterlockErrors(int route, params int[] switchIds) =>
            new InterlockErrorSwitchesMessage {
                Route                 = route,
                InterlockErrorSwitches = new List<int>(switchIds),
            };

        public static ReversedSwitchesMessage ReversedSwitches(int route, params int[] switchIds) =>
            new ReversedSwitchesMessage {
                Route            = route,
                ReversedSwitches = new List<int>(switchIds),
            };

        public static UnlockedSwitchesMessage UnlockedSwitches(int route, params int[] switchIds) =>
            new UnlockedSwitchesMessage {
                Route            = route,
                UnlockedSwitches = new List<int>(switchIds),
            };
    }
}
