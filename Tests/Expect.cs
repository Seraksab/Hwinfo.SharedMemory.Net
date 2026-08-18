namespace Hwinfo.SharedMemory.Tests;

/// <summary>
/// Assertions shared by the test classes.
/// </summary>
internal static class Expect
{
  /// <summary>
  /// Asserts that a result is the "nothing to read" one a <c>TryRead</c> hands out when it returns
  /// <c>false</c>: <em>empty</em> rather than <c>default</c>, so a caller that overlooks the
  /// <c>false</c> iterates nothing instead of hitting the <see cref="NullReferenceException"/> an
  /// uninitialized <see cref="System.Collections.Immutable.ImmutableArray{T}"/> throws.
  /// </summary>
  internal static void NoReadings(SensorReadings readings)
  {
    Assert.False(readings.Readings.IsDefault);
    Assert.False(readings.Sensors.IsDefault);
    Assert.Empty(readings.Readings);
    Assert.Empty(readings.Sensors);
    Assert.Equal(default, readings.PollTime);
  }
}
