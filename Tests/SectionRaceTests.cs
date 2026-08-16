using System.Collections.Concurrent;

namespace Hwinfo.SharedMemory.Tests;

/// <summary>
/// Tests the reader against a section that moves while it is being read - the case the retry loop and
/// the header-after-copy check exist for.
/// <para>
/// HWiNFO republishes its readings on every poll: it drops the reading count to 0 and counts it back
/// up while it refills the section, a window of tens of microseconds every couple of seconds. Without
/// the mutex a read can land in it. These tests reproduce that by rewriting the published section from
/// a second thread while the reader works.
/// </para>
/// </summary>
public class SectionRaceTests
{
  private static readonly int FullReadingCount = (int)SharedMemorySnapshot.ReadingCount;

  [Fact]
  public void Read_WhileTheSectionNeverSettles_ShouldGiveUpAfterItsAttempts()
  {
    using var snapshot = SharedMemorySnapshot.Publish();
    using var reader = new SharedMemoryReader();

    // A poll time that only ever moves forward means the header can never describe what was copied,
    // so every attempt is discarded and the reader runs out of them
    using var mutator = SectionMutator.MovingPollTime(snapshot);

    var exception = Assert.Throws<InvalidDataException>(() => reader.ReadMemoryMappedFile(snapshot.FileName));

    Assert.Contains("read attempts", exception.Message);
  }

  [Fact]
  public void Read_AfterTheSectionSettles_ShouldSucceedAgain()
  {
    using var snapshot = SharedMemorySnapshot.Publish();
    using var reader = new SharedMemoryReader();

    using (SectionMutator.MovingPollTime(snapshot))
    {
      Assert.Throws<InvalidDataException>(() => reader.ReadMemoryMappedFile(snapshot.FileName));
    }

    snapshot.PatchPollTimeToNow();
    var result = reader.ReadMemoryMappedFile(snapshot.FileName);

    // A discarded attempt leaves the byte buffers and the decoded strings untouched, so the reader
    // isn't left half-updated by the reads that failed
    Assert.Equal(FullReadingCount, result.Readings.Length);
    Assert.Equal("Virtual Memory Committed", result.Readings[0].LabelOrig);
    Assert.All(result.Readings, reading => Assert.NotEmpty(reading.Sensor.NameOrig));
  }

  [Fact]
  public void Read_WhileTheReadingCountIsRepublished_ShouldOnlyReturnSetsTheSectionDeclared()
  {
    using var snapshot = SharedMemorySnapshot.Publish();
    using var reader = new SharedMemoryReader();

    using var mutator = SectionMutator.FlippingReadingCount(snapshot, (uint)FullReadingCount);

    var counts = new HashSet<int>();
    var failures = 0;
    for (var attempt = 0; attempt < 200; attempt++)
    {
      try
      {
        var result = reader.ReadMemoryMappedFile(snapshot.FileName);
        counts.Add(result.Readings.Length);
        // A truncated or mixed copy would join readings to sensors that were never read
        Assert.All(result.Readings, reading => Assert.NotEmpty(reading.Sensor.NameOrig));
      }
      catch (InvalidDataException)
      {
        // The section moved during all five attempts, which is a legitimate outcome here
        failures++;
      }
    }

    Assert.True(failures < 200, "every single read failed, so nothing was actually verified");
    Assert.All(counts, count => Assert.True(count == 0 || count == FullReadingCount, $"returned {count} readings"));
  }

  [Fact]
  public void Read_FromMultipleThreads_ShouldReturnCompleteResultsToEveryCaller()
  {
    using var snapshot = SharedMemorySnapshot.Publish();
    using var reader = new SharedMemoryReader();

    var counts = new ConcurrentBag<int>();
    Parallel.For(0, 200, _ => counts.Add(reader.ReadMemoryMappedFile(snapshot.FileName).Readings.Length));

    Assert.Equal(200, counts.Count);
    Assert.All(counts, count => Assert.Equal(FullReadingCount, count));
  }

  /// <summary>
  /// Rewrites a published section from a background thread until it is disposed.
  /// </summary>
  private sealed class SectionMutator : IDisposable
  {
    private readonly Thread _thread;
    private volatile bool _stop;
    private long _iterations;

    private SectionMutator(Action<long> step)
    {
      _thread = new Thread(() =>
      {
        while (!_stop)
        {
          step(Interlocked.Increment(ref _iterations));
        }
      }) { IsBackground = true, Name = nameof(SectionMutator) };
      _thread.Start();
      WaitUntilRunning();
    }

    /// <summary>
    /// Moves the poll time forward without ever repeating a value. It starts at now, so the section
    /// never looks stale, and it only ever grows, so two reads of the header can't agree.
    /// </summary>
    internal static SectionMutator MovingPollTime(SharedMemorySnapshot snapshot)
    {
      var start = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
      return new SectionMutator(iteration => snapshot.PatchPollTime(start + iteration));
    }

    /// <summary>
    /// Alternates the reading count between none and all of them, the way HWiNFO does while it
    /// republishes the section.
    /// </summary>
    internal static SectionMutator FlippingReadingCount(SharedMemorySnapshot snapshot, uint full) =>
      new(iteration => snapshot.PatchReadingCount((iteration & 1) == 0 ? 0 : full));

    /// <summary>
    /// Blocks until the thread is demonstrably running, so a test never races its own setup.
    /// </summary>
    private void WaitUntilRunning()
    {
      var deadline = Environment.TickCount64 + 10_000;
      while (Interlocked.Read(ref _iterations) < 1000)
      {
        if (Environment.TickCount64 > deadline) throw new TimeoutException("the mutator thread never got going");
        Thread.Sleep(1);
      }
    }

    public void Dispose()
    {
      _stop = true;
      _thread.Join(10_000);
    }
  }
}
