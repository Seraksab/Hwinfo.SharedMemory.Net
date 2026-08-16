using System;
using System.Collections.Generic;

namespace Hwinfo.SharedMemory;

/// <summary>
/// The result of one read: the readings HWiNFO published, and when it published them.
/// </summary>
/// <param name="PollTime">
/// When HWiNFO last polled its sensors. HWiNFO reports this in whole seconds, so it only changes
/// once a second even if it polls more often.
/// </param>
/// <param name="Readings">The sensor readings of that poll.</param>
/// <param name="Sensors">
/// Every sensor HWiNFO published, in the order it published them. This includes sensors that have no
/// readings of their own and therefore appear nowhere in <paramref name="Readings"/>.
/// </param>
public readonly record struct SensorReadings(
  DateTimeOffset PollTime,
  IReadOnlyList<SensorReading> Readings,
  IReadOnlyList<Sensor> Sensors
);

/// <summary>
/// A single sensor reading.
/// </summary>
public readonly record struct SensorReading(
  uint ReadingId, // A unique ID of the reading within a particular sensor
  SensorType ReadingType, // Type of sensor reading
  string LabelOrig, // Original label (e.g. "Chassis2 Fan")
  string LabelUser, // Label displayed, which might have been renamed by user
  string Unit,
  double Value,
  double ValueMin,
  double ValueMax,
  double ValueAvg,
  Sensor Sensor // The sensor this reading belongs to
);

/// <summary>
/// The sensor a <see cref="SensorReading"/> belongs to.
/// One instance is shared by all readings of that sensor and is kept across reads for as long as
/// HWiNFO reports it unchanged.
/// </summary>
public sealed record Sensor(
  uint Id, // A unique Sensor ID
  uint Instance, // The instance of the sensor (together with Id forms a unique ID)
  string NameOrig, // Original sensor name
  string NameUser // Sensor name displayed, which might have been renamed by user
);

/// <summary>
/// What a <see cref="SensorReading"/> measures.
/// A reading type HWiNFO reports that isn't one of these is mapped to <see cref="Other"/>, so the
/// value is always a defined member of this enum.
/// </summary>
public enum SensorType
{
  /// <summary>No type reported.</summary>
  None = 0,

  /// <summary>A temperature, e.g. in °C.</summary>
  Temp,

  /// <summary>A voltage, e.g. in V.</summary>
  Volt,

  /// <summary>A fan speed, e.g. in RPM.</summary>
  Fan,

  /// <summary>An electric current, e.g. in A.</summary>
  Current,

  /// <summary>A power draw, e.g. in W.</summary>
  Power,

  /// <summary>A clock frequency, e.g. in MHz.</summary>
  Clock,

  /// <summary>A usage or load, e.g. in %.</summary>
  Usage,

  /// <summary>Anything else, including reading types unknown to this library.</summary>
  Other
};