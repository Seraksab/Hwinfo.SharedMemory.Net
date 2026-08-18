namespace Hwinfo.SharedMemory.Tests;

/// <summary>
/// Tests that change a published section between two reads of the same reader.
/// <para>
/// A reader caches one mapped section per file name, and most of its state - the byte buffers, the
/// decoded strings, the sensor instances, the reused result - only exists on that cached section.
/// Publishing a second snapshot would get a fresh name and therefore a fresh section, so none of
/// those paths would run. Patching the published section is what makes them reachable.
/// </para>
/// </summary>
public class SectionChangeTests : IDisposable
{
  private static readonly int FullReadingCount = (int)SharedMemorySnapshot.ReadingCount;

  private readonly SharedMemoryReader _reader = new();

  public void Dispose() => _reader.Dispose();

  [Fact]
  public void Read_WhenTheReadingCountChanges_ShouldReturnTheNewShape()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var first = _reader.ReadMemoryMappedFile(snapshot.FileName);
    Assert.Equal(FullReadingCount, first.Readings.Length);

    snapshot.PatchReadingCount(100);
    var second = _reader.ReadMemoryMappedFile(snapshot.FileName);

    Assert.Equal(100, second.Readings.Length);
    Assert.Equal(first.Readings[0].LabelOrig, second.Readings[0].LabelOrig);
    // The buffers and the string caches were sized for the old shape, so they were dropped and
    // everything was decoded again rather than reused from a stale slot
    Assert.NotSame(first.Readings[0].LabelOrig, second.Readings[0].LabelOrig);
  }

  [Fact]
  public void Read_WhenTheReadingCountChangesBack_ShouldReturnTheOriginalReadingsAgain()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var first = _reader.ReadMemoryMappedFile(snapshot.FileName);
    snapshot.PatchReadingCount(100);
    _reader.ReadMemoryMappedFile(snapshot.FileName);
    snapshot.PatchReadingCount((uint)FullReadingCount);
    var third = _reader.ReadMemoryMappedFile(snapshot.FileName);

    Assert.Equal(FullReadingCount, third.Readings.Length);
    Assert.Equal<SensorReading>(first.Readings, third.Readings);
  }

  [Fact]
  public void Read_WhenTheReadingCountDropsToZero_ShouldReturnNoReadingsButEverySensor()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    _reader.ReadMemoryMappedFile(snapshot.FileName);
    snapshot.PatchReadingCount(0);
    var result = _reader.ReadMemoryMappedFile(snapshot.FileName);

    Assert.Empty(result.Readings);
    Assert.Equal((int)SharedMemorySnapshot.SensorCount, result.Sensors.Length);
  }

  [Fact]
  public void Read_WhenASensorChanges_ShouldReplaceOnlyThatSensor()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var first = _reader.ReadMemoryMappedFile(snapshot.FileName);
    var originalName = first.Sensors[0].NameOrig;

    snapshot.PatchSensorName(0, "Renamed Sensor");
    var second = _reader.ReadMemoryMappedFile(snapshot.FileName);

    Assert.Equal("Renamed Sensor", second.Sensors[0].NameOrig);
    Assert.NotEqual(originalName, second.Sensors[0].NameOrig);

    // Every sensor whose bytes didn't move is still the very instance of the previous read
    Assert.Same(first.Sensors[1], second.Sensors[1]);
  }

  [Fact]
  public void Read_WhenASensorChanges_ShouldNotChangeAResultTheCallerAlreadyHolds()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var first = _reader.ReadMemoryMappedFile(snapshot.FileName);
    var originalName = first.Sensors[0].NameOrig;

    snapshot.PatchSensorName(0, "Renamed Sensor");
    var second = _reader.ReadMemoryMappedFile(snapshot.FileName);

    // The reader reuses one sensor array slot by slot, so the list it hands out has to be a copy.
    // Without it the rename would reach back into the result the first read produced.
    Assert.Equal(originalName, first.Sensors[0].NameOrig);
    Assert.False(first.Sensors == second.Sensors);
  }

  [Fact]
  public void Read_WhenTheSectionGoesStale_ShouldReopenItAndDropItsCaches()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var first = _reader.ReadMemoryMappedFile(snapshot.FileName);
    var second = _reader.ReadMemoryMappedFile(snapshot.FileName);
    // Same section throughout, so the decoded strings are handed out again rather than decoded again
    Assert.Same(first.Readings[0].LabelOrig, second.Readings[0].LabelOrig);

    // A poll time from 1970 makes the section look like an orphan HWiNFO no longer updates, so it is
    // released and reopened - which throws away every string decoded from it along with the mapping
    snapshot.PatchPollTime(0);
    var third = _reader.ReadMemoryMappedFile(snapshot.FileName);

    Assert.Equal(FullReadingCount, third.Readings.Length);
    Assert.Equal(second.Readings[0].LabelOrig, third.Readings[0].LabelOrig);
    Assert.NotSame(second.Readings[0].LabelOrig, third.Readings[0].LabelOrig);
  }

  [Fact]
  public void Read_WhenTheSectionStaysStale_ShouldReopenItOnlyOncePerStalenessTimeout()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    var first = _reader.ReadMemoryMappedFile(snapshot.FileName);

    // The first read after it goes stale releases and reopens it, which drops every string it decoded
    snapshot.PatchPollTime(0);
    var reopened = _reader.ReadMemoryMappedFile(snapshot.FileName);
    Assert.NotSame(first.Readings[0].LabelOrig, reopened.Readings[0].LabelOrig);

    // It is still stale, because nothing is refreshing it - an orphan another process keeps alive, or
    // an HWiNFO with monitoring paused. Reopening it again would pay for a mapping and a full decode
    // on every read of a polling loop and change nothing, so it is left alone until the timeout is up.
    var again = _reader.ReadMemoryMappedFile(snapshot.FileName);
    Assert.Same(reopened.Readings[0].LabelOrig, again.Readings[0].LabelOrig);

    // Still the stale data it was reopened on, just without reopening it to get there
    Assert.Equal(DateTimeOffset.UnixEpoch, again.PollTime);
    Assert.Equal(FullReadingCount, again.Readings.Length);
  }

  [Fact]
  public void Read_WhenTheStalenessTimeoutElapses_ShouldGiveTheStaleSectionAnotherChance()
  {
    // The hold-off is only a hold-off. HWiNFO may yet publish a new section under the same name, and
    // opening it again is the only way to find that out, so the retry comes back round every timeout.
    using var snapshot = SharedMemorySnapshot.Publish();
    using var reader = new SharedMemoryReader(
      new SharedMemoryReaderOptions { StalenessTimeout = TimeSpan.FromMilliseconds(50) }
    );

    snapshot.PatchPollTime(0);
    var reopened = reader.ReadMemoryMappedFile(snapshot.FileName);

    var heldOff = reader.ReadMemoryMappedFile(snapshot.FileName);
    Assert.Same(reopened.Readings[0].LabelOrig, heldOff.Readings[0].LabelOrig);

    // Comfortably past both the timeout and the resolution of the clock the hold-off is measured on
    Thread.Sleep(200);

    var retried = reader.ReadMemoryMappedFile(snapshot.FileName);
    Assert.NotSame(heldOff.Readings[0].LabelOrig, retried.Readings[0].LabelOrig);
  }

  [Fact]
  public void Read_WithStalenessCheckDisabled_ShouldKeepUsingTheCachedSection()
  {
    using var snapshot = SharedMemorySnapshot.Publish();
    using var reader = new SharedMemoryReader(new SharedMemoryReaderOptions { StalenessTimeout = TimeSpan.Zero });

    var first = reader.ReadMemoryMappedFile(snapshot.FileName);
    snapshot.PatchPollTime(0);
    var second = reader.ReadMemoryMappedFile(snapshot.FileName);

    // Nothing was reopened, so the strings survive
    Assert.Same(first.Readings[0].LabelOrig, second.Readings[0].LabelOrig);
  }

  [Fact]
  public void TryRead_WhenTheSectionIsTornDown_ShouldReturnFalseAndReadAgainOnceItIsBack()
  {
    using var snapshot = SharedMemorySnapshot.Publish();

    Assert.True(_reader.TryReadMemoryMappedFile(snapshot.FileName, out var first));
    Assert.Equal(FullReadingCount, first.Readings.Length);

    // HWiNFO overwrites the signature while it tears the section down, which is the same "no data"
    // condition as a section that isn't there at all
    snapshot.PatchSignature(0xDEADBEEF);
    Assert.False(_reader.TryReadMemoryMappedFile(snapshot.FileName, out var torn));
    Expect.NoReadings(torn);

    // The failed read released the cached section instead of poisoning it, so the reader picks the
    // section up again as soon as it carries a valid header
    snapshot.PatchSignature(SharedMemorySnapshot.Signature);
    snapshot.PatchPollTimeToNow();
    Assert.True(_reader.TryReadMemoryMappedFile(snapshot.FileName, out var second));
    Assert.Equal(FullReadingCount, second.Readings.Length);
    Assert.Equal<SensorReading>(first.Readings, second.Readings);
  }

  [Fact]
  public void Read_WithReuseUnchangedPolls_ShouldReadAgainOnceThePollTimeMoves()
  {
    using var snapshot = SharedMemorySnapshot.Publish();
    using var reader = new SharedMemoryReader(new SharedMemoryReaderOptions { ReuseUnchangedPolls = true });

    var first = reader.ReadMemoryMappedFile(snapshot.FileName);
    var second = reader.ReadMemoryMappedFile(snapshot.FileName);
    Assert.True(first.Readings == second.Readings);

    snapshot.PatchPollTime(first.PollTime.ToUnixTimeSeconds() + 1);
    var third = reader.ReadMemoryMappedFile(snapshot.FileName);

    Assert.False(second.Readings == third.Readings);
    Assert.Equal(first.PollTime.AddSeconds(1), third.PollTime);
    Assert.Equal<SensorReading>(first.Readings, third.Readings);
  }
}
