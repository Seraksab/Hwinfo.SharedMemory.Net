# Hwinfo.SharedMemory.Net

[![Nuget](https://img.shields.io/nuget/v/Hwinfo.SharedMemory.Net?style=flat-square)](https://www.nuget.org/packages/Hwinfo.SharedMemory.Net)
![GitHub](https://img.shields.io/github/license/Seraksab/Hwinfo.SharedMemory.Net)

A small and simple library to read sensor values shared by [HWiNFO](https://www.hwinfo.com/) via shared memory.

## Requirements

- Windows, .NET 10 or later
- [HWiNFO](https://www.hwinfo.com/) running with **Shared Memory Support** enabled

Without that setting HWiNFO publishes no shared memory section at all, so every read throws
`FileNotFoundException` - see [Errors](#errors).

## Installation

```
dotnet add package Hwinfo.SharedMemory.Net
```

## Usage

```csharp
using var reader = new SharedMemoryReader();
var result = reader.ReadLocal();

Console.Out.WriteLine($"HWiNFO last polled at {result.PollTime:HH:mm:ss}");
foreach (var sensorReading in result.Readings)
{
  Console.Out.WriteLine($"{sensorReading.Sensor.NameUser}: {sensorReading.LabelUser} = " +
                        $"{sensorReading.Value} {sensorReading.Unit}");
}
```

`ReadRemote(index)` reads a remote HWiNFO instance instead of the local one, `index` being the
connection index starting at 0.

### Reading without exceptions

"HWiNFO isn't running" is a normal condition for a polling loop, not an error, so `TryReadLocal` and
`TryReadRemote` report it as `false` instead of throwing:

```csharp
if (reader.TryReadLocal(out var result))
{
  // ...
}
```

They return `false` if the shared memory section doesn't exist or doesn't carry a valid HWiNFO header,
i.e. HWiNFO isn't running, Shared Memory Support is off, there is no remote connection at that index,
or the instance is currently starting up or shutting down. Everything else - a section that can't be
parsed, a mutex that can't be acquired, a disposed reader - still throws, see [Errors](#errors).

### What a read returns

`ReadLocal` and `ReadRemote` return a `SensorReadings`, which carries the `Readings` together with the
`PollTime` at which HWiNFO produced them, so a caller polling faster than HWiNFO can tell new values
from a repeat of the previous ones.\
It also carries `Sensors`, every sensor HWiNFO published - including those that have no readings of
their own and therefore do not appear in any `SensorReading`:

```csharp
foreach (var sensor in result.Sensors)
{
  Console.Out.WriteLine($"{sensor.NameUser} ({sensor.Id}/{sensor.Instance})");
}
```

The same `Sensor` instance is shared by all readings of that sensor and is kept across reads for as
long as HWiNFO reports it unchanged, so reference equality is a valid way to group or key by sensor.

## Configuration

All of the reader's settings live on `SharedMemoryReaderOptions`, each of them optional:

| Option                | Default  | Meaning                                                                            |
|-----------------------|----------|------------------------------------------------------------------------------------|
| `MutexTimeout`        | 1 second | How long a read waits for HWiNFO's mutex before it throws a `TimeoutException`     |
| `StalenessTimeout`    | 1 minute | How long a section may go without an update before it is reopened; `Zero` to never |
| `ReuseUnchangedPolls` | `false`  | Whether to hand out the previous result while HWiNFO's `PollTime` is unchanged     |

### Reusing unchanged polls

If you poll more often than HWiNFO updates its shared memory, you can let the reader hand out the previous result
instead of reading again:

```csharp
using var reader = new SharedMemoryReader(new SharedMemoryReaderOptions { ReuseUnchangedPolls = true });
```

Note that HWiNFO reports its poll time in whole seconds, so with a polling period configured below one
second this can serve values that are up to a second old - which an unchanged `PollTime` makes visible.

## Lifetime and threading

- **Keep the reader and reuse it.** It caches the memory mapping and everything it decodes from it, so
  a steady-state read allocates little more than the result itself. Creating one per read throws that
  away and reopens the section every time.
- **A reader is safe to use from multiple threads.** Reads are serialized internally.
- **Dispose it when you're done.** `SharedMemoryReader` is `IDisposable` and holds the open mapping
  until it is disposed.

## Errors

- **`FileNotFoundException`** - HWiNFO isn't running, Shared Memory Support is off, or there is no
  remote connection at that index. This is the expected signal for "no data available", not a bug -
  `TryReadLocal`/`TryReadRemote` report it as `false` instead.
- **`TimeoutException`** - HWiNFO's mutex was not acquired within `MutexTimeout`.
- **`InvalidDataException`** - the section could not be parsed: bad signature, unsupported version, or
  a section that doesn't fit the mapping.
- **`UnauthorizedAccessException`** - the section exists but this process may not open it.
- **`ObjectDisposedException`** - the reader has been disposed.

`ReadRemote` and `TryReadRemote` additionally throw `ArgumentOutOfRangeException` for a negative index,
and the constructor throws it for a negative or oversized timeout.

## Benchmark

Reading 470 readings of 25 sensors.\
`ReadSharedMemoryReusingPolls` is the same read with `ReuseUnchangedPolls` enabled, i.e. the poll time 
hasn't moved and the previous result is handed out:

| Method                       |         Mean | Ratio | Allocated |
|------------------------------|-------------:|------:|----------:|
| ReadSharedMemory             | 36,801.16 ns | 1.000 |   33864 B |
| ReadSharedMemoryReusingPolls |     55.70 ns | 0.002 |         - |

Run on:

- Windows 11 Pro
- .NET 10.0.111
- CPU: AMD Ryzen 9 7900X
- RAM: DDR5-6200 CL30

## License

See [LICENSE](LICENSE)
