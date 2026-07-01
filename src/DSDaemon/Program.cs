using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DSDaemon;
using DSDaemon.Discovery;

// ── Parse arguments ──────────────────────────────────────────────────────────
string  host         = "localhost";
int     port         = 15192;
string? logPath      = null;
bool    discoverMode = false;
string  routeMapPath = "route-map.json";

for (int i = 0; i < args.Length; i++) {
    switch (args[i]) {
        case "--host"      when i + 1 < args.Length: host         = args[++i]; break;
        case "--port"      when i + 1 < args.Length: port         = int.Parse(args[++i]); break;
        case "--log-file"  when i + 1 < args.Length: logPath      = args[++i]; break;
        case "--route-map" when i + 1 < args.Length: routeMapPath = args[++i]; break;
        case "--discover":  discoverMode = true; break;
        case "-h":
        case "--help":
            Console.WriteLine("Usage: DSDaemon [--host <h>] [--port <p>] [--log-file <path>]");
            Console.WriteLine("                [--discover] [--route-map <path>]");
            Console.WriteLine("  --host       Run8 host (default: localhost)");
            Console.WriteLine($"  --port       Run8 WCF port (default: {port})");
            Console.WriteLine("  --log-file   Append all output to this file as well");
            Console.WriteLine("  --discover   Enable empirical route discovery mode");
            Console.WriteLine("  --route-map  Route map JSON path (default: route-map.json)");
            return;
    }
}

// ── Logging ───────────────────────────────────────────────────────────────────
StreamWriter? fileWriter = null;
if (logPath != null) {
    fileWriter = new StreamWriter(logPath, append: true, encoding: System.Text.Encoding.UTF8) {
        AutoFlush = true,
    };
}

var consoleLock = new object();

void Log(string message, ConsoleColor color = ConsoleColor.White) {
    var line = $"{DateTime.Now:HH:mm:ss.fff}  {message}";
    lock (consoleLock) {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(line);
        Console.ForegroundColor = prev;
    }
    fileWriter?.WriteLine(line);
}

// ── Banner ────────────────────────────────────────────────────────────────────
Log("DSDaemon — Run8 Southern California External Dispatcher (v1 console prototype)", ConsoleColor.Cyan);
Log($"Target: net.tcp://{host}:{port}/Run8", ConsoleColor.Gray);
if (logPath      != null) Log($"Logging to:   {logPath}",      ConsoleColor.Gray);
if (discoverMode)          Log($"Discovery ON  route-map: {routeMapPath}", ConsoleColor.Cyan);
Log("Press Ctrl+C to exit", ConsoleColor.Gray);
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

bool reconnect = true;
while (reconnect && !cts.Token.IsCancellationRequested) {
    var monitor  = new PtcMonitor();
    var callback = new DispatcherCallback(Log, monitor);
    using var connector = new Run8Connector(host, port, callback, Log);
    RouteDiscoveryEngine? discovery = null;
    try {
        connector.Connect();

        if (routeMap != null) {
            var commander = new TrainCommander(connector.Channel!);
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
        discovery?.Dispose();
    }
}

// Save route map on clean exit.
if (routeMap != null) {
    routeMap.Save(routeMapPath);
    Log($"[DISC] Route map saved → {routeMapPath}", ConsoleColor.Cyan);
}

fileWriter?.Dispose();
Log("Done.", ConsoleColor.Gray);
