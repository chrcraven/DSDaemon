using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DSDaemon;
using DSDaemon.Discovery;

// ── Parse arguments ──────────────────────────────────────────────────────────
string  host         = "localhost";
int     port         = 15192;
string  logPath      = $"dsdaemon-{DateTime.Now:yyyyMMdd-HHmmss}.log";
bool    logPathSet   = false;
bool    discoverMode = false;
string  routeMapPath = "route-map.json";

for (int i = 0; i < args.Length; i++) {
    switch (args[i]) {
        case "--host"      when i + 1 < args.Length: host         = args[++i]; break;
        case "--port"      when i + 1 < args.Length: port         = int.Parse(args[++i]); break;
        case "--log-file"  when i + 1 < args.Length: logPath      = args[++i]; logPathSet = true; break;
        case "--route-map" when i + 1 < args.Length: routeMapPath = args[++i]; break;
        case "--discover":  discoverMode = true; break;
        case "-h":
        case "--help":
            Console.WriteLine("Usage: DSDaemon [--host <h>] [--port <p>] [--log-file <path>]");
            Console.WriteLine("                [--discover] [--route-map <path>]");
            Console.WriteLine("  --host       Run8 host (default: localhost)");
            Console.WriteLine($"  --port       Run8 WCF port (default: {port})");
            Console.WriteLine("  --log-file   Append all output to this file as well");
            Console.WriteLine($"               (default: dsdaemon-<timestamp>.log — always written, so a");
            Console.WriteLine( "               run's output can be handed back for debugging)");
            Console.WriteLine("  --discover   Enable empirical route discovery mode");
            Console.WriteLine("  --route-map  Route map JSON path (default: route-map.json)");
            return;
    }
}

// ── Logging ───────────────────────────────────────────────────────────────────
// Always writes to a file — by default a fresh timestamped one per run — so the
// full session output is available to hand back for debugging even when nobody
// was watching the console live.
var logDir = Path.GetDirectoryName(logPath);
if (!string.IsNullOrEmpty(logDir)) Directory.CreateDirectory(logDir);
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
if (discoverMode)          Log($"Discovery ON  route-map: {routeMapPath}", ConsoleColor.Cyan);
Log("Press Ctrl+C to exit. Type 'help' for dispatcher commands.", ConsoleColor.Gray);
Log(new string('─', 80), ConsoleColor.DarkGray);

// Load route map once; persist across reconnects.
RouteMap? routeMap = discoverMode ? RouteMap.LoadOrCreate(routeMapPath) : null;

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
            discovery = new RouteDiscoveryEngine(routeMap, commander, log: Log);
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

// Save route map on clean exit.
if (routeMap != null) {
    routeMap.Save(routeMapPath);
    Log($"[DISC] Route map saved → {routeMapPath}", ConsoleColor.Cyan);
}

Log("Done.", ConsoleColor.Gray);
fileWriter.Dispose();
