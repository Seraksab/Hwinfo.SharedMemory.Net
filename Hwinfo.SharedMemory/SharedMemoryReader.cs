using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
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

  // "HWiS" in little-endian byte order.
  // HWiNFO overwrites it (e.g. with 0xDEADBEEF) while the section is being torn down
  private const uint HWiNfoSensorsSignature = 0x53695748;

  // Layout version of the shared memory.
  // Newer versions only append fields (the offsets and element sizes are read from the header).
  // Anything below this is rejected, anything above is accepted.
  private const uint HWiNfoSensorsMinVersion = 2;

  private readonly int _mutexTimeout;
  private readonly int _stalenessTimeout;
  private readonly Lock _lock = new();
  private readonly Dictionary<string, (MemoryMappedFile Mmf, MemoryMappedViewAccessor Accessor)> _cache = new();

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
  /// <param name="mutexTimeout">The number of milliseconds to wait for the mutex, or Infinite (-1) to wait indefinitely</param>
  /// <param name="stalenessTimeout">
  /// The number of milliseconds after which a shared memory file that hasn't been updated by HWiNFO is
  /// considered stale and is reopened, or 0 to never consider it stale. Should be well above the polling
  /// period configured in HWiNFO.
  /// </param>
  public SharedMemoryReader(int mutexTimeout = 1000, int stalenessTimeout = 60000)
  {
    _mutexTimeout = mutexTimeout;
    _stalenessTimeout = stalenessTimeout;
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
  public IEnumerable<SensorReading> ReadLocal()
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
  public IEnumerable<SensorReading> ReadRemote(int index = 0)
  {
    if (index < 0) throw new ArgumentOutOfRangeException(nameof(index), "Must be greater than or equal to 0");
    return ReadMemoryMappedFile($"{HWiNfoSensorsMapFileNameRemote}{index}");
  }

  /// <inheritdoc />
  public void Dispose()
  {
    // Taking the lock keeps a concurrent read from having its accessors disposed underneath it
    lock (_lock)
    {
      if (_disposed) return;
      _disposed = true;

      _mutex?.Dispose();
      _mutex = null;
      foreach (var value in _cache.Values)
      {
        value.Accessor.Dispose();
        value.Mmf.Dispose();
      }

      _cache.Clear();
    }

    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// Reads the sensor values from the memory mapped file with the given name.
  /// Internal so tests can read from a snapshot instead of a live HWiNFO instance.
  /// </summary>
  internal SensorReading[] ReadMemoryMappedFile(string fileName)
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
          $"Timed out after {_mutexTimeout} ms waiting for the mutex '{HWiNfoSensorsSm2Mutex}'."
        );
      }

      try
      {
        var accessor = GetOrOpenAccessor(fileName);
        if (!TryReadHeader(accessor, out var sharedMem) || IsStale(sharedMem))
        {
          // The section we're mapped to has been torn down or is no longer being updated, e.g.
          // because HWiNFO was restarted. Release it - holding on to it would keep serving stale
          // data and could keep HWiNFO from creating a new section of a different size under the
          // same name. The reopened section is used as-is, even if it still looks stale.
          RemoveFromCache(fileName);
          accessor = GetOrOpenAccessor(fileName);
          if (!TryReadHeader(accessor, out sharedMem))
          {
            throw new InvalidDataException($"'{fileName}' does not contain a valid HWiNFO header.");
          }
        }

        if (sharedMem.Version < HWiNfoSensorsMinVersion)
        {
          throw new InvalidDataException(
            $"'{fileName}' has the unsupported shared memory version {sharedMem.Version}, " +
            $"expected {HWiNfoSensorsMinVersion} or higher."
          );
        }

        return ReadSensorReadings(accessor, sharedMem);
      }
      finally
      {
        if (mutexAcquired) mutex?.ReleaseMutex();
      }
    }
  }

  /// <summary>
  /// Returns the cached view accessor for the given file, opening and caching it if necessary.
  /// </summary>
  private MemoryMappedViewAccessor GetOrOpenAccessor(string fileName)
  {
    if (_cache.TryGetValue(fileName, out var cached)) return cached.Accessor;

    var mmf = MemoryMappedFile.OpenExisting(fileName, MemoryMappedFileRights.Read);
    var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
    _cache[fileName] = (mmf, accessor);
    return accessor;
  }

  /// <summary>
  /// Removes the cached memory mapped file for the given file name and releases its handles.
  /// </summary>
  private void RemoveFromCache(string fileName)
  {
    if (!_cache.Remove(fileName, out var cached)) return;

    cached.Accessor.Dispose();
    cached.Mmf.Dispose();
  }

  /// <summary>
  /// Reads the header and returns whether it carries a valid HWiNFO signature.
  /// </summary>
  private static bool TryReadHeader(MemoryMappedViewAccessor accessor, out SmSensorsSharedMem2 sharedMem)
  {
    accessor.Read(0, out sharedMem);
    return sharedMem.Signature == HWiNfoSensorsSignature;
  }

  /// <summary>
  /// Returns whether the last poll of HWiNFO is longer ago than the configured staleness timeout,
  /// which indicates that the mapped section is an orphan that HWiNFO no longer updates.
  /// </summary>
  private bool IsStale(SmSensorsSharedMem2 sharedMem)
  {
    if (_stalenessTimeout <= 0) return false;

    // PollTime is the unix time in seconds of the last update
    var ageInSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - sharedMem.PollTime;
    return ageInSeconds * 1000d > _stalenessTimeout;
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

  private static SensorReading[] ReadSensorReadings(MemoryMappedViewAccessor accessor, SmSensorsSharedMem2 sharedMem)
  {
    // Read sensor data and reading data
    var sensors = ReadSensorData(accessor, sharedMem);
    var readings = ReadReadingData(accessor, sharedMem);

    // Convert to SensorReading 
    return ConvertToSensorReading(readings, sensors);
  }

  private static SmSensorsSensorElement[] ReadSensorData(
    MemoryMappedViewAccessor accessor,
    SmSensorsSharedMem2 sharedMem
  )
  {
    return ReadStructs<SmSensorsSensorElement>(
      accessor,
      sharedMem.SensorSection_Offset,
      sharedMem.SensorSection_NumElements,
      (int)sharedMem.SensorSection_SizeOfElement
    );
  }

  private static SmSensorsReadingElement[] ReadReadingData(
    MemoryMappedViewAccessor accessor,
    SmSensorsSharedMem2 sharedMem
  )
  {
    return ReadStructs<SmSensorsReadingElement>(
      accessor,
      sharedMem.ReadingSection_Offset,
      sharedMem.ReadingElements_NumElements,
      (int)sharedMem.ReadingSection_SizeOfElement
    );
  }

  private static SensorReading[] ConvertToSensorReading(
    SmSensorsReadingElement[] readings,
    SmSensorsSensorElement[] sensors
  )
  {
    var sensorReadings = new SensorReading[readings.Length];
    for (var idx = 0; idx < readings.Length; idx++)
    {
      ref readonly var reading = ref readings[idx];
      if (reading.SensorIndex >= (uint)sensors.Length)
      {
        throw new InvalidDataException(
          $"Reading {idx} refers to sensor {reading.SensorIndex}, but only {sensors.Length} sensors were read."
        );
      }

      ref readonly var sensor = ref sensors[(int)reading.SensorIndex];
      sensorReadings[idx] = new SensorReading(
        ReadingId: reading.ReadingId,
        SensorIndex: reading.SensorIndex,
        ReadingType: reading.Type,
        LabelOrig: reading.LabelOrig,
        LabelUser: reading.LabelUser,
        Unit: reading.Unit,
        Value: reading.Value,
        ValueMin: reading.ValueMin,
        ValueMax: reading.ValueMax,
        ValueAvg: reading.ValueAvg,
        SensorId: sensor.SensorId,
        SensorInstance: sensor.SensorInst,
        SensorNameOrig: sensor.NameOrig,
        SensorNameUser: sensor.NameUser
      );
    }

    return sensorReadings;
  }

  private static T[] ReadStructs<T>(MemoryMappedViewAccessor accessor, long offset, long numElements, int elementSize)
    where T : struct
  {
    var results = new T[numElements];
    var byteBuffer = new byte[elementSize];
    var handle = GCHandle.Alloc(byteBuffer, GCHandleType.Pinned);
    try
    {
      var ptr = handle.AddrOfPinnedObject();
      for (var idx = 0; idx < numElements; idx++)
      {
        accessor.ReadArray(offset + (idx * (long)elementSize), byteBuffer, 0, elementSize);
        results[idx] = (T)(
          Marshal.PtrToStructure(ptr, typeof(T))
          ?? throw new InvalidDataException("Failed to convert bytes to struct.")
        );
      }
    }
    finally
    {
      handle.Free();
    }

    return results;
  }
}