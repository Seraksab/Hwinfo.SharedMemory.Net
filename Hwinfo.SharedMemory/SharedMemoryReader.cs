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

  private readonly int _mutexTimeout;
  private readonly Dictionary<string, (MemoryMappedFile Mmf, MemoryMappedViewAccessor Accessor)> _cache = new();

  // The mutex is advisory: HWiNFO runs elevated and its mutex may be inaccessible to a normal user
  // process, while the shared memory itself still opens read-only. It is therefore opened lazily and
  // best-effort, and reads proceed without it if it can't be obtained.
  private Mutex? _mutex;
  private bool _mutexAccessDenied;

  /// <summary>
  /// Creates a new SharedMemoryReader
  /// </summary>
  /// <param name="mutexTimeout">The number of milliseconds to wait for the mutex, or Infinite (-1) to wait indefinitely</param>
  public SharedMemoryReader(int mutexTimeout = 1000)
  {
    _mutexTimeout = mutexTimeout;
  }

  /// <summary>
  /// Reads the sensor values of the local HWiNFO instance
  /// </summary>
  /// <returns>The sensor values</returns>
  /// <exception cref="FileNotFoundException">The shared memory file does not exist.</exception> 
  /// <exception cref="UnauthorizedAccessException">Access is invalid for the shared memory file.</exception> 
  /// <exception cref="InvalidDataException">Failure to parse data read from the shared memory file.</exception> 
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
  public IEnumerable<SensorReading> ReadRemote(int index = 0)
  {
    if (index < 0) throw new ArgumentOutOfRangeException(nameof(index), "Must be greater than or equal to 0");
    return ReadMemoryMappedFile($"{HWiNfoSensorsMapFileNameRemote}{index}");
  }

  /// <inheritdoc />
  public void Dispose()
  {
    _mutex?.Dispose();
    _mutex = null;
    foreach (var value in _cache.Values)
    {
      value.Accessor.Dispose();
      value.Mmf.Dispose();
    }

    _cache.Clear();
  }

  private SensorReading[] ReadMemoryMappedFile(string fileName)
  {
    var mutex = AcquireMutex();
    try
    {
      mutex?.WaitOne(_mutexTimeout);

      if (!_cache.TryGetValue(fileName, out var cached))
      {
        var mmf = MemoryMappedFile.OpenExisting(fileName, MemoryMappedFileRights.Read);
        var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        cached = (mmf, accessor);
        _cache[fileName] = cached;
      }

      return ReadSensorReadings(cached.Accessor);
    }
    finally
    {
      try
      {
        mutex?.ReleaseMutex();
      }
      catch
      {
        // ignored
      }
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

  private static SensorReading[] ReadSensorReadings(MemoryMappedViewAccessor accessor)
  {
    // Read sharedMem
    accessor.Read(0, out SmSensorsSharedMem2 sharedMem);

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