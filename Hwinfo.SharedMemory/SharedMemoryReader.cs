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
  private const int HWiNfoSensorsSm2MutexTimeout = 2000;

  private readonly Mutex _mutex;

  /// <summary>
  /// Creates a new SharedMemoryReader 
  /// </summary>
  public SharedMemoryReader()
  {
    _mutex = new Mutex(false, HWiNfoSensorsSm2Mutex);
  }

  /// <summary>
  /// Reads the sensor values of the local HWiNFO instance
  /// </summary>
  /// <returns>The sensor values</returns>
  public IEnumerable<SensorReading> ReadLocal() => ReadMemoryMappedFile(HWiNfoSensorsMapFileNameLocal);


  /// <summary>
  /// Reads the sensor values of the remote HWiNFO instance with the given connection index
  /// </summary>
  /// <param name="index">The connection index starting with 0></param>
  /// <returns>The sensor values</returns>
  public IEnumerable<SensorReading> ReadRemote(int index = 0) => ReadMemoryMappedFile(
    $"{HWiNfoSensorsMapFileNameRemote}{index}"
  );

  public void Dispose() => _mutex.Dispose();

  private SensorReading[] ReadMemoryMappedFile(string fileName)
  {
    try
    {
      _mutex.WaitOne(HWiNfoSensorsSm2MutexTimeout);

      using var mmf = MemoryMappedFile.OpenExisting(fileName, MemoryMappedFileRights.Read);
      return ReadSensorReadings(mmf);
    }
    catch (FileNotFoundException)
    {
      return Array.Empty<SensorReading>();
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
    var sharedMem = ReadStruct<SmSensorsSharedMem2>(mmf, 0, Marshal.SizeOf(typeof(SmSensorsSharedMem2)));

    // read sensors (= group)
    var sensors = ReadStructs<SmSensorsSensorElement>(
      mmf,
      sharedMem.SensorSection_Offset,
      sharedMem.SensorSection_NumElements,
      (int)sharedMem.SensorSection_SizeOfElement
    );

    // read sensor readings 
    var readings = ReadStructs<SmSensorsReadingElement>(
      mmf,
      sharedMem.ReadingSection_Offset,
      sharedMem.ReadingElements_NumElements,
      (int)sharedMem.ReadingSection_SizeOfElement
    );

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
      if (viewStream.Read(byteBuffer) < elementSize) return results;
      results[idx] = BufferToStruct<T>(byteBuffer);
    }

    return results;
  }

  private static T BufferToStruct<T>(byte[] byteBuffer) where T : struct
  {
    if (byteBuffer.Length <= 0) return default;

    var handle = GCHandle.Alloc(byteBuffer, GCHandleType.Pinned);
    try
    {
      return (T)(
        Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T))
        ?? throw new Exception("Failed to parse byte buffer to struct")
      );
    }
    finally
    {
      handle.Free();
    }
  }
}