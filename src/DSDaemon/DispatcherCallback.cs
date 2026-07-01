using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using DSDaemon.Contracts;
using DSDaemon.Messages;

namespace DSDaemon {
    /// <summary>
    /// Receives all push callbacks from Run8 and writes them to the console (and optional log file).
    /// ConcurrencyMode.Multiple: Run8 may invoke callbacks concurrently; each Write call is
    /// already thread-safe on a modern console, and Log() serialises via a lock.
    /// UseSynchronizationContext = false: prevents deadlock when no sync context exists.
    /// </summary>
    [CallbackBehavior(ConcurrencyMode = ConcurrencyMode.Multiple, UseSynchronizationContext = false)]
    public sealed class DispatcherCallback : IDispatcher {
        private readonly Action<string, ConsoleColor> _write;

        public DispatcherCallback(Action<string, ConsoleColor> write) {
            _write = write;
        }

        public void Ping() {
            // Frequent keepalive; suppress unless verbose logging is added later.
        }

        public void SendSimulationState(SimulationStateMessage m) {
            _write($"[SIM]  {m.SimulationTime:ddd yyyy-MM-dd HH:mm}  " +
                   $"{(m.IsClient ? "MP-client" : "standalone/server")}",
                   ConsoleColor.Cyan);
        }

        public void PermissionUpdate(DispatcherPermissionMessage m) {
            var color = m.Permission == EDispatcherPermission.Granted ? ConsoleColor.Green
                      : m.Permission == EDispatcherPermission.Observer ? ConsoleColor.Yellow
                      : ConsoleColor.Red;
            _write($"[PERM] {m.Permission}  AI={m.AIPermission}", color);
        }

        public void SetSignals(SignalsMessage m) {
            if (m.Signals == null || m.Signals.Count == 0) return;

            var counts = new Dictionary<ESignalIndication, int>();
            foreach (var s in m.Signals)
                counts[s] = counts.TryGetValue(s, out int c) ? c + 1 : 1;

            var stop    = counts.TryGetValue(ESignalIndication.Stop,    out int n) ? n : 0;
            var proceed = counts.TryGetValue(ESignalIndication.Proceed, out int p) ? p : 0;
            var fleet   = counts.TryGetValue(ESignalIndication.Fleet,   out int f) ? f : 0;
            var flagby  = counts.TryGetValue(ESignalIndication.FlagBy,  out int g) ? g : 0;

            _write($"[SIG]  route={m.Route}  total={m.Signals.Count}  " +
                   $"stop={stop} proceed={proceed} fleet={fleet} flagby={flagby}",
                   ConsoleColor.White);
        }

        public void SetOccupiedBlocks(OccupiedBlocksMessage m) {
            var blocks = m.OccupiedBlocks?.Count ?? 0;
            var manual = m.OpenManualSwitchBlocks?.Count ?? 0;
            var color  = blocks > 0 ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
            _write($"[BLK]  route={m.Route}  occupied={blocks}  openManual={manual}" +
                   (blocks > 0 && blocks <= 20 ? $"  [{string.Join(",", m.OccupiedBlocks!)}]" : ""),
                   color);
        }

        public void SetOccupiedSwitches(OccupiedSwitchesMessage m) {
            var count = m.OccupiedSwitches?.Count ?? 0;
            _write($"[SW-OCC] route={m.Route}  count={count}" +
                   (count > 0 && count <= 20 ? $"  [{string.Join(",", m.OccupiedSwitches!)}]" : ""),
                   ConsoleColor.DarkYellow);
        }

        public void SetReversedSwitches(ReversedSwitchesMessage m) {
            var count = m.ReversedSwitches?.Count ?? 0;
            _write($"[SW-REV] route={m.Route}  count={count}" +
                   (count <= 20 ? $"  [{string.Join(",", m.ReversedSwitches ?? new List<int>())}]" : ""),
                   ConsoleColor.Magenta);
        }

        public void SetUnlockedSwitches(UnlockedSwitchesMessage m) {
            var count = m.UnlockedSwitches?.Count ?? 0;
            if (count == 0) return;
            _write($"[SW-UNL] route={m.Route}  count={count}  [{string.Join(",", m.UnlockedSwitches!)}]",
                   ConsoleColor.DarkCyan);
        }

        public void SetInterlockErrorSwitches(InterlockErrorSwitchesMessage m) {
            var count = m.InterlockErrorSwitches?.Count ?? 0;
            if (count == 0) return;
            _write($"[SW-ERR] route={m.Route}  count={count}  [{string.Join(",", m.InterlockErrorSwitches!)}]",
                   ConsoleColor.Red);
        }

        public void UpdateTrainData(TrainDataMessage m) {
            var t = m.Train;

            // PTC speed enforcement: flag any train exceeding its authority speed limit.
            bool speedViolation = t.TrainSpeedLimitMPH > 0 &&
                                  Math.Abs(t.TrainSpeedMph) > t.TrainSpeedLimitMPH;
            bool held           = t.HoldingForDispatcher;

            var color = speedViolation ? ConsoleColor.Red
                      : held           ? ConsoleColor.Magenta
                      : t.EngineerType == EEngineerType.Player ? ConsoleColor.Green
                      : t.EngineerType == EEngineerType.AI     ? ConsoleColor.DarkGreen
                      : ConsoleColor.DarkGray;

            string ptcTag = speedViolation ? " !! PTC OVERSPEED !!"
                          : held           ? " [HELD]"
                          : "";

            _write($"[TRN]  #{t.TrainID} {t.RailroadInitials}{t.LocoNumber,-6} [{t.TrainSymbol,-12}] " +
                   $"{t.TrainSpeedMph,6:F1}/{t.TrainSpeedLimitMPH}mph  blk={t.BlockID,5}  " +
                   $"{t.TrainWeightTons,6}t  {t.TrainLengthFeet,5}ft  {t.EngineerType}:{t.EngineerName}" +
                   ptcTag,
                   color);
        }

        public void RadioText(RadioTextMessage m) {
            _write($"[RADIO] ch={m.Channel}  \"{m.Text}\"", ConsoleColor.Blue);
        }

        public void DTMF(DTMFMessage m) {
            var color = m.DTMFType == EDTMFType.EmergencyTone ? ConsoleColor.Red : ConsoleColor.DarkBlue;
            _write($"[DTMF]  ch={m.Channel}  {m.DTMFType}  tone={m.Tone}  tower={m.TowerDescription}", color);
        }
    }
}
