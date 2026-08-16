# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 4.0.0 - 2026-08-16

### Added

- `reuseUnchangedPolls`: an opt-in constructor flag that hands out the previous result while
  HWiNFO's poll time is unchanged, which makes reads more frequent than HWiNFO's polling period
  practically free (56 ns and no allocation). Off by default, because HWiNFO reports its poll time
  in whole seconds and a polling period below one second would make it serve values up to a second
  old.

### Changed

- The HWiNFO mutex is now treated as advisory: it's opened lazily, requesting only synchronization
  rights, and reads proceed without it if it's unavailable. Previously the constructor requested full
  access and threw `UnauthorizedAccessException` in any non-elevated process while HWiNFO was running.
- `ReadLocal` and `ReadRemote` return `IReadOnlyList<SensorReading>` instead of `IEnumerable<SensorReading>`,
  so callers no longer need a `ToList()` to get a count or an indexer.
- The sensor a reading belongs to moved out of `SensorReading` into a `Sensor` record. One `Sensor`
  instance is now shared by all of its readings rather than being copied.
- Reads are now about 2.7x faster and allocate about 7x less: the benchmark of 470 readings went from
  99.7 µs and 237 KB (which included the `ToList()` it needed) to 36.3 µs and 33 KB.
- The `SensorType` members lost their `SensorType` prefix
- A reading type HWiNFO reports that isn't one of the known nine is mapped to `SensorType.Other`
- The constructor takes a single `SharedMemoryReaderOptions` instead of three optional parameters.

### Fixed

- The mutex timeout is now honoured: `ReadLocal`/`ReadRemote` throw `TimeoutException` instead of
  reading potentially torn data. Reads within a process are additionally serialized by an internal
  lock, so the cached memory mapped files stay consistent even without the mutex.
- An abandoned mutex (e.g. after an HWiNFO crash) no longer breaks the reader permanently
- The header signature is validated on every read. A cached memory mapped file whose section was torn
  down (e.g. after an HWiNFO restart) is now released and reopened instead of serving stale data.
- The shared memory version is validated and `InvalidDataException` is thrown for versions below 2
- A shared memory file whose last poll is older than the new `stalenessTimeout` is reopened,
  which detects an orphaned section that still carries a valid signature
- A reading referring to a sensor index outside the sensor array now throws the documented
  `InvalidDataException` instead of `IndexOutOfRangeException`
- `Dispose` is idempotent and no longer disposes the memory mapped files underneath a concurrent read.
- A read that lands in the window in which HWiNFO republishes its readings (it drops the element
  count to 0 and counts it back up) is now detected and retried instead of returning a truncated
  set of readings. `InvalidDataException` is thrown if it doesn't succeed within five attempts.
- The sensor and reading sections are checked against the size of the mapping, and their element
  size against the minimum the layout needs, before anything is read from them

## 3.0.0 - 2026-02-09

### Changed

- Renamed some SensorReading properties for better clarity
- Cache memory mapped file to improve performance
- Updated to target framework net10.0

## 2.1.0 - 2024-10-07

### Added

- Added net8.0 target framework

### Changed

- Updated dependencies
- Minor refactoring

## 2.0.0 - 2023-11-19

### Added

- Support for remote HWiNFO instances
- Performance improvements
- Configurable mutex timeout

### Changed

- FileNotFoundException is no longer caught

## 1.0.0 - 2023-09-22

First public release