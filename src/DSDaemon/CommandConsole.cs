using System;
using System.Globalization;
using System.Linq;
using DSDaemon.Messages;

namespace DSDaemon {
    /// <summary>
    /// Parses one operator command line and dispatches it against an
    /// IDispatcherCommander. Kept separate from the stdin read loop so it can
    /// be unit tested without a console.
    /// </summary>
    public static class CommandConsole {
        public static void PrintHelp(Action<string, ConsoleColor> log) {
            log("Commands:", ConsoleColor.Cyan);
            log("  signal <id> <stop|proceed|fleet|flagby> [auto]  Change a signal indication", ConsoleColor.Gray);
            log("  switch <id> <normal|reverse|unlock|relock>      Throw/lock a switch", ConsoleColor.Gray);
            log("  hold <trainId>                                  Hold an AI train", ConsoleColor.Gray);
            log("  release <trainId>                                Release a held AI train", ConsoleColor.Gray);
            log("  stop <trainId>                                  Stop an AI train immediately", ConsoleColor.Gray);
            log("  recrew <trainId>                                Recrew an AI train", ConsoleColor.Gray);
            log("  relinquish <trainId> [on|off]                   Relinquish AI control (default on)", ConsoleColor.Gray);
            log("  trains                                          List known trains", ConsoleColor.Gray);
            log("  help                                            Show this help", ConsoleColor.Gray);
            log("  quit | exit                                     Shut down DSDaemon", ConsoleColor.Gray);
        }

        /// <summary>
        /// Parses and dispatches one command line against the live commander.
        /// Returns true if the operator asked to shut down (quit/exit).
        /// </summary>
        public static bool Execute(
            string line,
            IDispatcherCommander commander,
            PtcMonitor monitor,
            Action<string, ConsoleColor> log) {

            var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            var cmd = parts[0].ToLowerInvariant();

            try {
                switch (cmd) {
                    case "help":
                    case "?":
                        PrintHelp(log);
                        return false;

                    case "quit":
                    case "exit":
                        log("[CMD] Shutting down...", ConsoleColor.Yellow);
                        return true;

                    case "signal":
                        ExecuteSignal(parts, commander, log);
                        return false;

                    case "switch":
                        ExecuteSwitch(parts, commander, log);
                        return false;

                    case "hold":
                    case "release":
                    case "stop":
                    case "recrew":
                        ExecuteTrainOrder(cmd, parts, commander, log);
                        return false;

                    case "relinquish":
                        ExecuteRelinquish(parts, commander, log);
                        return false;

                    case "trains":
                        ListTrains(monitor, log);
                        return false;

                    default:
                        log($"[CMD] Unknown command '{cmd}' — type 'help' for a list", ConsoleColor.Yellow);
                        return false;
                }
            } catch (FormatException) {
                log($"[CMD] Invalid number in '{line}'", ConsoleColor.Yellow);
                return false;
            }
        }

        private static void ExecuteSignal(string[] parts, IDispatcherCommander commander, Action<string, ConsoleColor> log) {
            if (parts.Length < 3) {
                log("[CMD] Usage: signal <id> <stop|proceed|fleet|flagby> [auto]", ConsoleColor.Yellow);
                return;
            }
            int id = int.Parse(parts[1], CultureInfo.InvariantCulture);
            if (!Enum.TryParse<ESignalIndication>(parts[2], ignoreCase: true, out var indication)) {
                log($"[CMD] Unknown signal indication '{parts[2]}'", ConsoleColor.Yellow);
                return;
            }
            bool auto = parts.Length > 3 && parts[3].Equals("auto", StringComparison.OrdinalIgnoreCase);
            commander.ChangeSignal(id, indication, auto);
            log($"[CMD] Signal {id} → {indication}{(auto ? " (auto)" : "")}", ConsoleColor.Cyan);
        }

        private static void ExecuteSwitch(string[] parts, IDispatcherCommander commander, Action<string, ConsoleColor> log) {
            if (parts.Length < 3) {
                log("[CMD] Usage: switch <id> <normal|reverse|unlock|relock>", ConsoleColor.Yellow);
                return;
            }
            int id = int.Parse(parts[1], CultureInfo.InvariantCulture);
            if (!Enum.TryParse<ESwitchState>(parts[2], ignoreCase: true, out var state)) {
                log($"[CMD] Unknown switch state '{parts[2]}'", ConsoleColor.Yellow);
                return;
            }
            commander.ThrowSwitch(id, state);
            log($"[CMD] Switch {id} → {state}", ConsoleColor.Cyan);
        }

        private static void ExecuteTrainOrder(string cmd, string[] parts, IDispatcherCommander commander, Action<string, ConsoleColor> log) {
            if (parts.Length < 2) {
                log($"[CMD] Usage: {cmd} <trainId>", ConsoleColor.Yellow);
                return;
            }
            int trainId = int.Parse(parts[1], CultureInfo.InvariantCulture);
            switch (cmd) {
                case "hold":    commander.Hold(trainId);    break;
                case "release": commander.Release(trainId); break;
                case "stop":    commander.Stop(trainId);    break;
                case "recrew":  commander.Recrew(trainId);  break;
            }
            log($"[CMD] {cmd} train #{trainId}", ConsoleColor.Cyan);
        }

        private static void ExecuteRelinquish(string[] parts, IDispatcherCommander commander, Action<string, ConsoleColor> log) {
            if (parts.Length < 2) {
                log("[CMD] Usage: relinquish <trainId> [on|off]", ConsoleColor.Yellow);
                return;
            }
            int trainId = int.Parse(parts[1], CultureInfo.InvariantCulture);
            bool on = parts.Length < 3 || !parts[2].Equals("off", StringComparison.OrdinalIgnoreCase);
            commander.Relinquish(trainId, on);
            log($"[CMD] relinquish train #{trainId} = {on}", ConsoleColor.Cyan);
        }

        private static void ListTrains(PtcMonitor monitor, Action<string, ConsoleColor> log) {
            if (monitor.Trains.Count == 0) {
                log("[CMD] No trains known yet.", ConsoleColor.Gray);
                return;
            }
            foreach (var t in monitor.Trains.Values.OrderBy(t => t.TrainID))
                log($"  #{t.TrainID} {t.RailroadInitials}{t.LocoNumber} [{t.TrainSymbol}] blk={t.BlockID} {t.EngineerType}" +
                    (t.HoldingForDispatcher ? " [HELD]" : ""), ConsoleColor.Gray);
        }
    }
}
