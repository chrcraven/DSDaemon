using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using DSDaemon.Contracts;
using DSDaemon.Messages;

namespace DSDaemon {
    /// <summary>
    /// Receives all push callbacks from Run8, updates the PtcMonitor, and writes
    /// colour-coded lines to the console (and any other sink passed as the write action).
    /// </summary>
    [CallbackBehavior(ConcurrencyMode = ConcurrencyMode.Multiple, UseSynchronizationContext = false)]
    public sealed class DispatcherCallback : IDispatcher {
        private readonly Action<string, ConsoleColor> _write;
        private readonly PtcMonitor _monitor;

        public DispatcherCallback(Action<string, ConsoleColor> write, PtcMonitor? monitor = null) {
            _write   = write;
            _monitor = monitor ?? new PtcMonitor();
        }

        // ── Keepalive ─────────────────────────────────────────────────────────

        public void Ping() { }

        // ── Simulation clock ──────────────────────────────────────────────────

        public void SendSimulationState(SimulationStateMessage m) {
            _write($"[SIM]  {m.SimulationTime:ddd yyyy-MM-dd HH:mm}  " +
                   $"{(m.IsClient ? "MP-client" : "standalone/server")}",
                   ConsoleColor.Cyan);
        }

        // ── Dispatcher access level ───────────────────────────────────────────

        public void PermissionUpdate(DispatcherPermissionMessage m) {
            var color = m.Permission == EDispatcherPermission.Granted  ? ConsoleColor.Green
                      : m.Permission == EDispatcherPermission.Observer ? ConsoleColor.Yellow
                      : ConsoleColor.Red;
            _write($"[PERM] {m.Permission}  AI={m.AIPermission}", color);
        }

        // ── Signals ───────────────────────────────────────────────────────────

        public void SetSignals(SignalsMessage m) {
            _monitor.UpdateSignals(m);
            if (m.Signals == null || m.Signals.Count == 0) return;

            var counts = new Dictionary<ESignalIndication, int>();
            foreach (var s in m.Signals)
                counts[s] = counts.TryGetValue(s, out int c) ? c + 1 : 1;

            int stop    = counts.TryGetValue(ESignalIndication.Stop,    out int n) ? n : 0;
            int proceed = counts.TryGetValue(ESignalIndication.Proceed, out int p) ? p : 0;
            int fleet   = counts.TryGetValue(ESignalIndication.Fleet,   out int f) ? f : 0;
            int flagby  = counts.TryGetValue(ESignalIndication.FlagBy,  out int g) ? g : 0;

            // Check authority conflict after updating monitor state.
            var conflict = _monitor.EvaluateRoute(m.Route);
            string conflictTag = conflict.IsConflict ? "  !! AUTHORITY CONFLICT !!" : "";
            var color = conflict.IsConflict ? ConsoleColor.Red : ConsoleColor.White;

            _write($"[SIG]  route={m.Route}  total={m.Signals.Count}  " +
                   $"stop={stop} proceed={proceed} fleet={fleet} flagby={flagby}" +
                   conflictTag, color);
        }

        // ── Block occupancy ───────────────────────────────────────────────────

        public void SetOccupiedBlocks(OccupiedBlocksMessage m) {
            _monitor.UpdateOccupiedBlocks(m);
            int blocks = m.OccupiedBlocks?.Count ?? 0;
            int manual = m.OpenManualSwitchBlocks?.Count ?? 0;
            var color  = blocks > 0 ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
            _write($"[BLK]  route={m.Route}  occupied={blocks}  openManual={manual}" +
                   (blocks > 0 && blocks <= 20
                       ? $"  [{string.Join(",", m.OccupiedBlocks!)}]" : ""),
                   color);
        }

        // ── Switch states ─────────────────────────────────────────────────────

        public void SetOccupiedSwitches(OccupiedSwitchesMessage m) {
            _monitor.UpdateOccupiedSwitches(m);
            int count = m.OccupiedSwitches?.Count ?? 0;
            _write($"[SW-OCC] route={m.Route}  count={count}" +
                   (count > 0 && count <= 20
                       ? $"  [{string.Join(",", m.OccupiedSwitches!)}]" : ""),
                   ConsoleColor.DarkYellow);
        }

        public void SetReversedSwitches(ReversedSwitchesMessage m) {
            _monitor.UpdateReversedSwitches(m);
            int count = m.ReversedSwitches?.Count ?? 0;
            _write($"[SW-REV] route={m.Route}  count={count}" +
                   (count <= 20
                       ? $"  [{string.Join(",", m.ReversedSwitches ?? new List<int>())}]" : ""),
                   ConsoleColor.Magenta);
        }

        public void SetUnlockedSwitches(UnlockedSwitchesMessage m) {
            _monitor.UpdateUnlockedSwitches(m);
            int count = m.UnlockedSwitches?.Count ?? 0;
            if (count == 0) return;
            _write($"[SW-UNL] route={m.Route}  count={count}  [{string.Join(",", m.UnlockedSwitches!)}]",
                   ConsoleColor.DarkCyan);
        }

        public void SetInterlockErrorSwitches(InterlockErrorSwitchesMessage m) {
            _monitor.UpdateInterlockErrors(m);
            int count = m.InterlockErrorSwitches?.Count ?? 0;
            if (count == 0) return;
            _write($"[SW-ERR] route={m.Route}  count={count}  [{string.Join(",", m.InterlockErrorSwitches!)}]",
                   ConsoleColor.Red);
        }

        // ── Train data (PTC evaluation) ───────────────────────────────────────

        public void UpdateTrainData(TrainDataMessage m) {
            _monitor.UpdateTrain(m.Train);
            var status = PtcMonitor.EvaluateTrain(m.Train);
            var t      = status.Train;

            var color = status.IsOverspeed   ? ConsoleColor.Red
                      : status.IsHeld        ? ConsoleColor.Magenta
                      : t.EngineerType == EEngineerType.Player ? ConsoleColor.Green
                      : t.EngineerType == EEngineerType.AI     ? ConsoleColor.DarkGreen
                      : ConsoleColor.DarkGray;

            string ptcTag = status.IsOverspeed ? " !! PTC OVERSPEED !!"
                          : status.IsHeld      ? " [HELD]"
                          : "";

            _write($"[TRN]  #{t.TrainID} {t.RailroadInitials}{t.LocoNumber,-6} [{t.TrainSymbol,-12}] " +
                   $"{t.TrainSpeedMph,6:F1}/{t.TrainSpeedLimitMPH}mph  blk={t.BlockID,5}  " +
                   $"{t.TrainWeightTons,6}t  {t.TrainLengthFeet,5}ft  {t.EngineerType}:{t.EngineerName}" +
                   ptcTag, color);
        }

        // ── Radio ─────────────────────────────────────────────────────────────

        public void RadioText(RadioTextMessage m) {
            _write($"[RADIO] ch={m.Channel}  \"{m.Text}\"", ConsoleColor.Blue);
        }

        public void DTMF(DTMFMessage m) {
            var color = m.DTMFType == EDTMFType.EmergencyTone ? ConsoleColor.Red : ConsoleColor.DarkBlue;
            _write($"[DTMF]  ch={m.Channel}  {m.DTMFType}  tone={m.Tone}  tower={m.TowerDescription}", color);
        }
    }
}
