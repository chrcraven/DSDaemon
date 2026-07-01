using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DSDaemon.Messages;

namespace DSDaemon.Discovery {

    public enum DiscoveryState { Initializing, WaitingToSelect, Released }

    /// <summary>
    /// Empirical route discovery via controlled scouting:
    ///
    ///   Initializing  — holds every AI train that arrives via UpdateTrainData.
    ///   WaitingToSelect — picks the next eligible held scout and releases it.
    ///   Released       — watches for the scout's BlockID to change; on change
    ///                    records the adjacency, re-holds the scout, selects next.
    ///
    /// While Released, the engine also aids the scout by clearing its own path:
    /// see OnSignalsUpdated/OnInterlockErrorsUpdated below.
    ///
    /// Scout eligibility is based purely on our own _heldByUs bookkeeping, not on
    /// Run8 echoing TrainData.HoldingForDispatcher back true: on a live session,
    /// Run8 never confirmed that flag for a dispatcher-issued AI hold, which left
    /// the engine stuck in WaitingToSelect forever. Trusting our own commanded
    /// state means scouts get released (and their route's signals/switches get
    /// cleared) as soon as we've seen the train once, at the cost of occasionally
    /// releasing a train Run8 hasn't actually finished holding yet — acceptable
    /// for this discovery-only, single-scout-at-a-time use.
    ///
    /// Thread-safe: all mutable state is protected by a single lock.
    /// WCF callbacks arrive on concurrent threads (ConcurrencyMode.Multiple).
    /// </summary>
    public sealed class RouteDiscoveryEngine : IDisposable {

        private readonly RouteMap _map;
        private readonly IDispatcherCommander _commander;
        private readonly TimeSpan _scoutTimeout;
        private readonly Action<string, ConsoleColor> _log;
        private readonly object _lock = new();

        // Latest TrainData per train ID.
        private readonly Dictionary<int, TrainData> _trains = new();
        // Trains we have sent a Hold command to and treat as held, regardless of
        // whether Run8 has echoed HoldingForDispatcher=true back to us.
        private readonly HashSet<int> _heldByUs = new();
        // Block → route membership (populated from SetOccupiedBlocks callbacks).
        private readonly Dictionary<int, int> _blockRoute = new();
        // Signal indices already commanded to Proceed for the current scouting run
        // (SignalsMessage carries no ID — we treat list position as SignalID, an
        // assumption unverified against the real Run8 wire behaviour; watch for
        // "[DISC-AUTO]" log lines and the following SetSignals callback to confirm).
        private readonly HashSet<int> _clearedSignalIndices = new();
        // Switch IDs already unlocked for the current scouting run (these are
        // genuine wire IDs — InterlockErrorSwitches reports real SwitchIDs).
        private readonly HashSet<int> _unlockedInterlockSwitches = new();

        private DiscoveryState _state = DiscoveryState.Initializing;
        private int?  _scoutTrainId;
        private int   _scoutFromBlock;
        private int   _scoutRoute;
        private int?  _lastScoutTrainId;
        private CancellationTokenSource? _timeoutCts;
        private bool  _disposed;

        public DiscoveryState State      { get { lock (_lock) return _state; } }
        public int?           ScoutTrainId { get { lock (_lock) return _scoutTrainId; } }

        public RouteDiscoveryEngine(
            RouteMap map,
            IDispatcherCommander commander,
            TimeSpan scoutTimeout = default,
            Action<string, ConsoleColor>? log = null) {
            _map          = map;
            _commander    = commander;
            _scoutTimeout = scoutTimeout == default ? TimeSpan.FromSeconds(30) : scoutTimeout;
            _log          = log ?? ((_, _) => { });
        }

        // ── Public entry points ───────────────────────────────────────────────

        /// <summary>
        /// Leaves Initializing and begins scouting.
        /// Call once the initial flood of UpdateTrainData callbacks has settled.
        /// </summary>
        public void Start() {
            lock (_lock) {
                if (_state != DiscoveryState.Initializing) return;
                _state = DiscoveryState.WaitingToSelect;
                _log("[DISC] Discovery started", ConsoleColor.Cyan);
                TrySelectScout();
            }
        }

        public void OnTrainDataReceived(TrainData train) {
            lock (_lock) {
                if (_disposed) return;
                _trains[train.TrainID] = train;

                // Hold any AI train we haven't commanded yet.
                if (train.EngineerType == EEngineerType.AI && !_heldByUs.Contains(train.TrainID)) {
                    _heldByUs.Add(train.TrainID);
                    _commander.Hold(train.TrainID);
                    _log($"[DISC] Holding AI train #{train.TrainID} (blk={train.BlockID})", ConsoleColor.DarkYellow);
                }

                // Try to pick a scout any time a train update lands while we're
                // waiting. We deliberately do NOT gate this on
                // train.HoldingForDispatcher: in practice Run8 never echoes that
                // flag back true for AI holds issued by the external dispatcher
                // (observed over a full live session — every UpdateTrainData for a
                // held AI train kept reporting HoldingForDispatcher=false forever),
                // so requiring it left discovery stuck in WaitingToSelect
                // permanently. We trust our own _heldByUs bookkeeping instead.
                if (_state == DiscoveryState.WaitingToSelect)
                    TrySelectScout();

                // Scout moved to a new block.
                if (_state == DiscoveryState.Released && train.TrainID == _scoutTrainId
                    && train.BlockID != 0 && train.BlockID != _scoutFromBlock)
                    RecordTransition(train.BlockID);
            }
        }

        public void OnOccupiedBlocksUpdated(OccupiedBlocksMessage msg) {
            lock (_lock) {
                if (_disposed || msg.OccupiedBlocks == null) return;
                foreach (var blockId in msg.OccupiedBlocks)
                    _blockRoute[blockId] = msg.Route;
            }
        }

        /// <summary>
        /// Aids the released scout by forcing any Stop signal on its route to
        /// Proceed. Safe only because discovery holds every other train — with
        /// just the scout moving on this route, clearing every signal on it
        /// cannot create a real conflicting movement authority.
        /// </summary>
        public void OnSignalsUpdated(SignalsMessage msg) {
            lock (_lock) {
                if (_disposed || _state != DiscoveryState.Released) return;
                if (msg.Route != _scoutRoute || msg.Signals == null) return;

                for (int i = 0; i < msg.Signals.Count; i++) {
                    if (msg.Signals[i] != ESignalIndication.Stop) continue;
                    if (!_clearedSignalIndices.Add(i)) continue;
                    _commander.ChangeSignal(i, ESignalIndication.Proceed, automaticWorking: true);
                    _log($"[DISC-AUTO] route {msg.Route}: signal {i} Stop→Proceed (aiding scout #{_scoutTrainId})",
                         ConsoleColor.DarkCyan);
                }
            }
        }

        /// <summary>
        /// Aids the released scout by unlocking any switch reporting an
        /// interlock error on its route — a genuine wire SwitchID, unlike
        /// signal indices, so this is unambiguous.
        /// </summary>
        public void OnInterlockErrorsUpdated(InterlockErrorSwitchesMessage msg) {
            lock (_lock) {
                if (_disposed || _state != DiscoveryState.Released) return;
                if (msg.Route != _scoutRoute || msg.InterlockErrorSwitches == null) return;

                foreach (var switchId in msg.InterlockErrorSwitches) {
                    if (!_unlockedInterlockSwitches.Add(switchId)) continue;
                    _commander.ThrowSwitch(switchId, ESwitchState.Unlock);
                    _log($"[DISC-AUTO] route {msg.Route}: switch {switchId} unlocked (interlock error, aiding scout #{_scoutTrainId})",
                         ConsoleColor.DarkCyan);
                }
            }
        }

        // ── State machine (all called under _lock) ────────────────────────────

        private void TrySelectScout() {
            // Prefer any eligible train other than the one we just finished
            // scouting, so one fast-moving train doesn't hog every turn. This
            // matters now that we no longer wait for Run8's hold confirmation
            // before considering a train eligible again (see OnTrainDataReceived) —
            // without this, a lone re-held scout would otherwise become eligible
            // again immediately. Fall back to re-selecting it if it's the only
            // held AI train around, so a single train still keeps exploring
            // rather than sitting held forever.
            if (TrySelectScoutFrom(skipId: _lastScoutTrainId)) return;
            TrySelectScoutFrom(skipId: null);
        }

        private bool TrySelectScoutFrom(int? skipId) {
            foreach (var (id, train) in _trains) {
                if (id == skipId) continue;
                if (train.EngineerType != EEngineerType.AI) continue;
                if (train.BlockID == 0) continue;
                // Trust our own hold bookkeeping rather than Run8's
                // HoldingForDispatcher confirmation — see OnTrainDataReceived.
                if (!_heldByUs.Contains(id)) continue;

                _scoutTrainId     = id;
                _lastScoutTrainId = id;
                _scoutFromBlock   = train.BlockID;
                // SetOccupiedBlocks reports raw per-route block numbers (e.g. 231),
                // but TrainData.BlockID has been observed on live Run8 data as a
                // composite route*1000+block encoding (e.g. 110231 for route 110,
                // block 231). Prefer an exact _blockRoute match (covers routes with
                // raw, non-composite numbering) and fall back to decoding the
                // composite form otherwise.
                _scoutRoute     = _blockRoute.TryGetValue(train.BlockID, out int r) ? r : train.BlockID / 1000;
                _state          = DiscoveryState.Released;
                _clearedSignalIndices.Clear();
                _unlockedInterlockSwitches.Clear();
                _heldByUs.Remove(id);
                _commander.Release(id);
                _log($"[DISC] Scout #{id} released from blk {_scoutFromBlock} (route {_scoutRoute})",
                     ConsoleColor.Yellow);
                StartTimeout(id);
                return true;
            }
            return false;
        }

        private void RecordTransition(int toBlock) {
            _timeoutCts?.Cancel();
            _timeoutCts = null;

            int fromBlock = _scoutFromBlock;
            int route     = _scoutRoute;
            int trainId   = _scoutTrainId!.Value;

            _map.RecordAdjacency(route, fromBlock, toBlock);
            _log($"[DISC] route {route}: blk {fromBlock} ↔ {toBlock} (train #{trainId})", ConsoleColor.Green);

            _heldByUs.Add(trainId);
            _commander.Hold(trainId);

            _state        = DiscoveryState.WaitingToSelect;
            _scoutTrainId = null;
            TrySelectScout();
        }

        private void StartTimeout(int trainId) {
            _timeoutCts?.Cancel();
            _timeoutCts = new CancellationTokenSource();
            var ct      = _timeoutCts.Token;

            _ = Task.Run(async () => {
                try {
                    await Task.Delay(_scoutTimeout, ct).ConfigureAwait(false);
                } catch (OperationCanceledException) { return; }

                lock (_lock) {
                    if (_disposed || _state != DiscoveryState.Released || _scoutTrainId != trainId) return;
                    _log($"[DISC] Scout #{trainId} timed out — re-holding", ConsoleColor.DarkYellow);
                    _heldByUs.Add(trainId);
                    _commander.Hold(trainId);
                    _state        = DiscoveryState.WaitingToSelect;
                    _scoutTrainId = null;
                    TrySelectScout();
                }
            }, CancellationToken.None);
        }

        public void Dispose() {
            lock (_lock) {
                _disposed = true;
                _timeoutCts?.Cancel();
                _timeoutCts?.Dispose();
                _timeoutCts = null;
            }
        }
    }
}
