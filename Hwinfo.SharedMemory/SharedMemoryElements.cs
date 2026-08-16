using System.Runtime.InteropServices;

namespace Hwinfo.SharedMemory;

/// <summary>
/// The header of the HWiNFO shared memory section.
/// A binary contract with HWiNFO: field order, types and packing must match its layout.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
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

  /// <summary>
  /// Whether both headers describe the same poll of the same section layout, i.e. whether the data
  /// read against this header is still the data the other header describes.
  /// </summary>
  internal bool Describes(in SmSensorsSharedMem2 other) =>
    Signature == other.Signature &&
    PollTime == other.PollTime &&
    SensorSection_Offset == other.SensorSection_Offset &&
    SensorSection_SizeOfElement == other.SensorSection_SizeOfElement &&
    SensorSection_NumElements == other.SensorSection_NumElements &&
    ReadingSection_Offset == other.ReadingSection_Offset &&
    ReadingSection_SizeOfElement == other.ReadingSection_SizeOfElement &&
    ReadingElements_NumElements == other.ReadingElements_NumElements;
}

/// <summary>
/// The layout of the sensor and reading elements: the offset of every field within its element and
/// the number of bytes we need from it.
/// <para>
/// A binary contract with HWiNFO, like <see cref="SmSensorsSharedMem2"/>. The element sizes here are
/// the required <em>minimum</em>: HWiNFO may append fields, so the actual stride always comes from
/// the header's <c>SizeOfElement</c>.
/// </para>
/// </summary>
internal static class SmLayout
{
  /// <summary>Size of <see cref="SmSensorsSharedMem2"/>, which is fixed by the same contract.</summary>
  internal const int HeaderSize = 44;

  /// <summary>Length of the fixed-length name and label fields, including the NUL terminator.</summary>
  internal const int StringLength = 128;

  /// <summary>Length of the fixed-length unit field, including the NUL terminator.</summary>
  internal const int UnitLength = 16;

  // Sensor element
  internal const int SensorId = 0;
  internal const int SensorInstance = 4;
  internal const int SensorNameOrig = 8;
  internal const int SensorNameUser = 136;
  internal const int SensorElementSize = 264;

  // Reading element
  internal const int ReadingType = 0;
  internal const int ReadingSensorIndex = 4;
  internal const int ReadingId = 8;
  internal const int ReadingLabelOrig = 12;
  internal const int ReadingLabelUser = 140;
  internal const int ReadingUnit = 268;
  internal const int ReadingValue = 284;
  internal const int ReadingValueMin = 292;
  internal const int ReadingValueMax = 300;
  internal const int ReadingValueAvg = 308;
  internal const int ReadingElementSize = 316;

  /// <summary>
  /// The three string fields of a reading element are adjacent, so one comparison covers all of them.
  /// </summary>
  internal const int ReadingStringsLength = ReadingUnit + UnitLength - ReadingLabelOrig;
}
