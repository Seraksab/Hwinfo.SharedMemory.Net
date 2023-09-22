namespace Hwinfo.SharedMemory;

/// <summary>
/// A single sensor reading.
/// </summary>
public readonly record struct SensorReading(
  uint Id,
  uint Index,
  SensorType Type,
  string LabelOrig,
  string LabelUser,
  string Unit,
  double Value,
  double ValueMin,
  double ValueMax,
  double ValueAvg,
  uint GroupId,
  uint GroupInstanceId,
  string GroupLabelUser,
  string GroupLabelOrig
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