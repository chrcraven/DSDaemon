# DSDaemon — Project Context

## What this is

DSDaemon is a **read-only external dispatcher** for the **Run8 Train Simulator**.
It connects to Run8 over its built-in WCF (Windows Communication Foundation) interface,
receives all pushed state (signals, occupied blocks/switches, reversed switches, train data,
sim clock) and displays it to the console with colour-coded output and optional file logging.

**V1 scope: visualization only — no commands sent to Run8 except in discovery mode.**
**V1.5 scope: empirical route discovery via controlled AI-train scouting.**
**V2 scope: the dispatcher may issue live commands — signals, switches, and AI
train orders — via an interactive console, alongside continued route discovery.**
Scope: Run8 Southern California region (Mojave Sub + Needles Sub).

## Technology decision (settled — do not reopen)

**C# (.NET 10) is the only viable choice.** Run8's dispatcher interface is WCF over
`net.tcp` with binary message encoding. WCF is Microsoft/.NET-only; no Python, Go, or
Rust library exists for the duplex net.tcp wire protocol. CoreWCF/System.ServiceModel
client packages work cross-platform on .NET 6+ but Run8 itself is Windows-only, so the
app will always run on Windows alongside Run8. (Targets net10.0 — the current LTS —
rather than the original net6.0, which is now out of support.)

## WCF connection details (from iecc8 source)

| Item | Value |
|------|-------|
| Protocol | `net.tcp` |
| Default port | **15192** |
| Service path | `/Run8` |
| Full URI | `net.tcp://localhost:15192/Run8` |
| Security mode | `SecurityMode.None` |
| Channel pattern | `DuplexChannelFactory<IRun8>` with `InstanceContext` wrapping `IDispatcher` callback |
| Session registration | Call `BeginDispatcherConnected` after opening the channel |

## Reference: iecc8

The iecc8 project (`github.com/Hawk777/iecc8`) is a UK-style signalling UI that implements
the same WCF contract. It is GPL-3.0 C# (WPF/.NET Framework). DSDaemon does **not** depend
on it — we've re-declared all contracts independently — but it is the authoritative reference
for the wire protocol.

Key files in iecc8 to consult:
- `iecc8/IDispatcher.cs` — callback interface (13 one-way operations)
- `iecc8/IRun8.cs` — service contract (`[ServiceContract(Name="IWCFRun8")]`, APM Begin/End pattern)
- `iecc8/Messages/` — all DataContract message structs
- `iecc8/UI/TopLevel/WelcomeWindowViewModel.cs` — exact binding/factory setup (port 15192)

## Project structure

```
DSDaemon/
  DSDaemon.sln
  CLAUDE.md                         ← you are here
  src/DSDaemon/
    DSDaemon.csproj                 ← net10.0, System.ServiceModel.{Duplex,NetTcp} 4.10.*
    Program.cs                      ← entry point, arg parsing, reconnect loop, command console, discovery startup
    Run8Connector.cs                ← WCF channel lifecycle; .Channel exposes IRun8 for commands
    DispatcherCallback.cs           ← IDispatcher impl — logs callbacks; forwards to discovery engine
    PtcMonitor.cs                   ← thread-safe PTC state (signals, blocks, switches, trains)
    DispatcherCommander.cs          ← ITrainCommander/IDispatcherCommander — wraps all outbound IRun8 commands
    CommandConsole.cs               ← parses operator stdin lines, dispatches to IDispatcherCommander
    Contracts/
      IDispatcher.cs                ← callback interface (must match Run8 wire contract)
      IRun8.cs                      ← service interface ([ServiceContract(Name="IWCFRun8")])
    Messages/
      TrainData.cs                  ← !! DataMember names use k__BackingField convention !!
      TrainDataMessage.cs
      SimulationStateMessage.cs
      SignalsMessage.cs
      OccupiedBlocksMessage.cs
      OccupiedSwitchesMessage.cs
      ReversedSwitchesMessage.cs
      UnlockedSwitchesMessage.cs
      InterlockErrorSwitchesMessage.cs
      DispatcherPermissionMessage.cs
      DTMFMessage.cs
      RadioTextMessage.cs
      ESignalIndication.cs
      EDispatcherPermission.cs
      EEngineerType.cs
      EDTMFType.cs
      ESwitchState.cs
      AIRecrewTrainMessage.cs       ← dispatcher→Run8 (needed to compile IRun8)
      DispatcherSignalMessage.cs
      DispatcherSwitchMessage.cs
      HoldAITrainMessage.cs         ← { TrainID, HoldTrain: bool } — toggle hold on/off
      RelinquishAITrainMessage.cs
      StopAITrainMessage.cs
      TransportPlayerMessage.cs
      TransportPlayerToBlockMessage.cs
    Discovery/
      RouteMap.cs                   ← adjacency graph (block↔block edges with confidence counts)
      RouteDiscoveryEngine.cs       ← state machine: Initializing→WaitingToSelect→Released→…
      StaticImport/
        TrackDatabaseFile.cs               ← parses Run8's TrackDatabase.r8 (section connectivity)
        BlockDetectorDatabaseFile.cs       ← parses BlockDetectorDatabase.r8 (BlockID → section grouping)
        DispatcherSwitchIconDatabaseFile.cs ← parses DispatcherSwitchIconDatabase.r8 (SwitchID + Route ID)
        RouteNameLookup.cs                 ← hardcoded route-folder-name → Route ID fallback table
        StaticRouteMapImporter.cs          ← orchestrates the above into RouteMap adjacency (see below)
  tests/DSDaemon.Tests/
    Helpers/
      TrainBuilder.cs               ← builds TrainData for tests
      MessageBuilder.cs             ← builds signal/switch/block messages
      FakeRun8.cs                   ← records outbound commands for DispatcherCommander tests
    Scenarios/
      OverspeedTests.cs
      BlockOccupancyTests.cs
      SignalConflictTests.cs
      SwitchStateTests.cs
      PermissionTests.cs
      TrainDisplayTests.cs
      RouteDiscoveryTests.cs        ← discovery state machine + RouteMap unit tests
      DispatcherCommanderTests.cs   ← verifies each command sends the right WCF message
      CommandConsoleTests.cs        ← verifies operator command-line parsing/dispatch
      StaticRouteMapImportTests.cs  ← parser + importer tests against synthetic .r8 fixtures
  proto/
    dsdaemon.proto                  ← gRPC contract for the server scaffold (see below) — NOT wired up yet
  src/DSDaemon.Server/              ← scaffold: ASP.NET Core gRPC server, ungated/unauthenticated
    DSDaemon.Server.csproj
    Program.cs                     ← minimal Kestrel/gRPC host, HTTP/2 plaintext on :5270 (dev only)
    Services/
      SessionRegistryService.cs    ← OpenSession/CloseSession — issues the session_id
      DispatcherBridgeService.cs   ← the bidi Stream RPC; folds events into SessionState, drains its Outbound queue
    Sessions/
      SessionManager.cs            ← in-memory session_id → SessionState registry
      SessionState.cs              ← per-session live state; does NOT yet replicate PtcMonitor/RouteDiscoveryEngine
  src/DSDaemon.ServerClient/        ← scaffold: gRPC client wrapper, NOT referenced by src/DSDaemon yet
    DSDaemon.ServerClient.csproj
    BridgeClient.cs                ← OpenSession + Stream wrapper for a future local-agent integration
  tests/DSDaemon.Server.Tests/
    SessionManagerTests.cs         ← registry bookkeeping + per-session isolation
```

## Critical wire-format detail: TrainData backing fields

The original `TrainData` in iecc8 was compiled from C# **auto-properties**, so WCF
serialized the compiler-generated backing field names (e.g. `<TrainID>k__BackingField`).
Our `TrainData.cs` must use `[DataMember(Name = "<Field>k__BackingField")]` on every field
or deserialization will silently produce zero/null values.

Run8-to-dispatcher DataContract namespace:
`http://schemas.datacontract.org/2004/07/DispatcherComms.MessagesFromRun8`

Dispatcher-to-Run8 DataContract namespace:
`http://schemas.datacontract.org/2004/07/DispatcherComms.MessagesFromDispatcher`

## Build & run

```powershell
# Prerequisites: .NET 10 SDK on Windows (Run8 machine)
cd src/DSDaemon
dotnet restore
dotnet build -c Release
dotnet run -- --host localhost --port 15192 --log-file C:\logs\dsdaemon.log
```

Or publish a self-contained exe:
```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o publish/
```

## Usage

```
DSDaemon [--host <h>] [--port <p>] [--log-file <path>] [--discover] [--route-map <path>]
         [--routes-dir <path>]

  --host        Run8 host (default: localhost)
  --port        Run8 WCF port (default: 15192)
  --log-file    Override the log file path (default: dsdaemon-<timestamp>.log)
  --discover    Enable empirical route discovery mode (issues BeginHoldAITrain commands)
  --route-map   JSON path to persist the growing route map (default: route-map.json)
  --routes-dir  Root directory with one subfolder per installed Run8 route; parses each
                route's TrackDatabase.r8 + BlockDetectorDatabase.r8 into route-map.json as
                a static baseline at startup (see "Static route-map import" below)
```

Start Run8 first. Launch DSDaemon; it will reconnect automatically on channel fault.

Every run always writes a log file (mirroring everything shown on the console) — by
default a fresh timestamped `dsdaemon-<timestamp>.log` in the working directory, so
a run's full output is available afterward (e.g. to hand back for debugging) even if
nobody was watching the console live. `--log-file` overrides the path and appends to
it instead of starting fresh.

To avoid piling up one file per run forever, only the most recent 10 default-named
logs are kept — older ones are pruned (best-effort delete) at startup, before the new
one is created. Pruning only ever touches files matching the default `dsdaemon-*.log`
naming scheme; an explicit `--log-file` path is never pruned, since that's the
operator's own file to manage.

## Interactive dispatcher commands (v2)

Once connected, DSDaemon reads commands from stdin (concurrently with the live callback
display) and dispatches them through `DispatcherCommander` → `IRun8`:

```
signal <id> <stop|proceed|fleet|flagby> [auto]  Change a signal indication
switch <id> <normal|reverse|unlock|relock>      Throw/lock a switch
hold <trainId>                                  Hold an AI train
release <trainId>                               Release a held AI train
stop <trainId>                                  Stop an AI train immediately
recrew <trainId>                                Recrew an AI train
relinquish <trainId> [on|off]                   Relinquish AI control (default on)
trains                                          List known trains
help                                             Show this help
quit | exit                                     Shut down DSDaemon
```

Commands are parsed and dispatched by `CommandConsole.Execute`, which is independent of
the stdin loop so it can be unit tested without a real console (see `CommandConsoleTests`).
When `--discover` is also active, both features share the same `DispatcherCommander`
instance — the discovery engine's scouting holds/releases, its autonomous signal/switch
clearing (see below), and manual operator commands can all be in flight concurrently; there's
no coordination between them, so a manual `release` on a train the discovery engine is
currently holding as non-scout will interfere with scouting, and a manual `signal`/`switch`
command on the scout's route may race with the engine's own auto-clearing.

## Route discovery mode (v1.5)

### Why empirical discovery?

Run8 exposes only numeric block/switch/signal IDs over WCF. There is no parseable file
format containing connectivity data (iecc8's XAML schemas for Mojave/Needles are empty
stubs). The only way to learn the topology is to watch trains move.

### The controlled-scout algorithm

The problem with passive observation of multiple trains: if train A is in block 100 and
block 101 becomes occupied, you can't tell whether it was train A or train B that entered
101. With N free-running trains, ambiguity compounds.

**Solution: hold everything, release one at a time.**

1. On startup, `RouteDiscoveryEngine` holds every AI train it sees via `BeginHoldAITrain`.
2. After 5 s (for the initial flood of callbacks to settle), `Start()` is called.
3. Engine picks one held AI train as "scout", releases it (`HoldTrain=false`).
4. Engine watches `UpdateTrainData` for the scout's `BlockID` to change.
5. On block change: records `fromBlock → toBlock` adjacency edge in `RouteMap`, re-holds
   the scout, picks next scout, repeats.
6. If the scout doesn't move within 30 s (timeout), it is re-held and a different train
   is tried.

Because only one train moves at a time, every block-ID delta is unambiguously attributable
to the scout.

**Scout eligibility does not wait for Run8 to confirm the hold.** `TrainData.HoldingForDispatcher`
was originally used to gate scout selection (only pick a train Run8 has echoed back as held), but
a full live session showed Run8 never sets that flag true for an AI hold issued by the external
dispatcher — every `UpdateTrainData` for a held train kept reporting it `false` indefinitely, which
left the engine stuck in `WaitingToSelect` forever and no scout was ever released. The engine now
tracks "held" purely via its own `_heldByUs` bookkeeping (have we sent the `Hold` command?) and
does not read `HoldingForDispatcher` for scout-selection purposes at all. When only one AI train is
around, the timeout/re-hold/re-select cycle (step 5/6) immediately re-releases the same train each
time rather than leaving it held — see the round-robin note below. `HoldingForDispatcher` is still
displayed to the operator (`[HELD]` tag / `trains` command) for visibility, it just isn't trusted
for discovery's own state machine.

**Scout selection is round-robin, not first-eligible.** `TrySelectScout` first tries any eligible
train *other than* the one most recently scouted, falling back to re-selecting it only if it's the
only AI train currently held. Without this, a single fast-moving train would keep re-winning
selection over other already-held trains the instant it's re-held after each move, since (per the
point above) there's no confirmation delay to naturally give another train a turn.

### Autonomous path-clearing (aiding the scout)

While a scout is `Released`, `RouteDiscoveryEngine` also clears its own path with no operator
involvement, via the full `IDispatcherCommander` (not just `ITrainCommander`):

- **Signals**: on `SetSignals` for the scout's route, any signal reported `Stop` is immediately
  commanded to `Proceed` (`ChangeSignal(index, Proceed, automaticWorking: true)`). This relies
  on an **unverified assumption**: `SignalsMessage.Signals` carries no ID, only a per-route
  positional list, so the engine treats list index as `SignalID`. It's safe to get wrong (Run8
  presumably no-ops on an unrecognized ID) but may simply have no effect — watch for
  `[DISC-AUTO]` log lines and confirm the following `SetSignals` callback actually flips to
  `Proceed` before trusting this on a new region.
- **Switches**: on `SetInterlockErrorSwitches` for the scout's route, every switch ID reported
  is commanded `Unlock`. Unlike signals, these are genuine wire `SwitchID`s (the same IDs
  reported in `ReversedSwitches`/`UnlockedSwitches`), so this is unambiguous.

Both only ever touch the scout's own route, and each signal index / switch ID is only
commanded once per scouting run (tracked in `_clearedSignalIndices` / `_unlockedInterlockSwitches`,
reset whenever a new scout is selected) to avoid spamming Run8 on every repeated callback.

**Auto-clearing is seeded at scout selection, not just reactive.** `SetSignals`/
`SetInterlockErrorSwitches` are pushed by Run8 only on change. If a signal was already `Stop`
(or a switch already interlock-errored) on a route *before* its scout was released, no new
callback would ever arrive to trigger the reactive clearing above — the scout would then sit
at red until the 30 s scout timeout re-holds it. To fix this, the engine caches the latest
`SignalsMessage`/`InterlockErrorSwitchesMessage` per route (`_lastSignalsByRoute` /
`_lastInterlockErrorsByRoute`, updated on every callback regardless of discovery state) and,
at the moment a new scout is released in `TrySelectScoutFrom`, immediately re-applies the
clear from that cached state for the scout's route — in addition to the ongoing reactive
clearing on subsequent live callbacks.

This is judged safe *only* because discovery holds every other train — with just the scout
moving on its route, forcing every signal on that route clear cannot create a real conflicting
movement authority. This autonomous clearing must not be reused outside discovery mode (e.g.
for general PTC enforcement) without re-deriving that safety argument.

### RouteMap format (route-map.json)

```json
{
  "Routes": {
    "0": {
      "Name": "",
      "Blocks": {
        "100": {
          "Name": "Mojave Yard East",
          "AdjacentBlocks": { "101": 14, "99": 12 }
        }
      }
    }
  }
}
```

`AdjacentBlocks` maps neighbour block ID → number of times that transition was observed.
High confidence (≥5) means the edge is reliable. Low confidence (1–2) may be spurious.

**Persistence is incremental, not just on clean exit.** `RouteDiscoveryEngine` takes an
optional `onAdjacencyRecorded` callback, invoked synchronously (still under the engine's own
lock) every time `RecordTransition` records a new edge. `Program.cs` wires this to save
`route-map.json` on every discovered edge, so a crash or `kill` mid-session doesn't lose
accumulated discovery — the previous behaviour only wrote the file once, after the reconnect
loop exited on Ctrl+C. The save must run under the engine's lock (not dispatched to a
background task) because `RouteMap.Routes` isn't synchronized against its own mutation — a
save racing a concurrent `RecordAdjacency` on another WCF callback thread would be an
unguarded concurrent read/write of the same dictionary. The final save at clean shutdown is
kept too, mostly for a definitive "saved" log line.

### `TrainData.BlockID` vs `OccupiedBlocksMessage.OccupiedBlocks` numbering

Observed on a live session: `SetOccupiedBlocks` reports raw, per-route local block numbers
(e.g. `[231]` for route 110), but `TrainData.BlockID` for a train sitting in that same block
came across as a **composite** `route*1000 + localBlock` value (`110231`). The two are not the
same numbering domain. `RouteDiscoveryEngine` derives the scout's route (used to match
`SetSignals`/`SetInterlockErrorSwitches` callbacks for auto-clearing, and to key `RouteMap`
adjacency) by first trying an exact `_blockRoute` lookup (populated from `SetOccupiedBlocks`,
for routes that turn out to use matching raw numbering) and falling back to `BlockID / 1000`
otherwise. This is inferred from a single route's data — reconfirm the composite-encoding
assumption if adjacency/auto-clearing looks wrong on a route with block numbers ≥ 1000 or a
route ID that doesn't cleanly divide out.

### Thread safety in RouteDiscoveryEngine

WCF callbacks arrive on concurrent threads (`ConcurrencyMode.Multiple`). The engine uses
a single `object _lock` to protect all state. The scout-timeout `Task.Delay` runs off the
lock and re-acquires it when it fires.

### Static route-map import (from installed Run8 game files)

Empirical scouting exists because Run8 exposes no parseable connectivity data over the
WCF interface itself. But Run8's own installed game files *do* contain the full track
topology — reverse-engineered by the third-party, GPL-unrelated project
[Puyodead1/Run8-V3-reverse-engineering](https://github.com/Puyodead1/Run8-V3-reverse-engineering)
(unaffiliated with Run8; format docs under `docs/*.md`, not vendored into this repo).
`StaticRouteMapImporter` (`Discovery/StaticImport/`) parses these files directly into
`RouteMap` adjacency at startup via `--routes-dir`, giving discovery a complete baseline
instead of starting from zero blocks.

**Files parsed, and what each is validated to mean** (validated byte-exact against real
BNSF_NeedlesSub game files supplied during development — every byte of every sample file
was consumed with no leftover and no implausible field value):

- **`TrackDatabase.r8`** — per-route track section geometry and connectivity
  (`Next Section Indices`). The published format doc's `TrackNode` offset table is wrong —
  it implies a 75-byte node, but the real node is **79 bytes** (`Tangent Degrees` is a full
  `Vector3`, not the truncated field the doc's offsets imply). Found by brute-forcing node
  width until the entire file parsed to an exact byte count; `TrackDatabaseFile.cs` skips
  node geometry entirely and keeps only `Section Index` + `Next Section Indices` +
  `IsCtcSwitch`, since that's all adjacency derivation needs.
- **`BlockDetectorDatabase.r8`** — the bridge between `TrackDatabase.r8`'s section-ID space
  and the live dispatcher `BlockID` space. `BlockDetector.Index` is the local part of the
  live `BlockID` (see the composite-encoding note above); `TrackIndices[]` are
  `TrackDatabase.r8` `Section Index` values belonging to that block. Confirmed by resolving
  every `TrackIndices` reference in the sample against real `TrackDatabase.r8` sections:
  100% resolved (3,796/3,796).
- **`DispatcherSwitchIconDatabase.r8`** — `RoutePrefix` is the numeric Route ID (matches
  the Route Name→ID table in the reverse-engineering repo's `GeneralInfo.md`, e.g. `110` for
  `BNSF_NeedlesSub`) and `Index` is the local part of the live wire `SwitchID`. Confirmed
  directly: every entry in the Needles Sub sample carried `RoutePrefix == 110`, and the
  sample's 66 switch icons matched `TrackDatabase.r8`'s 66 `IsCtcSwitch`-flagged sections.
  The format doc's `(String) Image Name` field (present when `Version == 2`, the only
  version observed) is **not** the nibble-swapped `R8String` encoding used elsewhere in
  Run8's formats — it's .NET's native `BinaryWriter`/`BinaryReader` string encoding (7-bit
  length prefix + UTF-8), so plain `BinaryReader.ReadString()` decodes it correctly.
- **`DispatcherBlockLightDatabase.r8` / `DispatcherSignalControllerDatabase.r8`** — parsed
  and validated byte-exact during investigation (dispatcher-panel layout data), but **not**
  wired into the importer — nothing in `RouteMap` currently consumes signal-panel data, and
  parsing them didn't independently confirm `RouteDiscoveryEngine`'s existing "SignalsMessage
  list-position = SignalID" assumption (a live-wire question these static files don't touch).

**How adjacency is derived** (`StaticRouteMapImporter.DeriveEdges`): group every
`TrackDatabase.r8` section by its owning `BlockDetectorDatabase.r8` block (`Index`), then
walk each section's `Next Section Indices` — two blocks are adjacent if any section in one
connects directly to any section in the other. Sections not covered by any block detector
(e.g. ones belonging to a neighbouring route not present under `--routes-dir` — a real
Needles Sub sample had ~2,000 `Next Section Indices` pointing outside its own 4,176
sections, presumably into an adjoining sub's file) simply can't contribute an edge and are
silently skipped, rather than guessed at.

**Route ID resolution**: prefers the `RoutePrefix` baked into that route's own
`DispatcherSwitchIconDatabase.r8` (comes from the install itself); falls back to matching
the route's folder name against `RouteNameLookup.cs` (hardcoded from `GeneralInfo.md`) only
when that file is missing/empty (a route with no CTC switches) or inconsistent. A route
folder whose ID can't be resolved either way is skipped with a warning — never guessed.

**Directory layout is caller-provided, not auto-detected.** `--routes-dir` must point at a
directory containing one subfolder per route, each holding that route's own
`TrackDatabase.r8` + `BlockDetectorDatabase.r8` (+ optional switch-icon file) directly. This
was a deliberate choice: Run8's actual on-disk install layout (e.g. whether routes live
under a `Content/Routes/<RouteName>/` path) was never verified against a real installation,
so the importer doesn't guess at or hardcode that structure — point `--routes-dir` at
whatever directory already holds the per-route subfolders.

**Confidence semantics**: each derived edge calls `RouteMap.RecordAdjacency` exactly once,
identical to a single empirical scout observation — it is **not** pre-boosted to the "≥5 =
high confidence" threshold described above, even though it comes from authoritative game
data rather than one ambiguous sighting. A statically-derived edge with a single parallel
track between two blocks will show confidence 1 until either more static edges land on it
(e.g. double track) or live discovery reinforces it. Revisit this if the low starting
confidence proves misleading in practice.

**Runs once at startup, independent of `--discover`.** Static import happens before any WCF
connection is attempted and works even without `--discover` (useful for just producing/
refreshing `route-map.json` from installed files without running Run8 at all). When both
flags are given, import seeds `route-map.json` first and `RouteDiscoveryEngine` then keeps
running normally against the live game — it reinforces or extends the static baseline, it
doesn't get skipped or replaced by it.

## Design philosophy: Positive Train Control (PTC)

DSDaemon is built around **PTC** as its core safety and dispatch model:

- **Movement authority**: a train may only occupy blocks for which it has been granted
  authority (signal clearance + no conflicting occupancy). Track block occupancy from
  `SetOccupiedBlocks` and signal states from `SetSignals` together determine whether
  each train is inside its granted movement authority.

- **Speed enforcement**: every `UpdateTrainData` callback carries `TrainSpeedMph` and
  `TrainSpeedLimitMPH`. The v1 display already flags any train exceeding its limit
  with a red `!! PTC OVERSPEED !!` tag. V2 will track a rolling speed history.

- **Switch alignment**: reversed or unlocked switches (`SetReversedSwitches`,
  `SetUnlockedSwitches`, `SetInterlockErrorSwitches`) are PTC-relevant — a train
  must not enter a block with an misaligned switch in its path. Interlock errors
  are always highlighted red.

- **Authority conflict detection (v2)**: correlate `OccupiedBlocks` with the signal
  states for the same route; if a block is occupied and the approach signal is
  `Proceed` or `Fleet`, that is a potential authority conflict worth flagging.

These evaluations remain display-only — DSDaemon does not yet automatically issue
`BeginHoldAITrain`/`BeginStopAITrain`/`BeginChangeSignal`/`BeginThrowSwitch` in response
to a detected conflict. V2 makes the full command surface available to a human dispatcher
via the interactive console (see above); automatic PTC enforcement is still a planned
next step.

## Planned next steps (v2+)

- TUI layout (Spectre.Console or similar) with live panels for each data type
- Sub-area filtering for Mojave Sub vs Needles Sub (Route IDs)
- File logging structured as newline-delimited JSON for downstream tooling
- SQLite state store so the display survives reconnects without flicker
- Automatic PTC enforcement: issue `BeginHoldAITrain`/`BeginStopAITrain`/
  `BeginChangeSignal` in response to a detected authority conflict, rather than only
  flagging it

## Server scaffold (exploratory — not wired into the app yet)

`proto/dsdaemon.proto`, `src/DSDaemon.Server/`, and `src/DSDaemon.ServerClient/` are the
first pieces of a bigger architectural direction: turning the local agent into a thin
proxy for the WCF connection, and moving PTC evaluation, route discovery, and a shared,
persisted route-topology store to a server that multiple agents (multiple Run8 world
instances, potentially over an untrusted network) can connect to. This unlocks a real UI
built off the server's data, and lets discovered routes accumulate across sessions
instead of every install re-scouting the same static Mojave/Needles topology.

**Current state — scaffold only:**
- `src/DSDaemon` (the local agent) is **unchanged** and still does everything itself
  (PtcMonitor, RouteDiscoveryEngine, DispatcherCommander all run in-process against the
  local WCF channel, same as today). It does not talk to `DSDaemon.Server`.
- `DSDaemon.Server` is a working ASP.NET Core gRPC host with the session handshake
  (`SessionRegistry.OpenSession`/`CloseSession`) and the bidirectional event/command
  stream (`DispatcherBridge.Stream`) wired up, but `SessionState.Apply` only records the
  latest train/signal/occupied-blocks event per session — none of PtcMonitor's
  evaluation logic or RouteDiscoveryEngine's scouting state machine has moved here yet.
- `DSDaemon.ServerClient`'s `BridgeClient` is a usable wrapper around the generated gRPC
  client, but nothing in `src/DSDaemon/Program.cs` calls it.
- Auth is a placeholder: `SessionRegistryService.OpenSession` accepts any `agent_token`
  without validating it. Don't expose this beyond localhost as-is.
- Kestrel listens on plaintext HTTP/2 (`:5270`, no TLS) — fine for local dev, but the
  whole point of this design is agents connecting over an untrusted network, so TLS
  (and real per-agent credentials) are required before that's safe.

**Session isolation is the load-bearing design decision** (see the header comment in
`proto/dsdaemon.proto`): live state — trains, block occupancy, signals, discovery
scouting — is keyed by `session_id` and must never merge across sessions, since two
different Run8 world instances can both have a "train #5" and treating them as the same
train would corrupt PTC evaluation. Only the *discovered route topology* (not modeled in
this scaffold yet — see the SQLite item above) is meant to be shared and merged globally
across every session, since the physical Mojave/Needles map itself is the same for
everyone. Any code added to `SessionState`/`SessionManager` should preserve that split.

**Proto naming gotcha worth knowing if you extend `dsdaemon.proto`:** several messages
originally had a field named the same as their own message type (e.g. `repeated int32
occupied_blocks` inside `message OccupiedBlocks`) — protoc's C# generator resolves that
collision by renaming the field's C# property to `Foo_` with a trailing underscore,
which is confusing to read later. All such fields were renamed instead (`blocks`,
`switches`, `indications`, etc.) — keep that pattern when adding new messages.
