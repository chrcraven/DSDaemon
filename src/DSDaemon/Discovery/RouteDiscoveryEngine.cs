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
        // Trains we have sent a Hold command to (may not be confirmed yet).
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

                // Newly confirmed as held → maybe now eligible to be a scout.
                if (_state == DiscoveryState.WaitingToSelect && train.HoldingForDispatcher)
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
            foreach (var (id, train) in _trains) {
                if (train.EngineerType != EEngineerType.AI) continue;
                if (train.BlockID == 0) continue;
                // Require Run8 confirmation AND that we are the one holding it.
                if (!train.HoldingForDispatcher) continue;
                if (!_heldByUs.Contains(id)) continue;

                _scoutTrainId   = id;
                _scoutFromBlock = train.BlockID;
                _scoutRoute     = _blockRoute.TryGetValue(train.BlockID, out int r) ? r : 0;
                _state          = DiscoveryState.Released;
                _clearedSignalIndices.Clear();
                _unlockedInterlockSwitches.Clear();
                _heldByUs.Remove(id);
                _commander.Release(id);
                _log($"[DISC] Scout #{id} released from blk {_scoutFromBlock} (route {_scoutRoute})",
                     ConsoleColor.Yellow);
                StartTimeout(id);
                return;
            }
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
