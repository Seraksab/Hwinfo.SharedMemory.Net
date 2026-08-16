namespace Hwinfo.SharedMemory.Tests;

/// <summary>
/// Tests that read from a snapshot of the HWiNFO shared memory and therefore run without HWiNFO.
/// </summary>
public class SnapshotTests : IDisposable
{
  private const int SnapshotReadingCount = 470;

  // One of the snapshot's 25 sensors ("ASUS EC") has no readings of its own, so only 24 of them are
  // reachable through the readings
  private const int SnapshotSensorCount = 24;

  private readonly SharedMemoryReader _reader = new();

  // Without this the reader keeps the snapshot's section mapped after the snapshot itself is
  // disposed, so it outlives the test that published it
  public void Dispose() => _reader.Dispose();

  [Fact]
  public void Read_ShouldReturnAllReadings()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var readings = _reader.ReadMemoryMappedFile(snapshot.FileName).Readings;

    Assert.Equal(SnapshotReadingCount, readings.Length);
  }

  [Fact]
  public void Read_ShouldJoinEachReadingToItsSensor()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var readings = _reader.ReadMemoryMappedFile(snapshot.FileName).Readings;

    Assert.All(readings, reading =>
    {
      Assert.NotNull(reading.Sensor);
      Assert.NotEmpty(reading.Sensor.NameOrig);
      Assert.NotEmpty(reading.Sensor.NameUser);
      Assert.NotEmpty(reading.LabelOrig);
    });
    Assert.Equal(
      SnapshotSensorCount,
      readings.Select(reading => reading.Sensor).Distinct(ReferenceEqualityComparer.Instance).Count()
    );
  }

  [Theory]
  // Decoded independently from the snapshot's bytes, so the parser is checked against something
  // other than itself
  [InlineData(
    0, SensorType.Other, 134217728u, "Virtual Memory Committed", "MB",
    26649.0, 7170.0, 28151.0, 21411.165534280426, 4026532609u, 0u, "System: ASUS "
  )]
  [InlineData(
    235, SensorType.Temp, 16777225u, "Temp9", "°C",
    24.0, 24.0, 24.0, 24.0, 4144396688u, 0u, "ASUS TUF GAMING X670E-PLUS WIFI (Nuvoton NCT6799D)"
  )]
  [InlineData(
    469, SensorType.Other, 134217728u, "Total Errors", "",
    0.0, 0.0, 0.0, 0.0, 4026535584u, 0u, "Windows Hardware Errors (WHEA)"
  )]
  public void Read_ShouldDecodeEveryFieldOfAReading(
    int index, SensorType readingType, uint readingId, string label, string unit,
    double value, double valueMin, double valueMax, double valueAvg,
    uint sensorId, uint sensorInstance, string sensorName
  )
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var reading = _reader.ReadMemoryMappedFile(snapshot.FileName).Readings[index];

    Assert.Equal(readingType, reading.ReadingType);
    Assert.Equal(readingId, reading.ReadingId);
    Assert.Equal(label, reading.LabelOrig);
    Assert.Equal(label, reading.LabelUser);
    Assert.Equal(unit, reading.Unit);
    Assert.Equal(value, reading.Value);
    Assert.Equal(valueMin, reading.ValueMin);
    Assert.Equal(valueMax, reading.ValueMax);
    Assert.Equal(valueAvg, reading.ValueAvg);
    Assert.Equal(sensorId, reading.Sensor.Id);
    Assert.Equal(sensorInstance, reading.Sensor.Instance);
    Assert.Equal(sensorName, reading.Sensor.NameOrig);
    Assert.Equal(sensorName, reading.Sensor.NameUser);
  }

  [Fact]
  public void Read_ShouldShareOneSensorInstanceAcrossItsReadings()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var readings = _reader.ReadMemoryMappedFile(snapshot.FileName).Readings;

    // All readings of a sensor point at the same instance, and it survives the next read
    var bySensor = readings.GroupBy(reading => reading.Sensor, ReferenceEqualityComparer.Instance).ToList();
    Assert.All(bySensor, group => Assert.All(group, reading => Assert.Same(group.Key, reading.Sensor)));

    var second = _reader.ReadMemoryMappedFile(snapshot.FileName).Readings;
    Assert.All(second.Zip(readings), pair => Assert.Same(pair.Second.Sensor, pair.First.Sensor));
  }

  [Fact]
  public void Read_ShouldReuseTheDecodedStringsOfUnchangedElements()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var first = _reader.ReadMemoryMappedFile(snapshot.FileName).Readings;
    var second = _reader.ReadMemoryMappedFile(snapshot.FileName).Readings;

    Assert.All(second.Zip(first), pair =>
    {
      Assert.Same(pair.Second.LabelOrig, pair.First.LabelOrig);
      Assert.Same(pair.Second.LabelUser, pair.First.LabelUser);
      Assert.Same(pair.Second.Unit, pair.First.Unit);
    });
  }

  [Fact]
  public void Read_ShouldReturnEverySensorIncludingThoseWithoutReadings()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var result = _reader.ReadMemoryMappedFile(snapshot.FileName);

    Assert.Equal(SnapshotSensorCount + 1, result.Sensors.Length);
    Assert.All(result.Sensors, sensor => Assert.NotEmpty(sensor.NameOrig));

    // Every reading points at the very instance from the sensor list, and the one sensor without
    // readings is reachable through that list alone
    Assert.All(
      result.Readings,
      reading => Assert.Contains(result.Sensors, sensor => ReferenceEquals(sensor, reading.Sensor))
    );
    var withoutReadings = result.Sensors
      .Where(sensor => result.Readings.All(reading => !ReferenceEquals(reading.Sensor, sensor)))
      .ToList();
    Assert.Single(withoutReadings);
  }

  [Fact]
  public void Read_ShouldHandOutTheSameSensorsWhileTheyAreUnchanged()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var first = _reader.ReadMemoryMappedFile(snapshot.FileName);
    var second = _reader.ReadMemoryMappedFile(snapshot.FileName);

    // Nothing changed, so the same copy is handed out again rather than a new one.
    // ImmutableArray's == compares the array behind it, so this is an identity check.
    Assert.True(first.Sensors == second.Sensors);
  }

  [Fact]
  public void Read_ShouldReportThePollTimeOfTheSection()
  {
    // The snapshot is published with its poll time set to now, in whole seconds
    var published = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    using var snapshot = SharedMemorySnapshot.Publish();

    var pollTime = _reader.ReadMemoryMappedFile(snapshot.FileName).PollTime;

    Assert.InRange(pollTime, published, published.AddSeconds(5));
    Assert.Equal(TimeSpan.Zero, pollTime.Offset);
  }

  [Fact]
  public void Read_WithUnrepresentablePollTime_ShouldThrowInvalidData()
  {
    using var snapshot = SharedMemorySnapshot.Publish(data =>
      SharedMemorySnapshot.Write(data, SharedMemorySnapshot.PollTimeOffset, long.MaxValue)
    );

    Assert.Throws<InvalidDataException>(() => _reader.ReadMemoryMappedFile(snapshot.FileName));
  }

  [Fact]
  public void Read_WithReuseUnchangedPolls_ShouldReturnTheSameResultWhilePollTimeIsUnchanged()
  {
    using var snapshot = SharedMemorySnapshot.Publish();
    using var reader = new SharedMemoryReader(new SharedMemoryReaderOptions { ReuseUnchangedPolls = true });

    var first = reader.ReadMemoryMappedFile(snapshot.FileName);
    var second = reader.ReadMemoryMappedFile(snapshot.FileName);

    Assert.True(first.Readings == second.Readings);
    Assert.Equal(first.PollTime, second.PollTime);
  }

  [Fact]
  public void Read_WithoutReuseUnchangedPolls_ShouldReadAgainForEveryCall()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var first = _reader.ReadMemoryMappedFile(snapshot.FileName).Readings;
    var second = _reader.ReadMemoryMappedFile(snapshot.FileName).Readings;

    // A fresh array every time, holding equal values
    Assert.False(first == second);
    Assert.Equal<SensorReading>(first, second);
  }

  [Fact]
  public void Read_WithOversizedElements_ShouldStillParseTheKnownFields()
  {
    // HWiNFO's live elements are larger than the layout the reader knows, which the snapshot covers
    using var snapshot = SharedMemorySnapshot.Publish();
    var sensorElementSize = SharedMemorySnapshot.ReadUInt32(
      SharedMemorySnapshot.Bytes, SharedMemorySnapshot.SensorSectionSizeOfElementOffset
    );
    var readingElementSize = SharedMemorySnapshot.ReadUInt32(
      SharedMemorySnapshot.Bytes, SharedMemorySnapshot.ReadingSectionSizeOfElementOffset
    );
    Assert.True(sensorElementSize > 264, $"expected the snapshot's sensor elements to exceed 264 bytes");
    Assert.True(readingElementSize > 316, $"expected the snapshot's reading elements to exceed 316 bytes");

    var readings = _reader.ReadMemoryMappedFile(snapshot.FileName).Readings;

    Assert.Equal(SnapshotReadingCount, readings.Length);
  }

  [Fact]
  public void Read_WithSectionBeyondTheMapping_ShouldThrowInvalidData()
  {
    using var snapshot = SharedMemorySnapshot.Publish(data =>
      SharedMemorySnapshot.Write(data, SharedMemorySnapshot.ReadingSectionOffsetOffset, uint.MaxValue - 16)
    );

    Assert.Throws<InvalidDataException>(() => _reader.ReadMemoryMappedFile(snapshot.FileName));
  }

  [Fact]
  public void Read_WithUndersizedElements_ShouldThrowInvalidData()
  {
    using var snapshot = SharedMemorySnapshot.Publish(data =>
      SharedMemorySnapshot.Write(data, SharedMemorySnapshot.ReadingSectionSizeOfElementOffset, 8u)
    );

    Assert.Throws<InvalidDataException>(() => _reader.ReadMemoryMappedFile(snapshot.FileName));
  }

  [Fact]
  public void Read_ShouldDecodeNonAsciiUnits()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var readings = _reader.ReadMemoryMappedFile(snapshot.FileName).Readings;

    var temperatures = readings.Where(reading => reading.ReadingType == SensorType.Temp).ToList();
    Assert.NotEmpty(temperatures);
    Assert.All(temperatures, reading => Assert.Equal("°C", reading.Unit));
  }

  [Fact]
  public void Read_ShouldReturnTheSameDataOnRepeatedReads()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var first = _reader.ReadMemoryMappedFile(snapshot.FileName).Readings;
    var second = _reader.ReadMemoryMappedFile(snapshot.FileName).Readings;

    Assert.Equal<SensorReading>(first, second);
  }

  [Fact]
  public void Read_WithInvalidSignature_ShouldThrowInvalidData()
  {
    using var snapshot = SharedMemorySnapshot.Publish(data =>
      SharedMemorySnapshot.Write(data, SharedMemorySnapshot.SignatureOffset, 0xDEADBEEF)
    );

    Assert.Throws<InvalidDataException>(() => _reader.ReadMemoryMappedFile(snapshot.FileName));
  }

  [Fact]
  public void Read_WithUnsupportedVersion_ShouldThrowInvalidData()
  {
    using var snapshot =
      SharedMemorySnapshot.Publish(data => SharedMemorySnapshot.Write(data, SharedMemorySnapshot.VersionOffset, 1u)
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
  public void Read_WithUnknownReadingType_ShouldReportOther()
  {
    using var snapshot = SharedMemorySnapshot.Publish(data =>
    {
      var readingOffset = (int)SharedMemorySnapshot.ReadUInt32(data, SharedMemorySnapshot.ReadingSectionOffsetOffset);
      SharedMemorySnapshot.Write(data, readingOffset + SharedMemorySnapshot.ReadingTypeOffset, 42u);
    });

    var readings = _reader.ReadMemoryMappedFile(snapshot.FileName).Readings;

    Assert.Equal(SensorType.Other, readings[0].ReadingType);
  }

  [Fact]
  public void Read_WithoutSensors_ShouldThrowInvalidData()
  {
    using var snapshot = SharedMemorySnapshot.Publish(data =>
      SharedMemorySnapshot.Write(data, SharedMemorySnapshot.SensorSectionNumElementsOffset, 0u)
    );

    Assert.Throws<InvalidDataException>(() => _reader.ReadMemoryMappedFile(snapshot.FileName));
  }

  [Fact]
  public void Read_WithStalePollTime_ShouldReopenAndStillReturnReadings()
  {
    using var snapshot =
      SharedMemorySnapshot.Publish(data => SharedMemorySnapshot.Write(data, SharedMemorySnapshot.PollTimeOffset, 0L)
      );

    var readings = _reader.ReadMemoryMappedFile(snapshot.FileName).Readings;

    Assert.Equal(SnapshotReadingCount, readings.Length);
  }

  [Fact]
  public void Read_WithStalenessCheckDisabled_ShouldNotReopen()
  {
    using var snapshot =
      SharedMemorySnapshot.Publish(data => SharedMemorySnapshot.Write(data, SharedMemorySnapshot.PollTimeOffset, 0L)
      );
    using var reader = new SharedMemoryReader(new SharedMemoryReaderOptions { StalenessTimeout = TimeSpan.Zero });

    var readings = reader.ReadMemoryMappedFile(snapshot.FileName).Readings;

    Assert.Equal(SnapshotReadingCount, readings.Length);
  }

  [Fact]
  public void Read_UnknownFile_ShouldThrowFileNotFound()
  {
    Assert.Throws<FileNotFoundException>(() =>
      _reader.ReadMemoryMappedFile($"Local\\Hwinfo.SharedMemory.Tests_{Guid.NewGuid():N}")
    );
  }

  [Fact]
  public void ReadRemote_WithAnIndexThatIsNotConnected_ShouldThrowFileNotFound()
  {
    // Covers how ReadRemote builds the section name. No HWiNFO needed: an index this high has no
    // section either way, which is also why it doesn't assume anything about the machine's
    // connections the way asserting on index 1 would
    Assert.Throws<FileNotFoundException>(() => _reader.ReadRemote(999));
  }

  [Fact]
  public void ReadRemote_WithNegativeIndex_ShouldThrowArgumentOutOfRange()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => _reader.ReadRemote(-1));
  }

  [Theory]
  [InlineData(-1, 0)]
  [InlineData(0, -1)]
  public void Constructor_WithNegativeTimeout_ShouldThrowArgumentOutOfRange(int mutexSeconds, int stalenessSeconds)
  {
    var options = new SharedMemoryReaderOptions
    {
      MutexTimeout = TimeSpan.FromSeconds(mutexSeconds),
      StalenessTimeout = TimeSpan.FromSeconds(stalenessSeconds)
    };

    Assert.Throws<ArgumentOutOfRangeException>(() => new SharedMemoryReader(options));
  }

  [Fact]
  public void Constructor_WithInfiniteMutexTimeout_ShouldBeAccepted()
  {
    using var snapshot = SharedMemorySnapshot.Publish();
    using var reader = new SharedMemoryReader(
      new SharedMemoryReaderOptions { MutexTimeout = Timeout.InfiniteTimeSpan }
    );

    var readings = reader.ReadMemoryMappedFile(snapshot.FileName).Readings;

    Assert.Equal(SnapshotReadingCount, readings.Length);
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