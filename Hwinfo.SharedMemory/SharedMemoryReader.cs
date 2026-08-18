using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Hwinfo.SharedMemory;

/// <summary>
/// Reads the sensor values shared by HWiNFO from shared memory.
/// <para>
/// A read is kept out of the window in which HWiNFO republishes its readings by two mechanisms.
/// HWiNFO's named mutex is held for the duration of a read, but only where it can be opened: HWiNFO
/// commonly runs elevated and may create it with permissions a normal user process doesn't have,
/// while the shared memory itself still opens read-only, so a read that can't take it proceeds
/// without it and <see cref="SharedMemoryReaderOptions.MutexTimeout"/> then does nothing. Set
/// <see cref="SharedMemoryReaderOptions.RequireMutex"/> to fail such a read instead.
/// </para>
/// <para>
/// The second mechanism always runs: the section is copied, the header is read again, and the copy is
/// discarded and retried unless the header still describes it. Its one blind spot is HWiNFO's poll
/// time, which has a resolution of one second - so with a polling period below that and without the
/// mutex, a result can mix the readings of two consecutive polls.
/// </para>
/// </summary>
public sealed class SharedMemoryReader : IDisposable
{
  private const string HWiNfoSensorsSm2Mutex = "Global\\HWiNFO_SM2_MUTEX";
  private const string HWiNfoSensorsMapFileNameLocal = "Global\\HWiNFO_SENS_SM2";
  private const string HWiNfoSensorsMapFileNameRemote = "Global\\HWiNFO_SENS_SM2_REMOTE_";

  // How often a read is retried when HWiNFO republishes the section underneath it, and how long the
  // first retry backs off before it does (doubling with every further attempt)
  private const int MaxReadAttempts = 5;
  private const int ReadRetrySpins = 1000;

  private readonly TimeSpan _mutexTimeout;
  private readonly TimeSpan _stalenessTimeout;
  private readonly bool _requireMutex;
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
    _requireMutex = options.RequireMutex;
    _reuseUnchangedPolls = options.ReuseUnchangedPolls;
  }

  /// <summary>
  /// Reads the sensor values of the local HWiNFO instance
  /// </summary>
  /// <returns>The sensor values and the time HWiNFO last polled them</returns>
  /// <exception cref="FileNotFoundException">The shared memory file does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">Access is invalid for the shared memory file.</exception>
  /// <exception cref="InvalidDataException">Failure to parse data read from the shared memory file.</exception>
  /// <exception cref="TimeoutException">The mutex could not be acquired within the configured timeout.</exception>
  /// <exception cref="InvalidOperationException">
  /// The mutex could not be obtained and <see cref="SharedMemoryReaderOptions.RequireMutex"/> is set.
  /// </exception>
  /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
  public SensorReadings ReadLocal()
  {
    return ReadMemoryMappedFile(HWiNfoSensorsMapFileNameLocal);
  }

  /// <summary>
  /// Tries to read the sensor values of the local HWiNFO instance, without throwing if HWiNFO isn't
  /// publishing them.
  /// </summary>
  /// <param name="readings">
  /// The sensor values and the time HWiNFO last polled them. Empty if there are none, so it is safe
  /// to iterate even when this returns <c>false</c>.
  /// </param>
  /// <returns>
  /// <c>false</c> if the shared memory section doesn't exist or doesn't carry a valid HWiNFO header,
  /// i.e. HWiNFO isn't running, has Shared Memory Support turned off, or is currently starting up or
  /// shutting down. 
  /// </returns>
  /// <exception cref="UnauthorizedAccessException">Access is invalid for the shared memory file.</exception>
  /// <exception cref="InvalidDataException">Failure to parse data read from the shared memory file.</exception>
  /// <exception cref="TimeoutException">The mutex could not be acquired within the configured timeout.</exception>
  /// <exception cref="InvalidOperationException">
  /// The mutex could not be obtained and <see cref="SharedMemoryReaderOptions.RequireMutex"/> is set.
  /// </exception>
  /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
  public bool TryReadLocal(out SensorReadings readings)
  {
    return TryReadMemoryMappedFile(HWiNfoSensorsMapFileNameLocal, out readings);
  }

  /// <summary>
  /// Reads the sensor values of the remote HWiNFO instance with the given connection index
  /// </summary>
  /// <param name="index">The connection index starting with 0></param>
  /// <returns>The sensor values and the time HWiNFO last polled them</returns>
  /// <exception cref="ArgumentOutOfRangeException">The index is negative.</exception>
  /// <exception cref="FileNotFoundException">The shared memory file does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">Access is invalid for the shared memory file.</exception>
  /// <exception cref="InvalidDataException">Failure to parse data read from the shared memory file.</exception>
  /// <exception cref="TimeoutException">The mutex could not be acquired within the configured timeout.</exception>
  /// <exception cref="InvalidOperationException">
  /// The mutex could not be obtained and <see cref="SharedMemoryReaderOptions.RequireMutex"/> is set.
  /// </exception>
  /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
  public SensorReadings ReadRemote(int index = 0)
  {
    if (index < 0) throw new ArgumentOutOfRangeException(nameof(index), "Must be greater than or equal to 0");
    return ReadMemoryMappedFile($"{HWiNfoSensorsMapFileNameRemote}{index}");
  }

  /// <summary>
  /// Tries to read the sensor values of the remote HWiNFO instance with the given connection index,
  /// without throwing if there is no such connection.
  /// </summary>
  /// <param name="index">The connection index starting with 0</param>
  /// <param name="readings">
  /// The sensor values and the time HWiNFO last polled them. Empty if there are none, so it is safe
  /// to iterate even when this returns <c>false</c>.
  /// </param>
  /// <returns>
  /// <c>false</c> if the shared memory section doesn't exist or doesn't carry a valid HWiNFO header,
  /// i.e. there is no remote connection at that index, or the instance behind it is currently
  /// starting up or shutting down.
  /// </returns>
  /// <exception cref="ArgumentOutOfRangeException">The index is negative.</exception>
  /// <exception cref="UnauthorizedAccessException">Access is invalid for the shared memory file.</exception>
  /// <exception cref="InvalidDataException">Failure to parse data read from the shared memory file.</exception>
  /// <exception cref="TimeoutException">The mutex could not be acquired within the configured timeout.</exception>
  /// <exception cref="InvalidOperationException">
  /// The mutex could not be obtained and <see cref="SharedMemoryReaderOptions.RequireMutex"/> is set.
  /// </exception>
  /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
  public bool TryReadRemote(int index, out SensorReadings readings)
  {
    if (index < 0) throw new ArgumentOutOfRangeException(nameof(index), "Must be greater than or equal to 0");
    return TryReadMemoryMappedFile($"{HWiNfoSensorsMapFileNameRemote}{index}", out readings);
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
  }

  /// <summary>
  /// Reads the sensor values from the memory mapped file with the given name.
  /// Internal so tests can read from a snapshot instead of a live HWiNFO instance.
  /// </summary>
  internal SensorReadings ReadMemoryMappedFile(string fileName)
  {
    ReadMemoryMappedFile(fileName, throwIfUnavailable: true, out var readings);
    return readings;
  }

  /// <summary>
  /// Tries to read the sensor values from the memory mapped file with the given name.
  /// Internal so tests can read from a snapshot instead of a live HWiNFO instance.
  /// </summary>
  internal bool TryReadMemoryMappedFile(string fileName, out SensorReadings readings)
  {
    return ReadMemoryMappedFile(fileName, throwIfUnavailable: false, out readings);
  }

  /// <summary>
  /// Reads the sensor values from the memory mapped file with the given name.
  /// </summary>
  /// <param name="fileName">The name of the memory mapped file to read.</param>
  /// <param name="throwIfUnavailable">
  /// Whether a missing section or one without a valid HWiNFO header throws, rather than returning
  /// <c>false</c>. Everything else throws either way.
  /// </param>
  /// <param name="readings">The sensor values that were read, or <c>default</c> if there were none.</param>
  private bool ReadMemoryMappedFile(string fileName, bool throwIfUnavailable, out SensorReadings readings)
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
        readings = SensorReadings.None;

        var section = GetOrOpenSection(fileName, throwIfUnavailable);
        if (section == null) return false;

        var headerValid = section.TryReadHeader(out var header);
        if (!headerValid || ShouldReplaceStale(section, header))
        {
          // The section we're mapped to has been torn down or is no longer being updated, e.g.
          // because HWiNFO was restarted. Release it - holding on to it would keep serving stale
          // data and could keep HWiNFO from creating a new section of a different size under the
          // same name. The reopened section is used as-is, even if it still looks stale.
          RemoveFromCache(fileName);
          section = GetOrOpenSection(fileName, throwIfUnavailable);
          if (section == null) return false;

          // Remember that this one already is the replacement for a stale section, so that a section
          // nothing refreshes isn't reopened again on the very next read. Only staleness is held off
          // like this: a torn down section is one HWiNFO is expected to publish again shortly.
          if (headerValid) section.StaleReopenedAt = Environment.TickCount64;

          if (!section.TryReadHeader(out header))
          {
            if (throwIfUnavailable)
            {
              throw new InvalidDataException($"'{fileName}' does not contain a valid HWiNFO header.");
            }

            return false;
          }
        }

        // There is a section worth reading, so this is where refusing to read it unsynchronized
        // belongs. Anything above reports itself first: "HWiNFO isn't publishing" is the section's
        // answer to give, and a polling loop on TryRead expects it as false rather than as a throw.
        if (_requireMutex && !mutexAcquired) throw MutexUnavailable();

        for (var attempt = 0;; attempt++)
        {
          if (section.TryRead(header, out readings)) return true;

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
            // The section was torn down underneath the retry, so there is nothing left to read
            if (throwIfUnavailable)
            {
              throw new InvalidDataException($"'{fileName}' does not contain a valid HWiNFO header.");
            }

            return false;
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
  /// Returns <c>null</c> if the file doesn't exist and <paramref name="throwIfMissing"/> is <c>false</c>.
  /// </summary>
  /// <exception cref="FileNotFoundException">
  /// The shared memory file does not exist and <paramref name="throwIfMissing"/> is <c>true</c>.
  /// </exception>
  private MappedSection? GetOrOpenSection(string fileName, bool throwIfMissing)
  {
    if (_cache.TryGetValue(fileName, out var cached)) return cached;

    MappedSection section;
    try
    {
      section = new MappedSection(fileName, _reuseUnchangedPolls);
    }
    catch (FileNotFoundException) when (!throwIfMissing)
    {
      // There is no such section, i.e. HWiNFO isn't publishing one. OpenExisting has no Try variant,
      // so this is the only way to tell without the caller having to catch it themselves.
      return null;
    }

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
  /// Builds the failure for a read that may not go ahead without HWiNFO's mutex.
  /// </summary>
  private InvalidOperationException MutexUnavailable()
  {
    var reason = _mutexAccessDenied ? "this process may not open it" : "it does not exist";
    return new InvalidOperationException(
      $"The mutex '{HWiNfoSensorsSm2Mutex}' cannot be used to synchronize with HWiNFO because " +
      $"{reason}, and {nameof(SharedMemoryReaderOptions)}.{nameof(SharedMemoryReaderOptions.RequireMutex)} " +
      "is set. HWiNFO may be running with higher privileges than this process; run with the same ones, " +
      "or turn the option off to read without the mutex."
    );
  }

  /// <summary>
  /// Returns whether a stale looking section is worth releasing and opening again.
  /// <para>
  /// A section that is <em>still</em> stale after it was already reopened is one nothing is going to
  /// refresh - an orphan another process keeps alive, or an HWiNFO whose monitoring is paused.
  /// Reopening it again would throw away the mapping and every string decoded from it on every
  /// single read, for nothing, so it is retried only once per staleness timeout. A section that was
  /// never reopened is always retried, which is what keeps an HWiNFO restart detected on the first
  /// read after it goes stale.
  /// </para>
  /// </summary>
  private bool ShouldReplaceStale(MappedSection section, in SmSensorsSharedMem2 header)
  {
    if (!IsStale(header)) return false;

    var reopenedAt = section.StaleReopenedAt;
    return reopenedAt == null
           || Environment.TickCount64 - reopenedAt.Value >= _stalenessTimeout.TotalMilliseconds;
  }

  /// <summary>
  /// Returns whether the last poll of HWiNFO is longer ago than the configured staleness timeout,
  /// which indicates that the mapped section is an orphan that HWiNFO no longer updates.
  /// </summary>
  private bool IsStale(SmSensorsSharedMem2 sharedMem)
  {
    if (_stalenessTimeout <= TimeSpan.Zero) return false;

    // PollTime is the unix time in seconds of the last update. The subtraction is done in double
    // because a garbage poll time can be any long, which would overflow both long and TimeSpan.
    var ageInSeconds = (double)DateTimeOffset.UtcNow.ToUnixTimeSeconds() - sharedMem.PollTime;
    return ageInSeconds > _stalenessTimeout.TotalSeconds;
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
  /// Tries to open the HWiNFO mutex. Returns null if it doesn't exist (yet) or if access to it is denied.
  /// </summary>
  private Mutex? AcquireMutex()
  {
    // The denial is latched so a reader that can't have the mutex doesn't ask for it on every read.
    // When it is required that trade is wrong: the reader would keep failing over a condition an
    // HWiNFO restart may well have cleared, so in that mode it is asked for again every time.
    if (_mutex != null || (_mutexAccessDenied && !_requireMutex)) return _mutex;

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