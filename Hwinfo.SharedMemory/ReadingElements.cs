namespace Hwinfo.SharedMemory;

/// <summary>
/// A single sensor reading.
/// </summary>
public readonly record struct SensorReading(
  uint ReadingId,          // A unique ID of the reading within a particular sensor
  SensorType ReadingType,  // Type of sensor reading
  string LabelOrig,        // Original label (e.g. "Chassis2 Fan")
  string LabelUser,        // Label displayed, which might have been renamed by user
  string Unit,
  double Value,
  double ValueMin,
  double ValueMax,
  double ValueAvg,
  Sensor Sensor            // The sensor this reading belongs to
);

/// <summary>
/// The sensor a <see cref="SensorReading"/> belongs to.
/// One instance is shared by all readings of that sensor and is kept across reads for as long as
/// HWiNFO reports it unchanged.
/// </summary>
public sealed record Sensor(
  uint Id,          // A unique Sensor ID
  uint Instance,    // The instance of the sensor (together with Id forms a unique ID)
  string NameOrig,  // Original sensor name
  string NameUser   // Sensor name displayed, which might have been renamed by user
);

/// <summary>
/// The sensor type.
/// </summary>
public enum SensorType
{
  SensorTypeNone = 0,
  SensorTypeTemp,
  SensorTypeVolt,
  SensorTypeFan,
  SensorTypeCurrent,
  SensorTypePower,
  SensorTypeClock,
  SensorTypeUsage,
  SensorTypeOther
};
