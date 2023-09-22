namespace Hwinfo.SharedMemory.Tests;

public class UnitTests
{
  [Fact]
  public void SharedMemoryReader_ShouldReturnSensorValues()
  {
    var reader = new SharedMemoryReader();
    var sensorValues = reader.Read().ToList();

    Assert.NotNull(sensorValues);
    Assert.True(sensorValues.Count > 0);
  }
}