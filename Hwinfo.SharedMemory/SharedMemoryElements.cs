using System.Runtime.InteropServices;

namespace Hwinfo.SharedMemory;

internal readonly struct SmConstants
{
  internal const int HWiNFO_SENSORS_STRING_LEN2 = 128;
  internal const int HWiNFO_UNIT_STRING_LEN = 16;
}

[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
internal readonly struct SmSensorsReadingElement
{
  public readonly SensorType Type;  // Type of sensor reading
  public readonly uint SensorIndex; // This is the index of sensor in the Sensors[] array to which this reading belongs to
  public readonly uint ReadingId;   // A unique ID of the reading within a particular sensor

  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = SmConstants.HWiNFO_SENSORS_STRING_LEN2)]
  public readonly string LabelOrig; // Original label (e.g. "Chassis2 Fan")

  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = SmConstants.HWiNFO_SENSORS_STRING_LEN2)]
  public readonly string LabelUser; // Label displayed, which might have been renamed by user

  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = SmConstants.HWiNFO_UNIT_STRING_LEN)]
  public readonly string Unit;

  public readonly double Value;
  public readonly double ValueMin;
  public readonly double ValueMax;
  public readonly double ValueAvg;
}

[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
internal readonly struct SmSensorsSensorElement
{
  public readonly uint SensorId;   // A unique Sensor ID
  public readonly uint SensorInst; // The instance of the sensor (together with dwSensorID forms a unique ID)

  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = SmConstants.HWiNFO_SENSORS_STRING_LEN2)]
  public readonly string NameOrig; // Original sensor name

  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = SmConstants.HWiNFO_SENSORS_STRING_LEN2)]
  public readonly string NameUser; // Sensor name displayed, which might have been renamed by user
}

[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
internal readonly struct SmSensorsSharedMem2
{
  public readonly uint Signature;
  public readonly uint Version;
  public readonly uint Revision;
  public readonly long PollTime;
  public readonly uint SensorSection_Offset;
  public readonly uint SensorSection_SizeOfElement;
  public readonly uint SensorSection_NumElements;
  public readonly uint ReadingSection_Offset;
  public readonly uint ReadingSection_SizeOfElement;
  public readonly uint ReadingElements_NumElements;
}