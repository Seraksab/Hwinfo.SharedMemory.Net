# Hwinfo.SharedMemory.Net

[![Nuget](https://img.shields.io/nuget/v/Hwinfo.SharedMemory.Net?style=flat-square)](https://www.nuget.org/packages/Hwinfo.SharedMemory.Net)
![GitHub](https://img.shields.io/github/license/Seraksab/Hwinfo.SharedMemory.Net)

A small and simple library to read sensor values shared by [HWiNFO](https://www.hwinfo.com/) via shared memory.

## Requirements

- Windows, .NET 10 or later
- [HWiNFO](https://www.hwinfo.com/) running with **Shared Memory Support** enabled

Without that setting HWiNFO publishes no section at all and every read throws `FileNotFoundException`,
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

They return `false` for "nothing to read": no section, or one without a valid HWiNFO header - HWiNFO
isn't running, Shared Memory Support is off, there is no remote connection at that index, or the
instance is starting up or shutting down. Everything else still throws, see [Errors](#errors).

### What a read returns

`ReadLocal` and `ReadRemote` return a `SensorReadings`: the `Readings`, the `PollTime` HWiNFO produced
them at - so a caller polling faster than HWiNFO can tell new values from a repeat - and `Sensors`,
every sensor it published, including those with no readings of their own:

```csharp
foreach (var sensor in result.Sensors)
{
  Console.Out.WriteLine($"{sensor.NameUser} ({sensor.Id}/{sensor.Instance})");
}
```

The same `Sensor` instance is shared by all readings of that sensor and kept across reads for as long
as HWiNFO reports it unchanged, so reference equality is a valid way to group or key by sensor.

## Configuration

All of the reader's settings live on `SharedMemoryReaderOptions`, each of them optional:

| Option                | Default  | Meaning                                                                            |
|-----------------------|----------|------------------------------------------------------------------------------------|
| `MutexTimeout`        | 1 second | How long a read waits for HWiNFO's mutex; no effect when it can't be opened        |
| `StalenessTimeout`    | 1 minute | How long a section may go without an update before it is reopened; `Zero` to never |
| `RequireMutex`        | `false`  | Whether a read that can't obtain the mutex fails instead of going ahead            |
| `ReuseUnchangedPolls` | `false`  | Whether to hand out the previous result while HWiNFO's `PollTime` is unchanged     |

### Reusing unchanged polls

If you poll more often than HWiNFO updates, the reader can hand out the previous result instead of
reading again:

```csharp
using var reader = new SharedMemoryReader(new SharedMemoryReaderOptions { ReuseUnchangedPolls = true });
```

HWiNFO reports its poll time in whole seconds, so with a polling period below one second this can
serve values up to a second old - which an unchanged `PollTime` makes visible.

## Synchronization with HWiNFO

HWiNFO republishes its readings on every poll: it drops the reading count to zero and counts it back
up while it refills the section, a window of tens of microseconds every couple of seconds. Two things
keep a read out of it.

**HWiNFO's mutex** (`Global\HWiNFO_SM2_MUTEX`) is held for the duration of a read - but only where it
can be opened. HWiNFO commonly runs elevated and may create it with permissions a normal user process
doesn't have, while the shared memory itself still opens read-only, so a read that can't take it goes
ahead without it and `MutexTimeout` does nothing. Running with the same privileges as HWiNFO is what
makes it available; there is no API for telling which mode a read ran in. To refuse such a read
instead, set `RequireMutex`:

```csharp
using var reader = new SharedMemoryReader(new SharedMemoryReaderOptions { RequireMutex = true });
```

It then throws `InvalidOperationException` - but only once there is something to read, so a missing or
torn down section still reports itself as usual and a `TryRead*` polling loop is unaffected.

**A check after the fact** always runs: the reader copies the section, re-reads the header, and
discards the copy unless it still describes what was copied, retrying a few times with a backoff. That
catches a republish that started or finished during the copy, and is what makes a read without the
mutex safe in practice. Its blind spot is the poll time's one-second resolution: with HWiNFO polling
faster than that, a republish which *completes* between the two header reads leaves every compared
field back at its original value, so a result can mix readings from two consecutive polls. Each is
still a real reading; they just may not all come from the same poll.

## Lifetime and threading

- **Keep the reader and reuse it.** It caches the mapping and everything decoded from it, so a
  steady-state read allocates little more than the result. One reader per read throws that away.
- **A reader is safe to use from multiple threads.** Reads are serialized internally.
- **Dispose it when you're done.** It holds the open mapping until you do.

## Errors

- **`FileNotFoundException`** - HWiNFO isn't running, Shared Memory Support is off, or there is no
  remote connection at that index. The expected signal for "no data available", not a bug -
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
