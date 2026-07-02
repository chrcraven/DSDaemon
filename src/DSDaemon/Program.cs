using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSDaemon;
using DSDaemon.Discovery;
using DSDaemon.Discovery.StaticImport;

// Default log naming/retention: only the most recent MaxLogFilesToKeep
// default-named logs are kept; this pruning never applies to an explicit
// --log-file path (that's the operator's own file to manage).
const int MaxLogFilesToKeep = 10;
const string DefaultLogPrefix = "dsdaemon-";
const string DefaultLogSuffix = ".log";

// ── Parse arguments ──────────────────────────────────────────────────────────
string  host         = "localhost";
int     port         = 15192;
string  logPath      = $"{DefaultLogPrefix}{DateTime.Now:yyyyMMdd-HHmmss}{DefaultLogSuffix}";
bool    logPathSet   = false;
bool    discoverMode = false;
string  routeMapPath = "route-map.json";
string? routesDir    = null;

for (int i = 0; i < args.Length; i++) {
    switch (args[i]) {
        case "--host"       when i + 1 < args.Length: host         = args[++i]; break;
        case "--port"       when i + 1 < args.Length: port         = int.Parse(args[++i]); break;
        case "--log-file"   when i + 1 < args.Length: logPath      = args[++i]; logPathSet = true; break;
        case "--route-map"  when i + 1 < args.Length: routeMapPath = args[++i]; break;
        case "--routes-dir" when i + 1 < args.Length: routesDir    = args[++i]; break;
        case "--discover":  discoverMode = true; break;
        case "-h":
        case "--help":
            Console.WriteLine("Usage: DSDaemon [--host <h>] [--port <p>] [--log-file <path>]");
            Console.WriteLine("                [--discover] [--route-map <path>] [--routes-dir <path>]");
            Console.WriteLine("  --host        Run8 host (default: localhost)");
            Console.WriteLine($"  --port        Run8 WCF port (default: {port})");
            Console.WriteLine("  --log-file    Append all output to this file as well");
            Console.WriteLine( "                (default: dsdaemon-<timestamp>.log — always written, so a");
            Console.WriteLine( "                run's output can be handed back for debugging; only the");
            Console.WriteLine($"                last {MaxLogFilesToKeep} default-named logs are kept, older ones are pruned)");
            Console.WriteLine("  --discover    Enable empirical route discovery mode");
            Console.WriteLine("  --route-map   Route map JSON path (default: route-map.json)");
            Console.WriteLine("  --routes-dir  Root directory holding one subfolder per installed Run8 route;");
            Console.WriteLine("                on startup, parses each route's TrackDatabase.r8 +");
            Console.WriteLine("                BlockDetectorDatabase.r8 into the route map as a static baseline");
            Console.WriteLine("                (loads/saves route-map.json even without --discover)");
            return;
    }
}

// ── Logging ───────────────────────────────────────────────────────────────────
// Always writes to a file — by default a fresh timestamped one per run — so the
// full session output is available to hand back for debugging even when nobody
// was watching the console live.
var logDir = Path.GetDirectoryName(logPath);
var logDirOrCurrent = string.IsNullOrEmpty(logDir) ? "." : logDir;
if (!string.IsNullOrEmpty(logDir)) Directory.CreateDirectory(logDir);

int prunedCount = 0;
if (!logPathSet) {
    var oldLogs = Directory.GetFiles(logDirOrCurrent, $"{DefaultLogPrefix}*{DefaultLogSuffix}")
                            .OrderByDescending(f => f, StringComparer.Ordinal)
                            .Skip(MaxLogFilesToKeep - 1);
    foreach (var old in oldLogs) {
        try { File.Delete(old); prunedCount++; } catch { /* best effort */ }
    }
}

var fileWriter = new StreamWriter(logPath, append: logPathSet, encoding: System.Text.Encoding.UTF8) {
    AutoFlush = true,
};

var consoleLock = new object();

void Log(string message, ConsoleColor color = ConsoleColor.White) {
    var line = $"{DateTime.Now:HH:mm:ss.fff}  {message}";
    lock (consoleLock) {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(line);
        Console.ForegroundColor = prev;
    }
    fileWriter.WriteLine(line);
}

// ── Banner ────────────────────────────────────────────────────────────────────
Log("DSDaemon — Run8 Southern California External Dispatcher (v2 console prototype)", ConsoleColor.Cyan);
Log($"Target: net.tcp://{host}:{port}/Run8", ConsoleColor.Gray);
Log($"Logging to:   {logPath}", ConsoleColor.Gray);
if (prunedCount > 0)       Log($"Pruned {prunedCount} old log file(s), keeping the last {MaxLogFilesToKeep}", ConsoleColor.DarkGray);
if (discoverMode)          Log($"Discovery ON  route-map: {routeMapPath}", ConsoleColor.Cyan);
if (routesDir != null)     Log($"Static import ON  routes-dir: {routesDir}", ConsoleColor.Cyan);
Log("Press Ctrl+C to exit. Type 'help' for dispatcher commands.", ConsoleColor.Gray);
Log(new string('─', 80), ConsoleColor.DarkGray);

// Load route map once; persist across reconnects. Needed either for live
// discovery or for a one-time static import (or both, sharing the same map
// and file — the static import just seeds a baseline that discovery keeps
// building on).
RouteMap? routeMap = (discoverMode || routesDir != null) ? RouteMap.LoadOrCreate(routeMapPath) : null;

// Defense in depth: serializes the file write itself in case a save is ever
// triggered from more than one place at once (incremental saves already run
// under the discovery engine's own lock, serialized with map mutation).
var routeMapSaveLock = new object();
void SaveRouteMap() {
    if (routeMap == null) return;
    lock (routeMapSaveLock) {
        try { routeMap.Save(routeMapPath); }
        catch (Exception ex) { Log($"[DISC] Route map save failed: {ex.Message}", ConsoleColor.Red); }
    }
}

// Static import runs once at startup, before any WCF connection — it's pure
// file I/O against the local Run8 install, independent of discovery/reconnect.
if (routeMap != null && routesDir != null) {
    var result = StaticRouteMapImporter.ImportAll(routeMap, routesDir, Log);
    Log($"[STATIC] Imported {result.RoutesImported} route(s), {result.EdgesRecorded} block edge(s) total",
        ConsoleColor.Cyan);
    SaveRouteMap();
}

// ── Connect and run ───────────────────────────────────────────────────────────
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => {
    e.Cancel = true;
    Log("Shutting down...", ConsoleColor.Yellow);
    cts.Cancel();
};

// ── Interactive dispatcher command console ────────────────────────────────────
// Runs for the life of the process; targets whichever commander/monitor is
// "live" for the current connection (updated on connect/disconnect below).
IDispatcherCommander? activeCommander = null;
PtcMonitor?           activeMonitor   = null;

_ = Task.Run(() => {
    while (!cts.Token.IsCancellationRequested) {
        string? line;
        try { line = Console.ReadLine(); }
        catch { break; }
        if (line == null) break; // stdin closed (e.g. redirected/non-interactive)
        if (string.IsNullOrWhiteSpace(line)) continue;

        var commander = activeCommander;
        var monitor   = activeMonitor;
        if (commander == null || monitor == null) {
            Log("[CMD] Not connected to Run8 yet.", ConsoleColor.DarkYellow);
            continue;
        }

        if (CommandConsole.Execute(line, commander, monitor, Log)) {
            cts.Cancel();
            break;
        }
    }
});

bool reconnect = true;
while (reconnect && !cts.Token.IsCancellationRequested) {
    var monitor  = new PtcMonitor();
    var callback = new DispatcherCallback(Log, monitor);
    using var connector = new Run8Connector(host, port, callback, Log);
    RouteDiscoveryEngine? discovery = null;
    try {
        connector.Connect();

        var commander = new DispatcherCommander(connector.Channel!);
        activeCommander = commander;
        activeMonitor   = monitor;

        if (routeMap != null) {
            // onAdjacencyRecorded fires synchronously under the engine's own lock
            // (from RecordTransition), so the save must run there too rather than
            // on a background task — RouteMap isn't synchronized against its own
            // mutation, and a save running concurrently with the next
            // RecordAdjacency would race on the same Routes dictionary.
            discovery = new RouteDiscoveryEngine(routeMap, commander, log: Log,
                onAdjacencyRecorded: SaveRouteMap);
            callback.SetDiscoveryEngine(discovery);
            // Allow 5 s for the initial flood of UpdateTrainData callbacks before scouting.
            _ = Task.Run(async () => {
                try {
                    await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
                    discovery.Start();
                } catch (OperationCanceledException) { }
            });
        }

        await connector.WaitAsync(cts.Token);
    } catch (OperationCanceledException) {
        reconnect = false;
    } catch (Exception ex) {
        Log($"[ERR]  {ex.GetType().Name}: {ex.Message}", ConsoleColor.Red);
        if (!cts.Token.IsCancellationRequested) {
            Log("[CONN] Retrying in 10 seconds...", ConsoleColor.Yellow);
            try { await Task.Delay(TimeSpan.FromSeconds(10), cts.Token); }
            catch (OperationCanceledException) { reconnect = false; }
        }
    } finally {
        activeCommander = null;
        activeMonitor   = null;
        discovery?.Dispose();
    }
}

// Final save on clean exit. Edges are already saved incrementally as they're
// discovered (see onAdjacencyRecorded above), so this is mostly redundant —
// it's kept for a definitive "saved" log line and to cover the (persistence-
// only) case where routeMap was loaded but discovery never recorded an edge.
if (routeMap != null) {
    SaveRouteMap();
    Log($"[DISC] Route map saved → {routeMapPath}", ConsoleColor.Cyan);
}

Log("Done.", ConsoleColor.Gray);
fileWriter.Dispose();
