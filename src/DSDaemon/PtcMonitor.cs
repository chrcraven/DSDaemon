using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DSDaemon.Messages;

namespace DSDaemon {

    // ── Result types ──────────────────────────────────────────────────────────

    public sealed record PtcTrainStatus(
        TrainData Train,
        bool IsOverspeed,
        bool IsHeld,
        bool IsRelinquishing) {
        public bool HasAnyFlag => IsOverspeed || IsHeld || IsRelinquishing;
    }

    // Two or more trains report the same BlockID — indicates a collision risk.
    public sealed record BlockOccupancyConflict(int BlockId, IReadOnlyList<int> TrainIds);

    // A route has occupied blocks AND at least one Proceed/Fleet signal — coarse
    // authority conflict: something is cleared that shouldn't be (or vice versa).
    public sealed record RouteConflictStatus(
        int Route,
        bool HasOccupiedBlocks,
        bool HasProceedSignals) {
        public bool IsConflict => HasOccupiedBlocks && HasProceedSignals;
    }

    // ── Monitor ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Accumulates sim state from Run8 callbacks and exposes PTC evaluation methods.
    /// Thread-safe: all collections are ConcurrentDictionary; evaluation methods
    /// take point-in-time snapshots.
    /// </summary>
    public sealed class PtcMonitor {
        private readonly ConcurrentDictionary<int, TrainData>                   _trains         = new();
        private readonly ConcurrentDictionary<int, OccupiedBlocksMessage>       _occupiedBlocks = new();
        private readonly ConcurrentDictionary<int, SignalsMessage>              _signals        = new();
        private readonly ConcurrentDictionary<int, ReversedSwitchesMessage>    _reversed       = new();
        private readonly ConcurrentDictionary<int, UnlockedSwitchesMessage>    _unlocked       = new();
        private readonly ConcurrentDictionary<int, InterlockErrorSwitchesMessage> _interlocks  = new();
        private readonly ConcurrentDictionary<int, OccupiedSwitchesMessage>    _occupiedSw     = new();

        // ── State updates ─────────────────────────────────────────────────────

        public void UpdateTrain(TrainData t)                   => _trains[t.TrainID]     = t;
        public void UpdateOccupiedBlocks(OccupiedBlocksMessage m) => _occupiedBlocks[m.Route] = m;
        public void UpdateSignals(SignalsMessage m)             => _signals[m.Route]      = m;
        public void UpdateReversedSwitches(ReversedSwitchesMessage m) => _reversed[m.Route]  = m;
        public void UpdateUnlockedSwitches(UnlockedSwitchesMessage m) => _unlocked[m.Route]  = m;
        public void UpdateInterlockErrors(InterlockErrorSwitchesMessage m) => _interlocks[m.Route] = m;
        public void UpdateOccupiedSwitches(OccupiedSwitchesMessage m) => _occupiedSw[m.Route] = m;

        // ── Per-train evaluation (static: needs only a single TrainData) ──────

        public static PtcTrainStatus EvaluateTrain(TrainData t) {
            // Speed limit of 0 means Run8 hasn't reported a limit yet — skip enforcement.
            bool overspeed = t.TrainSpeedLimitMPH > 0 &&
                             Math.Abs(t.TrainSpeedMph) > t.TrainSpeedLimitMPH;
            return new PtcTrainStatus(
                Train:           t,
                IsOverspeed:     overspeed,
                IsHeld:          t.HoldingForDispatcher,
                IsRelinquishing: t.RelinquishWhenStopped);
        }

        // ── Multi-train block conflict (requires accumulated train state) ──────

        /// <summary>
        /// Returns every block where two or more trains report the same BlockID.
        /// BlockID == 0 is excluded (means "not yet known").
        /// </summary>
        public IReadOnlyList<BlockOccupancyConflict> FindMultiOccupiedBlocks() =>
            _trains.Values
                   .Where(t => t.BlockID != 0)
                   .GroupBy(t => t.BlockID)
                   .Where(g => g.Count() > 1)
                   .Select(g => new BlockOccupancyConflict(
                       g.Key, g.Select(t => t.TrainID).OrderBy(id => id).ToList()))
                   .ToList();

        // ── Route-level signal / occupancy conflict ────────────────────────────

        /// <summary>
        /// Flags routes where the occupied-block list is non-empty AND at least one
        /// signal is Proceed or Fleet. This is a coarse authority-conflict indicator:
        /// a block is occupied but a signal ahead of it is still cleared.
        /// </summary>
        public RouteConflictStatus EvaluateRoute(int route) {
            bool hasOccupied = _occupiedBlocks.TryGetValue(route, out var blk)
                               && (blk.OccupiedBlocks?.Count ?? 0) > 0;

            bool hasProceed = _signals.TryGetValue(route, out var sig)
                              && sig.Signals != null
                              && sig.Signals.Any(s => s == ESignalIndication.Proceed
                                                   || s == ESignalIndication.Fleet);

            return new RouteConflictStatus(route, hasOccupied, hasProceed);
        }

        // ── Switch-state queries ──────────────────────────────────────────────

        public IReadOnlyList<int> GetInterlockErrors(int route) =>
            _interlocks.TryGetValue(route, out var m) && m.InterlockErrorSwitches != null
                ? m.InterlockErrorSwitches
                : Array.Empty<int>();

        public IReadOnlyList<int> GetReversedSwitches(int route) =>
            _reversed.TryGetValue(route, out var m) && m.ReversedSwitches != null
                ? m.ReversedSwitches
                : Array.Empty<int>();

        public IReadOnlyList<int> GetUnlockedSwitches(int route) =>
            _unlocked.TryGetValue(route, out var m) && m.UnlockedSwitches != null
                ? m.UnlockedSwitches
                : Array.Empty<int>();

        // ── Snapshot ─────────────────────────────────────────────────────────

        public IReadOnlyDictionary<int, TrainData> Trains => _trains;
    }
}
