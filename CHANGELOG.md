# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 3.0.1 - 2026-08-15


### Changed 

- The HWiNFO mutex is now treated as advisory: it's opened lazily, requesting only synchronization
  rights, and reads proceed without it if it's unavailable. Previously the constructor requested full
  access and threw `UnauthorizedAccessException` in any non-elevated process while HWiNFO was running.

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