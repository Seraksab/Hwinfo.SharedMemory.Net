namespace Hwinfo.SharedMemory.Tests;

/// <summary>
/// Integration tests against a live HWiNFO instance with Shared Memory Support enabled.
/// <see cref="ReadRemote_0_ShouldReturnSensorValues"/> additionally requires a remote connection at index 0.
/// Exclude them with <c>dotnet test --filter "Category!=RequiresHwinfo"</c>.
/// </summary>
[Trait("Category", "RequiresHwinfo")]
public class LiveHwinfoTests
{
  private readonly SharedMemoryReader _reader = new();

  [Fact]
  public void ReadLocal_ShouldReturnSensorValues()
  {
    var sensorValues = _reader.ReadLocal();
    Assert.NotNull(sensorValues);
    Assert.True(sensorValues.Count > 0);
  }

  [Fact]
  public void ReadRemote_0_ShouldReturnSensorValues()
  {
    var sensorValues = _reader.ReadRemote(0);
    Assert.NotNull(sensorValues);
    Assert.True(sensorValues.Count > 0);
  }

  [Fact]
  public void ReadRemote_1_ThrowsFileNotFound()
  {
    Assert.Throws<FileNotFoundException>(() => _reader.ReadRemote(1));
  }
}
