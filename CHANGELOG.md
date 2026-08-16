# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 4.0.0 - Unreleased

### Added

- `SharedMemoryReaderOptions.ReuseUnchangedPolls`: an opt-in setting that hands out the previous result
  while HWiNFO's poll time is unchanged, which makes reads more frequent than HWiNFO's polling period
  practically free

### Changed

- The HWiNFO mutex is now treated as advisory: it's opened lazily with `Mutex.TryOpenExisting`, which
  asks only for the rights needed to wait on and release it rather than for full control, and reads
  proceed without it if it's unavailable
- `ReadLocal` and `ReadRemote` now return a `SensorReadings`, which pairs the `IReadOnlyList<SensorReading>`
  with the `PollTime` at which HWiNFO produced them and the `Sensors` it published.
- The sensor a reading belongs to moved out of `SensorReading` into a `Sensor` record. One `Sensor`
  instance is now shared by all of its readings rather than being copied.
- Reads are now about 2.6x faster and allocate about 7x less: the benchmark of 470 readings went from
  99.7 µs and 237 KB to 38.2 µs and 33 KB.
- The `SensorType` members lost their `SensorType` prefix
- A reading type HWiNFO reports that isn't one of the known nine is mapped to `SensorType.Other`
- The constructor takes a single `SharedMemoryReaderOptions` instead of three optional parameters.

### Fixed

- The mutex timeout is now honoured: `ReadLocal`/`ReadRemote` throw `TimeoutException` instead of
  reading potentially torn data
- An abandoned mutex (e.g. after an HWiNFO crash) no longer breaks the reader permanently
- The header signature is validated on every read. A cached memory mapped file whose section was torn
  down (e.g. after an HWiNFO restart) is now released and reopened instead of serving stale data.
- The shared memory version is validated and `InvalidDataException` is thrown for versions below 2
- A shared memory file whose last poll is older than the new `StalenessTimeout` is reopened,
  which detects an orphaned section that still carries a valid signature
- A reading referring to a sensor index outside the sensor array now throws `InvalidDataException`
- `Dispose` is idempotent and no longer disposes the memory mapped files underneath a concurrent read
- A read that lands in the window in which HWiNFO republishes its readings (it drops the element count 
  to 0 and counts it back up) is now detected and retried instead of returning a truncated set of readings
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