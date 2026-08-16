using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Hwinfo.SharedMemory;

/// <summary>
/// Reads the sensor values shared by HWiNFO from shared memory.
/// </summary>
public class SharedMemoryReader : IDisposable
{
  private const string HWiNfoSensorsSm2Mutex = "Global\\HWiNFO_SM2_MUTEX";
  private const string HWiNfoSensorsMapFileNameLocal = "Global\\HWiNFO_SENS_SM2";
  private const string HWiNfoSensorsMapFileNameRemote = "Global\\HWiNFO_SENS_SM2_REMOTE_";

  // Layout version of the shared memory.
  // Newer versions only append fields (the offsets and element sizes are read from the header).
  // Anything below this is rejected, anything above is accepted.
  private const uint HWiNfoSensorsMinVersion = 2;

  // How often a read is retried when HWiNFO republishes the section underneath it, and how long the
  // first retry backs off before it does (doubling with every further attempt)
  private const int MaxReadAttempts = 5;
  private const int ReadRetrySpins = 1000;

  private readonly TimeSpan _mutexTimeout;
  private readonly TimeSpan _stalenessTimeout;
  private readonly bool _reuseUnchangedPolls;
  private readonly Lock _lock = new();
  private readonly Dictionary<string, MappedSection> _cache = new();

  // The mutex is advisory:
  // HWiNFO runs elevated and its mutex may be inaccessible to a normal user process, while the shared memory itself
  // still opens read-only. It is therefore opened lazily and best-effort, and reads proceed without it if it can't be
  // obtained.
  private Mutex? _mutex;
  private bool _mutexAccessDenied;
  private bool _disposed;

  /// <summary>
  /// Creates a new SharedMemoryReader
  /// </summary>
  /// <param name="options">
  /// The settings to use, or <c>null</c> for the defaults of <see cref="SharedMemoryReaderOptions"/>
  /// </param>
  /// <exception cref="ArgumentOutOfRangeException">A timeout in the options is negative or too large.</exception>
  public SharedMemoryReader(SharedMemoryReaderOptions? options = null)
  {
    options ??= new SharedMemoryReaderOptions();
    options.Validate();

    _mutexTimeout = options.MutexTimeout;
    _stalenessTimeout = options.StalenessTimeout;
    _reuseUnchangedPolls = options.ReuseUnchangedPolls;
  }

  /// <summary>
  /// Reads the sensor values of the local HWiNFO instance
  /// </summary>
  /// <returns>The sensor values</returns>
  /// <exception cref="FileNotFoundException">The shared memory file does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">Access is invalid for the shared memory file.</exception>
  /// <exception cref="InvalidDataException">Failure to parse data read from the shared memory file.</exception>
  /// <exception cref="TimeoutException">The mutex could not be acquired within the configured timeout.</exception>
  /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
  public IReadOnlyList<SensorReading> ReadLocal()
  {
    return ReadMemoryMappedFile(HWiNfoSensorsMapFileNameLocal);
  }

  /// <summary>
  /// Reads the sensor values of the remote HWiNFO instance with the given connection index
  /// </summary>
  /// <param name="index">The connection index starting with 0></param>
  /// <returns>The sensor values</returns>
  /// <exception cref="ArgumentOutOfRangeException">The index is negative.</exception>
  /// <exception cref="FileNotFoundException">The shared memory file does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">Access is invalid for the shared memory file.</exception>
  /// <exception cref="InvalidDataException">Failure to parse data read from the shared memory file.</exception>
  /// <exception cref="TimeoutException">The mutex could not be acquired within the configured timeout.</exception>
  /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
  public IReadOnlyList<SensorReading> ReadRemote(int index = 0)
  {
    if (index < 0) throw new ArgumentOutOfRangeException(nameof(index), "Must be greater than or equal to 0");
    return ReadMemoryMappedFile($"{HWiNfoSensorsMapFileNameRemote}{index}");
  }

  /// <inheritdoc />
  public void Dispose()
  {
    // Taking the lock keeps a concurrent read from having its section disposed underneath it
    lock (_lock)
    {
      if (_disposed) return;
      _disposed = true;

      _mutex?.Dispose();
      _mutex = null;
      foreach (var section in _cache.Values)
      {
        section.Dispose();
      }

      _cache.Clear();
    }

    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// Reads the sensor values from the memory mapped file with the given name.
  /// Internal so tests can read from a snapshot instead of a live HWiNFO instance.
  /// </summary>
  internal IReadOnlyList<SensorReading> ReadMemoryMappedFile(string fileName)
  {
    // The cross-process mutex may be unavailable, so callers within this process are serialized
    // separately to keep the cache and the reads consistent
    lock (_lock)
    {
      ObjectDisposedException.ThrowIf(_disposed, this);

      var mutex = AcquireMutex();
      var mutexAcquired = mutex != null && WaitForMutex(mutex);
      if (mutex != null && !mutexAcquired)
      {
        throw new TimeoutException(
          $"Timed out after {_mutexTimeout} waiting for the mutex '{HWiNfoSensorsSm2Mutex}'."
        );
      }

      try
      {
        var section = GetOrOpenSection(fileName);
        if (!section.TryReadHeader(out var header) || IsStale(header))
        {
          // The section we're mapped to has been torn down or is no longer being updated, e.g.
          // because HWiNFO was restarted. Release it - holding on to it would keep serving stale
          // data and could keep HWiNFO from creating a new section of a different size under the
          // same name. The reopened section is used as-is, even if it still looks stale.
          RemoveFromCache(fileName);
          section = GetOrOpenSection(fileName);
          if (!section.TryReadHeader(out header))
          {
            throw new InvalidDataException($"'{fileName}' does not contain a valid HWiNFO header.");
          }
        }

        for (var attempt = 0;; attempt++)
        {
          ValidateVersion(header, fileName);
          if (section.TryRead(header, out var readings)) return readings;

          if (attempt + 1 >= MaxReadAttempts)
          {
            throw new InvalidDataException(
              $"'{fileName}' was updated by HWiNFO during each of {MaxReadAttempts} read attempts."
            );
          }

          // HWiNFO is republishing the section. Retrying straight away tends to land in the same
          // window: the update reports a smaller element count while it counts back up, so the retry
          // reads less and finishes faster than the update it is racing. Backing off first - the
          // window is tens of microseconds - lets it finish.
          Thread.SpinWait(ReadRetrySpins << attempt);

          if (!section.TryReadHeader(out header))
          {
            throw new InvalidDataException($"'{fileName}' does not contain a valid HWiNFO header.");
          }
        }
      }
      finally
      {
        if (mutexAcquired) mutex?.ReleaseMutex();
      }
    }
  }

  /// <summary>
  /// Returns the cached section for the given file, opening and caching it if necessary.
  /// </summary>
  private MappedSection GetOrOpenSection(string fileName)
  {
    if (_cache.TryGetValue(fileName, out var cached)) return cached;

    var section = new MappedSection(fileName, _reuseUnchangedPolls);
    _cache[fileName] = section;
    return section;
  }

  /// <summary>
  /// Removes the cached section for the given file name and releases its handles.
  /// </summary>
  private void RemoveFromCache(string fileName)
  {
    if (_cache.Remove(fileName, out var cached))
    {
      cached.Dispose();
    }
  }

  /// <summary>
  /// Validates the version of the shared memory header to ensure compatibility with the expected minimum version.
  /// </summary>
  /// <exception cref="InvalidDataException">
  /// Thrown when the shared memory version in the header is lower than the supported minimum version.
  /// </exception>
  private static void ValidateVersion(in SmSensorsSharedMem2 header, string fileName)
  {
    if (header.Version < HWiNfoSensorsMinVersion)
    {
      throw new InvalidDataException(
        $"'{fileName}' has the unsupported shared memory version {header.Version}, " +
        $"expected {HWiNfoSensorsMinVersion} or higher."
      );
    }
  }

  /// <summary>
  /// Returns whether the last poll of HWiNFO is longer ago than the configured staleness timeout,
  /// which indicates that the mapped section is an orphan that HWiNFO no longer updates.
  /// </summary>
  private bool IsStale(SmSensorsSharedMem2 sharedMem)
  {
    if (_stalenessTimeout <= TimeSpan.Zero) return false;

    // PollTime is the unix time in seconds of the last update
    var ageInSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - sharedMem.PollTime;
    return TimeSpan.FromSeconds(ageInSeconds) > _stalenessTimeout;
  }

  /// <summary>
  /// Waits for the mutex and returns whether it was acquired within the configured timeout.
  /// </summary>
  private bool WaitForMutex(Mutex mutex)
  {
    try
    {
      return mutex.WaitOne(_mutexTimeout);
    }
    catch (AbandonedMutexException)
    {
      // The previous owner (e.g. a crashed HWiNFO) didn't release the mutex. The wait still
      // succeeded, and we now own it -> continue
      return true;
    }
  }

  /// <summary>
  /// Tries to open the HWiNFO mutex, requesting only the rights needed to synchronize with it.
  /// Returns null if it doesn't exist (yet) or if access to it is denied.
  /// </summary>
  private Mutex? AcquireMutex()
  {
    if (_mutex != null || _mutexAccessDenied) return _mutex;

    try
    {
      Mutex.TryOpenExisting(HWiNfoSensorsSm2Mutex, out _mutex);
    }
    catch (UnauthorizedAccessException)
    {
      // HWiNFO created the mutex with a DACL that doesn't grant us access; read without it
      _mutexAccessDenied = true;
    }

    return _mutex;
  }
}