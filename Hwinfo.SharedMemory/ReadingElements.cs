using System;
using System.Collections.Immutable;

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
  ImmutableArray<SensorReading> Readings,
  ImmutableArray<Sensor> Sensors
);

/// <summary>
/// A single sensor reading.
/// </summary>
/// <param name="ReadingId">A unique ID of the reading within its <paramref name="Sensor"/>.</param>
/// <param name="ReadingType">What the reading measures.</param>
/// <param name="LabelOrig">The original label, e.g. "Chassis2 Fan".</param>
/// <param name="LabelUser">The label displayed, which the user might have renamed.</param>
/// <param name="Unit">The unit the values are in, e.g. "°C". Empty if the reading has none.</param>
/// <param name="Value">The current value.</param>
/// <param name="ValueMin">The lowest value HWiNFO has seen since it started monitoring.</param>
/// <param name="ValueMax">The highest value HWiNFO has seen since it started monitoring.</param>
/// <param name="ValueAvg">The average value HWiNFO has seen since it started monitoring.</param>
/// <param name="Sensor">The sensor this reading belongs to.</param>
public readonly record struct SensorReading(
  uint ReadingId,
  SensorType ReadingType,
  string LabelOrig,
  string LabelUser,
  string Unit,
  double Value,
  double ValueMin,
  double ValueMax,
  double ValueAvg,
  Sensor Sensor
);

/// <summary>
/// The sensor a <see cref="SensorReading"/> belongs to.
/// One instance is shared by all readings of that sensor and is kept across reads for as long as
/// HWiNFO reports it unchanged.
/// </summary>
/// <param name="Id">A unique sensor ID, together with <paramref name="Instance"/>.</param>
/// <param name="Instance">The instance of the sensor, together with <paramref name="Id"/>.</param>
/// <param name="NameOrig">The original sensor name.</param>
/// <param name="NameUser">The sensor name displayed, which the user might have renamed.</param>
public sealed record Sensor(
  uint Id,
  uint Instance,
  string NameOrig,
  string NameUser
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
}