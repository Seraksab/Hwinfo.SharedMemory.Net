# Hwinfo.SharedMemory.Net

[![Nuget](https://img.shields.io/nuget/v/Hwinfo.SharedMemory.Net?style=flat-square)](https://www.nuget.org/packages/Hwinfo.SharedMemory.Net)
![GitHub](https://img.shields.io/github/license/Seraksab/Hwinfo.SharedMemory.Net)

A small, fast library for reading the sensor values [HWiNFO](https://www.hwinfo.com/) publishes through
its shared memory interface.

## Requirements

- Windows, .NET 10 or later
- [HWiNFO](https://www.hwinfo.com/) running with **Shared Memory Support** enabled

Without that setting enabled HWiNFO publishes nothing at all and every read will throw a `FileNotFoundException`,
see [Errors](#errors).

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

Use `ReadRemote(index)` to read a remote HWiNFO instance instead of the local one, `index` being the
connection index starting at 0.

Three rules cover the reader's lifetime:

- **Keep it and reuse it.** It caches the mapping and everything decoded from it, so a steady-state
  read allocates little more than the result itself. A reader per read throws all of that away.
- **Use it from as many threads as you like.** Reads are serialized internally.
- **Dispose it when you're done.** It holds the mapping open until you do.

### What a read returns

`ReadLocal` and `ReadRemote` return a `SensorReadings`, which carries three things: the `Readings`
themselves, the `PollTime` HWiNFO produced them at - so a caller polling faster than HWiNFO can tell
new values from a repeat - and `Sensors`, every sensor of the section, including those with no
readings of their own:

```csharp
foreach (var sensor in result.Sensors)
{
  Console.Out.WriteLine($"{sensor.NameUser} ({sensor.Id}/{sensor.Instance})");
}
```

All readings of a sensor share one `Sensor` instance, and it is kept across reads for as long as
HWiNFO reports that sensor unchanged, so reference equality is a valid way to group or key by sensor.

### Reading without exceptions

For a polling loop, "HWiNFO isn't running" is a normal condition rather than an error, so
`TryReadLocal` and `TryReadRemote` report it as `false` instead of throwing:

```csharp
if (reader.TryReadLocal(out var result))
{
  // ...
}
```

They return `false` for exactly one thing: nothing to read. Either there is no section, or there is
one without a valid HWiNFO header, Shared Memory Support is off, there is no remote connection at that index, 
or the instance is starting up or shutting down. Everything else still throws, see [Errors](#errors).

## Configuration

The reader's settings live on `SharedMemoryReaderOptions`, each of them optional:

| Option                | Default  | Meaning                                                                           |
|-----------------------|----------|-----------------------------------------------------------------------------------|
| `MutexTimeout`        | 1 second | How long a read waits for HWiNFO's mutex; no effect when it can't be opened        |
| `StalenessTimeout`    | 1 minute | How long a section may go without an update before it is reopened; `Zero` to never |
| `RequireMutex`        | `false`  | Whether a read that can't obtain the mutex fails instead of going ahead            |
| `ReuseUnchangedPolls` | `false`  | Whether to hand out the previous result while HWiNFO's `PollTime` is unchanged     |

### Reusing unchanged polls

If you poll more often than HWiNFO updates, the reader can hand out the previous result instead of
reading the section again:

```csharp
using var reader = new SharedMemoryReader(new SharedMemoryReaderOptions { ReuseUnchangedPolls = true });
```

HWiNFO reports its poll time in whole seconds, so with a polling period below one second this can
serve values up to a second old - which an unchanged `PollTime` makes visible.

## Synchronization with HWiNFO

HWiNFO republishes its readings on every poll: it drops the reading count to zero and counts it back
up while it refills the section. Two mechanisms keep a read out of that window:

**HWiNFO's mutex** (`Global\HWiNFO_SM2_MUTEX`) is held for the duration of a read, but only where it
can be opened. HWiNFO commonly runs elevated and may create the mutex with permissions a normal user
process doesn't have, while the shared memory itself still opens read-only.\
A read that can't take the mutex therefore goes ahead without it, and `MutexTimeout` does nothing.\
To refuse an unsynchronized read outright, set `RequireMutex`:

```csharp
using var reader = new SharedMemoryReader(new SharedMemoryReaderOptions { RequireMutex = true });
```

Such a read then throws `InvalidOperationException` - but only once there is something to read, so a
missing or torn down section still reports itself as usual and a `TryRead*` polling loop is
unaffected.

**A check after the fact** always runs, mutex or not: the reader copies the section, re-reads the
header, and discards the copy unless it still describes what was copied, retrying a few times with a
backoff. That catches a republish which started or finished during the copy, and is what makes a read
without the mutex safe in practice.

## Errors

- **`FileNotFoundException`** - HWiNFO isn't running, Shared Memory Support is off, or there is no
  remote connection at that index. This is the expected signal for "no data available" -
  `TryReadLocal`/`TryReadRemote` report it as `false` instead.
- **`TimeoutException`** - the mutex was not acquired within `MutexTimeout`. Only thrown when the
  mutex exists and this process may open it.
- **`InvalidDataException`** - the section could not be parsed: bad signature, unsupported version, or
  a section that doesn't fit the mapping.
- **`UnauthorizedAccessException`** - the section exists but this process may not open it.
- **`InvalidOperationException`** - `RequireMutex` is set and the mutex could not be obtained.
- **`ObjectDisposedException`** - the reader has been disposed.

`ReadRemote` and `TryReadRemote` additionally throw `ArgumentOutOfRangeException` for a negative index,
and the constructor throws it for a negative or oversized timeout.

## Benchmark

Reading 470 readings of 25 sensors. `ReadSharedMemoryReusingPolls` is the same read with
`ReuseUnchangedPolls` enabled, i.e. the poll time hasn't moved and the previous result is handed out:

| Method                       |         Mean | Ratio | Allocated |
|------------------------------|-------------:|------:|----------:|
| ReadSharedMemory             | 36,801.16 ns | 1.000 |   33864 B |
| ReadSharedMemoryReusingPolls |     55.70 ns | 0.002 |         - |

Run on Windows 11 Pro, .NET 10.0.111, AMD Ryzen 9 7900X, DDR5-6200 CL30.

## License

See [LICENSE](LICENSE)
