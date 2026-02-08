namespace Hwinfo.SharedMemory;

/// <summary>
/// A single sensor reading.
/// </summary>
public readonly record struct SensorReading(
  uint ReadingId,          // A unique ID of the reading within a particular sensor
  uint SensorIndex,        // This is the index of sensor in the Sensors[] array to which this reading belongs to
  SensorType ReadingType,  // Type of sensor reading
  string LabelOrig,        // Original label (e.g. "Chassis2 Fan")
  string LabelUser,        // Label displayed, which might have been renamed by user
  string Unit,
  double Value,
  double ValueMin,
  double ValueMax,
  double ValueAvg,
  uint SensorId,           // A unique Sensor ID
  uint SensorInstance,     // The instance of the sensor (together with dwSensorID forms a unique ID)
  string SensorNameOrig,   // Original sensor name
  string SensorNameUser    // Sensor name displayed, which might have been renamed by user
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