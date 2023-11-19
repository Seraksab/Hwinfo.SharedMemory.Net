namespace Hwinfo.SharedMemory.Tests;

public class UnitTests
{
  private readonly SharedMemoryReader _reader = new();

  [Fact]
  public void SharedMemoryReader_ReadLocal_ShouldReturnSensorValues()
  {
    var sensorValues = _reader.ReadLocal().ToList();
    Assert.NotNull(sensorValues);
    Assert.True(sensorValues.Count > 0);
  }

  [Fact]
  public void SharedMemoryReader_ReadRemote_0_ShouldReturnSensorValues()
  {
    var sensorValues = _reader.ReadRemote(0).ToList();
    Assert.NotNull(sensorValues);
    Assert.True(sensorValues.Count > 0);
  }

  [Fact]
  public void SharedMemoryReader_ReadRemote_1_ThrowsFileNotFound()
  {
    Assert.Throws<FileNotFoundException>(() => _reader.ReadRemote(1));
  }
}