﻿using System.Runtime.InteropServices;

namespace Hwinfo.SharedMemory;

[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
internal readonly struct SmSensorsReadingElement
{
  public readonly SensorType Type;
  public readonly uint Idx;
  public readonly uint Id;

  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
  public readonly string LabelOrig;

  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
  public readonly string LabelUser;

  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
  public readonly string Unit;

  public readonly double Value;
  public readonly double ValueMin;
  public readonly double ValueMax;
  public readonly double ValueAvg;
}

[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
internal readonly struct SmSensorsSensorElement
{
  public readonly uint Id;
  public readonly uint Instance;

  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
  public readonly string LabelOrig;

  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
  public readonly string LabelUser;
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