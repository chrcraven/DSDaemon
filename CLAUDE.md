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

**C# (.NET 6) is the only viable choice.** Run8's dispatcher interface is WCF over
`net.tcp` with binary message encoding. WCF is Microsoft/.NET-only; no Python, Go, or
Rust library exists for the duplex net.tcp wire protocol. CoreWCF/System.ServiceModel
client packages work cross-platform on .NET 6+ but Run8 itself is Windows-only, so the
app will always run on Windows alongside Run8.

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
    DSDaemon.csproj                 ← net6.0, System.ServiceModel.{Duplex,NetTcp} 4.10.*
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
# Prerequisites: .NET 6 SDK on Windows (Run8 machine)
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

  --host       Run8 host (default: localhost)
  --port       Run8 WCF port (default: 15192)
  --log-file   Append timestamped output to this file as well as console
  --discover   Enable empirical route discovery mode (issues BeginHoldAITrain commands)
  --route-map  JSON path to persist the growing route map (default: route-map.json)
```

Start Run8 first. Launch DSDaemon; it will reconnect automatically on channel fault.

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
instance — the discovery engine's scouting holds/releases and manual operator commands can
both be in flight; there's no coordination between them, so a manual `release` on a train
the discovery engine is currently holding as non-scout will interfere with scouting.

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

### Thread safety in RouteDiscoveryEngine

WCF callbacks arrive on concurrent threads (`ConcurrencyMode.Multiple`). The engine uses
a single `object _lock` to protect all state. The scout-timeout `Task.Delay` runs off the
lock and re-acquires it when it fires.

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
