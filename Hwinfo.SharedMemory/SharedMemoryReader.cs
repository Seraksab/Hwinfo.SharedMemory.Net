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
  private readonly Mutex _mutex;

  /// <summary>
  /// Creates a new SharedMemoryReader 
  /// </summary>
  /// <param name="mutexTimeout">The number of milliseconds to wait for the mutex, or Infinite (-1) to wait indefinitely</param>
  public SharedMemoryReader(int mutexTimeout = 1000)
  {
    _mutexTimeout = mutexTimeout;
    _mutex = new Mutex(false, HWiNfoSensorsSm2Mutex);
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
  public void Dispose() => _mutex.Dispose();

  private SensorReading[] ReadMemoryMappedFile(string fileName)
  {
    try
    {
      _mutex.WaitOne(_mutexTimeout);
      using var mmf = MemoryMappedFile.OpenExisting(fileName, MemoryMappedFileRights.Read);
      return ReadSensorReadings(mmf);
    }
    finally
    {
      try
      {
        _mutex.ReleaseMutex();
      }
      catch
      {
        // ignored
      }
    }
  }

  private static SensorReading[] ReadSensorReadings(MemoryMappedFile mmf)
  {
    // Read sharedMem
    var sharedMem = ReadStruct<SmSensorsSharedMem2>(mmf, 0, Marshal.SizeOf(typeof(SmSensorsSharedMem2)));

    // Read sensor data and reading data
    var sensors = ReadSensorData(mmf, sharedMem);
    var readings = ReadReadingData(mmf, sharedMem);

    // Convert to SensorReading 
    return ConvertToSensorReading(readings, sensors);
  }

  private static SmSensorsSensorElement[] ReadSensorData(MemoryMappedFile mmf, SmSensorsSharedMem2 sharedMem)
  {
    return ReadStructs<SmSensorsSensorElement>(
      mmf,
      sharedMem.SensorSection_Offset,
      sharedMem.SensorSection_NumElements,
      (int)sharedMem.SensorSection_SizeOfElement
    );
  }

  private static SmSensorsReadingElement[] ReadReadingData(MemoryMappedFile mmf, SmSensorsSharedMem2 sharedMem)
  {
    return ReadStructs<SmSensorsReadingElement>(
      mmf,
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
      var reading = readings[idx];
      var sensor = sensors[(int)reading.Idx];
      sensorReadings[idx] = new SensorReading(
        Id: reading.Id,
        Index: reading.Idx,
        Type: reading.Type,
        LabelOrig: reading.LabelOrig,
        LabelUser: reading.LabelUser,
        Unit: reading.Unit,
        Value: reading.Value,
        ValueMin: reading.ValueMin,
        ValueMax: reading.ValueMax,
        ValueAvg: reading.ValueAvg,
        GroupId: sensor.Id,
        GroupInstanceId: sensor.Instance,
        GroupLabelUser: sensor.LabelUser,
        GroupLabelOrig: sensor.LabelOrig
      );
    }

    return sensorReadings;
  }

  private static T ReadStruct<T>(MemoryMappedFile mmf, long offset, long elementSize) where T : struct
  {
    using var viewAccessor = mmf.CreateViewAccessor(
      offset: offset,
      size: elementSize,
      access: MemoryMappedFileAccess.Read
    );

    viewAccessor.Read(0, out T reading);
    return reading;
  }

  private static T[] ReadStructs<T>(MemoryMappedFile mmf, long offset, long numElements, int elementSize)
    where T : struct
  {
    using var viewStream = mmf.CreateViewStream(
      offset: offset,
      size: elementSize * numElements,
      access: MemoryMappedFileAccess.Read
    );

    var results = new T[numElements];
    var byteBuffer = new byte[elementSize];
    for (var idx = 0; idx < numElements; idx++)
    {
      if (viewStream.Read(byteBuffer) < elementSize)
      {
        throw new InvalidDataException("Failed to read bytes from memory mapped file.");
      }

      var handle = GCHandle.Alloc(byteBuffer, GCHandleType.Pinned);
      try
      {
        results[idx] = (T)(
          Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T))
          ?? throw new InvalidDataException("Failed to convert bytes to struct.")
        );
      }
      finally
      {
        handle.Free();
      }
    }

    return results;
  }
}