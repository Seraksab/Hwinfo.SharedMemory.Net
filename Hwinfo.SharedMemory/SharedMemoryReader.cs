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
  private const string HWiNfoSensorsMapFileName = "Global\\HWiNFO_SENS_SM2";
  private const int HWiNfoSensorsSm2MutexTimeout = 2000;

  private readonly Mutex _mutex;
  private SmSensorsSensorElement[] _sensors;

  /// <summary>
  /// Creates a new SharedMemoryReader 
  /// </summary>
  public SharedMemoryReader()
  {
    _mutex = new Mutex(false, HWiNfoSensorsSm2Mutex);
    _sensors = Array.Empty<SmSensorsSensorElement>();
  }

  /// <summary>
  /// Reads the sensor values
  /// </summary>
  /// <returns>The sensor values</returns>
  public IEnumerable<SensorReading> Read()
  {
    try
    {
      _mutex.WaitOne(HWiNfoSensorsSm2MutexTimeout);
      return ReadSm();
    }
    catch (FileNotFoundException)
    {
      _sensors = Array.Empty<SmSensorsSensorElement>();
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

  public void Dispose() => _mutex.Dispose();

  private IEnumerable<SensorReading> ReadSm()
  {
    using var mmf = MemoryMappedFile.OpenExisting(HWiNfoSensorsMapFileName, MemoryMappedFileRights.Read);
    using var accessor = mmf.CreateViewAccessor(
      offset: 0,
      size: Marshal.SizeOf(typeof(SmSensorsSharedMem2)),
      access: MemoryMappedFileAccess.Read
    );

    accessor.Read(0, out SmSensorsSharedMem2 sharedMem);

    if (_sensors.Length != sharedMem.SensorSection_NumElements)
    {
      _sensors = ReadSensorsGroups(mmf, sharedMem);
    }

    return ReadSensors(mmf, sharedMem);
  }

  private IEnumerable<SensorReading> ReadSensors(MemoryMappedFile mmf, in SmSensorsSharedMem2 sharedMem)
  {
    var sizeReadingSection = sharedMem.ReadingSection_SizeOfElement;
    var byteBuffer = new byte[sizeReadingSection];
    var readings = new SensorReading[sharedMem.ReadingElements_NumElements];

    for (uint idx = 0; idx < sharedMem.ReadingElements_NumElements; idx++)
    {
      using var sensorElementAccessor = mmf.CreateViewStream(
        offset: sharedMem.ReadingSection_Offset + (idx * sizeReadingSection),
        size: sizeReadingSection,
        access: MemoryMappedFileAccess.Read
      );

      sensorElementAccessor.Read(byteBuffer, 0, (int)sizeReadingSection);

      var reading = BufferToStruct<SmSensorsReadingElement>(byteBuffer);
      var sensor = _sensors[(int)reading.Idx];

      readings[idx] = new SensorReading(
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

    return readings;
  }

  private SmSensorsSensorElement[] ReadSensorsGroups(MemoryMappedFile mmf, in SmSensorsSharedMem2 sharedMem)
  {
    var sizeSensorElement = sharedMem.SensorSection_SizeOfElement;
    var byteBuffer = new byte[sizeSensorElement];
    var sensors = new SmSensorsSensorElement[sharedMem.SensorSection_NumElements];

    for (uint idx = 0; idx < sharedMem.SensorSection_NumElements; idx++)
    {
      using var sensorElementAccessor = mmf.CreateViewStream(
        sharedMem.SensorSection_Offset + (idx * sizeSensorElement),
        sizeSensorElement,
        MemoryMappedFileAccess.Read
      );

      sensorElementAccessor.Read(byteBuffer, 0, (int)sizeSensorElement);
      sensors[idx] = BufferToStruct<SmSensorsSensorElement>(byteBuffer);
    }

    return sensors;
  }

  private static T BufferToStruct<T>(byte[] byteBuffer) where T : struct
  {
    if (byteBuffer.Length <= 0)
    {
      throw new Exception("No bytes read");
    }

    var handle = GCHandle.Alloc(byteBuffer, GCHandleType.Pinned);
    var element = (T)
    (
      Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T))
      ?? throw new Exception("Failed to parse byte buffer to struct")
    );

    handle.Free();

    return element;
  }
}