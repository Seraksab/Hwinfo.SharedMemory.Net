namespace Hwinfo.SharedMemory.Tests;

/// <summary>
/// Tests that read from a snapshot of the HWiNFO shared memory and therefore run without HWiNFO.
/// </summary>
public class SnapshotTests
{
  private const int SnapshotSensorCount = 25;
  private const int SnapshotReadingCount = 470;

  private readonly SharedMemoryReader _reader = new();

  [Fact]
  public void Read_ShouldReturnAllReadings()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var readings = _reader.ReadMemoryMappedFile(snapshot.FileName);

    Assert.Equal(SnapshotReadingCount, readings.Length);
  }

  [Fact]
  public void Read_ShouldJoinEachReadingToItsSensor()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var readings = _reader.ReadMemoryMappedFile(snapshot.FileName);

    Assert.All(readings, reading =>
    {
      Assert.InRange(reading.SensorIndex, 0u, SnapshotSensorCount - 1u);
      Assert.NotEmpty(reading.SensorNameOrig);
      Assert.NotEmpty(reading.SensorNameUser);
      Assert.NotEmpty(reading.LabelOrig);
    });
  }

  [Fact]
  public void Read_ShouldDecodeNonAsciiUnits()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var readings = _reader.ReadMemoryMappedFile(snapshot.FileName);

    var temperatures = readings.Where(reading => reading.ReadingType == SensorType.SensorTypeTemp).ToList();
    Assert.NotEmpty(temperatures);
    Assert.All(temperatures, reading => Assert.Equal("°C", reading.Unit));
  }

  [Fact]
  public void Read_ShouldReturnTheSameDataOnRepeatedReads()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var first = _reader.ReadMemoryMappedFile(snapshot.FileName);
    var second = _reader.ReadMemoryMappedFile(snapshot.FileName);

    Assert.Equal(first, second);
  }

  [Fact]
  public void Read_WithInvalidSignature_ShouldThrowInvalidData()
  {
    using var snapshot = SharedMemorySnapshot.Publish(
      data => SharedMemorySnapshot.Write(data, SharedMemorySnapshot.SignatureOffset, 0xDEADBEEF)
    );

    Assert.Throws<InvalidDataException>(() => _reader.ReadMemoryMappedFile(snapshot.FileName));
  }

  [Fact]
  public void Read_WithUnsupportedVersion_ShouldThrowInvalidData()
  {
    using var snapshot = SharedMemorySnapshot.Publish(
      data => SharedMemorySnapshot.Write(data, SharedMemorySnapshot.VersionOffset, 1u)
    );

    Assert.Throws<InvalidDataException>(() => _reader.ReadMemoryMappedFile(snapshot.FileName));
  }

  [Fact]
  public void Read_WithSensorIndexOutOfRange_ShouldThrowInvalidData()
  {
    using var snapshot = SharedMemorySnapshot.Publish(data =>
    {
      var readingOffset = (int)SharedMemorySnapshot.ReadUInt32(data, SharedMemorySnapshot.ReadingSectionOffsetOffset);
      SharedMemorySnapshot.Write(data, readingOffset + SharedMemorySnapshot.ReadingSensorIndexOffset, uint.MaxValue);
    });

    Assert.Throws<InvalidDataException>(() => _reader.ReadMemoryMappedFile(snapshot.FileName));
  }

  [Fact]
  public void Read_WithoutSensors_ShouldThrowInvalidData()
  {
    using var snapshot = SharedMemorySnapshot.Publish(
      data => SharedMemorySnapshot.Write(data, SharedMemorySnapshot.SensorSectionNumElementsOffset, 0u)
    );

    Assert.Throws<InvalidDataException>(() => _reader.ReadMemoryMappedFile(snapshot.FileName));
  }

  [Fact]
  public void Read_WithStalePollTime_ShouldReopenAndStillReturnReadings()
  {
    using var snapshot = SharedMemorySnapshot.Publish(
      data => SharedMemorySnapshot.Write(data, SharedMemorySnapshot.PollTimeOffset, 0L)
    );

    var readings = _reader.ReadMemoryMappedFile(snapshot.FileName);

    Assert.Equal(SnapshotReadingCount, readings.Length);
  }

  [Fact]
  public void Read_WithStalenessCheckDisabled_ShouldNotReopen()
  {
    using var snapshot = SharedMemorySnapshot.Publish(
      data => SharedMemorySnapshot.Write(data, SharedMemorySnapshot.PollTimeOffset, 0L)
    );
    using var reader = new SharedMemoryReader(stalenessTimeout: 0);

    var readings = reader.ReadMemoryMappedFile(snapshot.FileName);

    Assert.Equal(SnapshotReadingCount, readings.Length);
  }

  [Fact]
  public void Read_UnknownFile_ShouldThrowFileNotFound()
  {
    Assert.Throws<FileNotFoundException>(
      () => _reader.ReadMemoryMappedFile($"Local\\Hwinfo.SharedMemory.Tests_{Guid.NewGuid():N}")
    );
  }

  [Fact]
  public void ReadRemote_WithNegativeIndex_ShouldThrowArgumentOutOfRange()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => _reader.ReadRemote(-1));
  }

  [Fact]
  public void Dispose_ShouldBeIdempotent()
  {
    var reader = new SharedMemoryReader();
    reader.Dispose();

    var exception = Record.Exception(() => reader.Dispose());

    Assert.Null(exception);
  }

  [Fact]
  public void Read_AfterDispose_ShouldThrowObjectDisposed()
  {
    using var snapshot = SharedMemorySnapshot.Publish();
    var reader = new SharedMemoryReader();
    reader.ReadMemoryMappedFile(snapshot.FileName);
    reader.Dispose();

    Assert.Throws<ObjectDisposedException>(() => reader.ReadMemoryMappedFile(snapshot.FileName));
  }
}
