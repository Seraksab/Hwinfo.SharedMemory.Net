using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Hwinfo.SharedMemory.Tests;

/// <summary>
/// Guards the binary contract with HWiNFO. Everything here is a constant rather than behaviour, which
/// is exactly why it needs pinning: a wrong offset doesn't fail to compile, it silently decodes the
/// wrong bytes, and the snapshot tests would only catch it for the fields they happen to assert.
/// </summary>
public class LayoutTests
{
  [Fact]
  public void Header_ShouldHaveTheSizeTheLayoutAssumes()
  {
    // TryReadHeader bounds checks the mapping against SmLayout.HeaderSize before reading the struct
    Assert.Equal(SmLayout.HeaderSize, Unsafe.SizeOf<SmSensorsSharedMem2>());
    Assert.Equal(SmLayout.HeaderSize, Marshal.SizeOf<SmSensorsSharedMem2>());
  }

  [Theory]
  [InlineData(nameof(SmSensorsSharedMem2.Signature), 0)]
  [InlineData(nameof(SmSensorsSharedMem2.Version), 4)]
  [InlineData(nameof(SmSensorsSharedMem2.Revision), 8)]
  [InlineData(nameof(SmSensorsSharedMem2.PollTime), 12)]
  [InlineData(nameof(SmSensorsSharedMem2.SensorSection_Offset), 20)]
  [InlineData(nameof(SmSensorsSharedMem2.SensorSection_SizeOfElement), 24)]
  [InlineData(nameof(SmSensorsSharedMem2.SensorSection_NumElements), 28)]
  [InlineData(nameof(SmSensorsSharedMem2.ReadingSection_Offset), 32)]
  [InlineData(nameof(SmSensorsSharedMem2.ReadingSection_SizeOfElement), 36)]
  [InlineData(nameof(SmSensorsSharedMem2.ReadingElements_NumElements), 40)]
  public void Header_ShouldPlaceEveryFieldWhereHwinfoWritesIt(string field, int offset)
  {
    // Pack = 1: PollTime sits at 12, not at 16 where natural alignment would put it
    Assert.Equal(offset, (int)Marshal.OffsetOf<SmSensorsSharedMem2>(field));
  }

  [Fact]
  public void SensorElement_FieldsShouldBeAdjacentAndCoverTheElement()
  {
    Assert.Equal(0, SmLayout.SensorId);
    Assert.Equal(SmLayout.SensorId + sizeof(uint), SmLayout.SensorInstance);
    Assert.Equal(SmLayout.SensorInstance + sizeof(uint), SmLayout.SensorNameOrig);
    Assert.Equal(SmLayout.SensorNameOrig + SmLayout.StringLength, SmLayout.SensorNameUser);
    Assert.Equal(SmLayout.SensorNameUser + SmLayout.StringLength, SmLayout.SensorElementSize);
  }

  [Fact]
  public void ReadingElement_FieldsShouldBeAdjacentAndCoverTheElement()
  {
    Assert.Equal(0, SmLayout.ReadingType);
    Assert.Equal(SmLayout.ReadingType + sizeof(uint), SmLayout.ReadingSensorIndex);
    Assert.Equal(SmLayout.ReadingSensorIndex + sizeof(uint), SmLayout.ReadingId);
    Assert.Equal(SmLayout.ReadingId + sizeof(uint), SmLayout.ReadingLabelOrig);
    Assert.Equal(SmLayout.ReadingLabelOrig + SmLayout.StringLength, SmLayout.ReadingLabelUser);
    Assert.Equal(SmLayout.ReadingLabelUser + SmLayout.StringLength, SmLayout.ReadingUnit);
    Assert.Equal(SmLayout.ReadingUnit + SmLayout.UnitLength, SmLayout.ReadingValue);
    Assert.Equal(SmLayout.ReadingValue + sizeof(double), SmLayout.ReadingValueMin);
    Assert.Equal(SmLayout.ReadingValueMin + sizeof(double), SmLayout.ReadingValueMax);
    Assert.Equal(SmLayout.ReadingValueMax + sizeof(double), SmLayout.ReadingValueAvg);
    Assert.Equal(SmLayout.ReadingValueAvg + sizeof(double), SmLayout.ReadingElementSize);
  }

  [Fact]
  public void ReadingStringsLength_ShouldSpanBothLabelsAndTheUnit()
  {
    // Parse compares this one block instead of the three fields separately, which only holds while
    // they stay adjacent
    Assert.Equal(2 * SmLayout.StringLength + SmLayout.UnitLength, SmLayout.ReadingStringsLength);
    Assert.Equal(SmLayout.ReadingUnit + SmLayout.UnitLength, SmLayout.ReadingLabelOrig + SmLayout.ReadingStringsLength);
  }

  [Fact]
  public void Snapshot_ShouldDeclareElementsAtLeastAsLargeAsTheLayoutNeeds()
  {
    // The live elements are larger than the part the reader knows, which is the case the header's
    // SizeOfElement exists for
    var sensorElementSize =
      SharedMemorySnapshot.ReadUInt32(SharedMemorySnapshot.Bytes, SharedMemorySnapshot.SensorSectionSizeOfElementOffset);
    var readingElementSize = SharedMemorySnapshot.ReadUInt32(
      SharedMemorySnapshot.Bytes, SharedMemorySnapshot.ReadingSectionSizeOfElementOffset
    );

    Assert.True(sensorElementSize > SmLayout.SensorElementSize, $"sensor elements are {sensorElementSize} bytes");
    Assert.True(readingElementSize > SmLayout.ReadingElementSize, $"reading elements are {readingElementSize} bytes");
  }
}
