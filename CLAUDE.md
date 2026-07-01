# DSDaemon — Project Context

## What this is

DSDaemon is a **read-only external dispatcher** for the **Run8 Train Simulator**.
It connects to Run8 over its built-in WCF (Windows Communication Foundation) interface,
receives all pushed state (signals, occupied blocks/switches, reversed switches, train data,
sim clock) and displays it to the console with colour-coded output and optional file logging.

**V1 scope: visualization only — no commands sent to Run8.**
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
    Program.cs                      ← entry point, arg parsing, reconnect loop
    Run8Connector.cs                ← WCF channel lifecycle (connect, wait, dispose)
    DispatcherCallback.cs           ← IDispatcher impl — logs all callbacks to console
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
      AIRecrewTrainMessage.cs       ← dispatcher→Run8 (unused in v1, needed to compile IRun8)
      DispatcherSignalMessage.cs
      DispatcherSwitchMessage.cs
      HoldAITrainMessage.cs
      RelinquishAITrainMessage.cs
      StopAITrainMessage.cs
      TransportPlayerMessage.cs
      TransportPlayerToBlockMessage.cs
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
DSDaemon [--host <host>] [--port <port>] [--log-file <path>]

  --host       Run8 host (default: localhost)
  --port       Run8 WCF port (default: 15192)
  --log-file   Append timestamped output to this file as well as console
```

Start Run8 first. Launch DSDaemon; it will reconnect automatically on channel fault.

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

In v1 these are display-only observations. In v2+ the app can issue `BeginHoldAITrain`
or `BeginStopAITrain` to enforce PTC authority limits on AI trains.

## Planned next steps (v2+)

- TUI layout (Spectre.Console or similar) with live panels for each data type
- Sub-area filtering for Mojave Sub vs Needles Sub (Route IDs)
- File logging structured as newline-delimited JSON for downstream tooling
- SQLite state store so the display survives reconnects without flicker
- Optional outbound commands (signal/switch control, AI train orders)
