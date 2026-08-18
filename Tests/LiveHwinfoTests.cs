namespace Hwinfo.SharedMemory.Tests;

/// <summary>
/// Integration tests against a live HWiNFO instance with Shared Memory Support enabled.
/// <see cref="ReadRemote_0_ShouldReturnSensorValues"/> additionally requires a remote connection at index 0.
/// Exclude them with <c>dotnet test --filter "Category!=RequiresHwinfo"</c>.
/// </summary>
[Trait("Category", "RequiresHwinfo")]
public class LiveHwinfoTests : IDisposable
{
  private readonly SharedMemoryReader _reader = new();

  public void Dispose() => _reader.Dispose();

  [Fact]
  public void ReadLocal_ShouldReturnSensorValues()
  {
    var sensorValues = _reader.ReadLocal();
    Assert.True(sensorValues.Readings.Length > 0);
    Assert.True(sensorValues.Sensors.Length > 0);
    Assert.NotEqual(default, sensorValues.PollTime);
  }

  [Fact]
  public void TryReadLocal_ShouldReturnSensorValues()
  {
    Assert.True(_reader.TryReadLocal(out var sensorValues));
    Assert.True(sensorValues.Readings.Length > 0);
    Assert.True(sensorValues.Sensors.Length > 0);
    Assert.NotEqual(default, sensorValues.PollTime);
  }

  [Fact]
  public void ReadLocal_WithRequireMutex_ShouldReturnSensorValues()
  {
    // The one test that reads with HWiNFO's mutex actually held, so it doubles as the answer to
    // whether this process can obtain it at all. A failure here means the mutex isn't available to it
    // - typically because HWiNFO runs elevated and this doesn't - not that the read is broken.
    using var reader = new SharedMemoryReader(new SharedMemoryReaderOptions { RequireMutex = true });

    var sensorValues = reader.ReadLocal();

    Assert.True(sensorValues.Readings.Length > 0);
    Assert.True(sensorValues.Sensors.Length > 0);
  }

  [Fact]
  public void ReadRemote_0_ShouldReturnSensorValues()
  {
    var sensorValues = _reader.ReadRemote(0);
    Assert.True(sensorValues.Readings.Length > 0);
  }

  [Fact]
  public void ReadRemote_1_ThrowsFileNotFound()
  {
    Assert.Throws<FileNotFoundException>(() => _reader.ReadRemote(1));
  }
}