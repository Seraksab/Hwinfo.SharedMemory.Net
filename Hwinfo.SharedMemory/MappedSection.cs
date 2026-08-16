using System;
using System.Collections.Immutable;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Hwinfo.SharedMemory;

/// <summary>
/// One mapped shared memory section together with everything that is reused between reads: the
/// memory mapped file and its view accessor, the copies of both element sections and the strings
/// decoded from them.
/// <para>
/// Reads copy the elements out of the mapping before parsing them. HWiNFO's section behaves like
/// uncached memory - it reads at a fraction of the speed of ordinary memory and touching the same
/// bytes twice costs twice as much - so every byte is fetched exactly once, and only the bytes of
/// each element that <see cref="SmLayout"/> covers.
/// </para>
/// </summary>
internal sealed class MappedSection : IDisposable
{
  private readonly string _fileName;
  private readonly bool _reuseUnchangedPolls;
  private readonly MemoryMappedFile _mmf;
  private readonly MemoryMappedViewAccessor _accessor;
  private readonly SafeMemoryMappedViewHandle _handle;
  private readonly ulong _mappedLength;

  // The two sections, copied element by element and packed back to back at SmLayout's element size.
  // The previous copy is kept to detect which strings have to be decoded again.
  private byte[] _sensorBytes = [];
  private byte[] _previousSensorBytes = [];
  private byte[] _readingBytes = [];
  private byte[] _previousReadingBytes = [];

  // Decoded per element slot and reused while the bytes behind them don't change
  private Sensor[] _sensors = [];
  private string[] _labelsOrig = [];
  private string[] _labelsUser = [];
  private string[] _units = [];

  private int _sensorCount = -1;
  private int _readingCount = -1;
  private bool _decodedStringsValid;

  private long _lastPollTime = -1;
  private ImmutableArray<SensorReading> _lastReadings;

  // The sensors as they are handed out: a copy of _sensors, taken whenever one of them changed, so
  // that a result already given to a caller never changes underneath them
  private ImmutableArray<Sensor> _sensorsView;

  // "HWiS" in little-endian byte order.
  // HWiNFO overwrites it (e.g. with 0xDEADBEEF) while the section is being torn down
  private const uint HWiNfoSensorsSignature = 0x53695748;

  // The range DateTimeOffset.FromUnixTimeSeconds accepts, i.e. the years 0001 to 9999
  private const long MinUnixSeconds = -62135596800;
  private const long MaxUnixSeconds = 253402300799;

  /// <exception cref="FileNotFoundException">The shared memory file does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">Access is invalid for the shared memory file.</exception>
  internal MappedSection(string fileName, bool reuseUnchangedPolls)
  {
    _fileName = fileName;
    _reuseUnchangedPolls = reuseUnchangedPolls;
    _mmf = MemoryMappedFile.OpenExisting(fileName, MemoryMappedFileRights.Read);
    try
    {
      _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
    }
    catch
    {
      _mmf.Dispose();
      throw;
    }

    _handle = _accessor.SafeMemoryMappedViewHandle;
    _mappedLength = _handle.ByteLength;
  }

  /// <summary>
  /// Reads the header and returns whether it carries a valid HWiNFO signature.
  /// </summary>
  internal bool TryReadHeader(out SmSensorsSharedMem2 header)
  {
    if (_mappedLength < SmLayout.HeaderSize)
    {
      header = default;
      return false;
    }

    _accessor.Read(0, out header);
    return header.Signature == HWiNfoSensorsSignature;
  }

  /// <summary>
  /// Reads the sensor readings described by the given header.
  /// </summary>
  /// <returns>
  /// <c>false</c> if HWiNFO updated the section while it was being read, in which case nothing was
  /// parsed and the caller should read the header again and retry.
  /// </returns>
  /// <exception cref="InvalidDataException">The header describes a section that cannot be parsed.</exception>
  internal bool TryRead(in SmSensorsSharedMem2 header, out SensorReadings readings)
  {
    var pollTime = ToPollTime(header.PollTime);
    var sensorCount = ValidateSection(
      header.SensorSection_Offset,
      header.SensorSection_SizeOfElement,
      header.SensorSection_NumElements,
      SmLayout.SensorElementSize,
      "sensor"
    );
    var readingCount = ValidateSection(
      header.ReadingSection_Offset,
      header.ReadingSection_SizeOfElement,
      header.ReadingElements_NumElements,
      SmLayout.ReadingElementSize,
      "reading"
    );

    // Nothing in the section has changed since the last read, so neither has its result. The result
    // is handed out as an immutable collection, so the same instance can be shared by every caller.
    if (_reuseUnchangedPolls
        && !_lastReadings.IsDefault
        && !_sensorsView.IsDefault
        && _lastPollTime == header.PollTime
        && _sensorCount == sensorCount
        && _readingCount == readingCount)
    {
      readings = new SensorReadings(pollTime, _lastReadings, _sensorsView);
      return true;
    }

    PrepareBuffers(sensorCount, readingCount);
    CopySection(
      header.SensorSection_Offset, (int)header.SensorSection_SizeOfElement, sensorCount,
      SmLayout.SensorElementSize, _sensorBytes
    );
    CopySection(
      header.ReadingSection_Offset, (int)header.ReadingSection_SizeOfElement, readingCount,
      SmLayout.ReadingElementSize, _readingBytes
    );

    // HWiNFO republishes the readings on every poll: it drops the number of reading elements to 0
    // and counts it back up while it refills the section. Without the mutex a read can land in that
    // window and copy a truncated or mixed set, so the header has to still describe what was copied.
    if (!TryReadHeader(out var afterRead) || !header.Describes(afterRead))
    {
      readings = default;
      return false;
    }

    // Parse fills _sensorsView along with the readings, so it is never default once it returns
    readings = new SensorReadings(pollTime, Parse(sensorCount, readingCount), _sensorsView);
    _lastPollTime = header.PollTime;
    return true;
  }

  /// <inheritdoc />
  public void Dispose()
  {
    _accessor.Dispose();
    _mmf.Dispose();
  }

  /// <summary>
  /// Checks a section against the mapped length and returns its number of elements.
  /// </summary>
  /// <exception cref="InvalidDataException">The section does not fit the mapping or its elements are too small.</exception>
  private int ValidateSection(uint offset, uint sizeOfElement, uint numElements, int minimumSize, string name)
  {
    if (sizeOfElement < minimumSize)
    {
      throw new InvalidDataException(
        $"The {name} elements of '{_fileName}' are {sizeOfElement} bytes, expected at least {minimumSize}."
      );
    }

    var end = offset + (ulong)sizeOfElement * numElements;
    if (end > _mappedLength)
    {
      throw new InvalidDataException(
        $"The {name} section of '{_fileName}' ends at byte {end}, beyond the {_mappedLength} bytes mapped."
      );
    }

    if (numElements > int.MaxValue / minimumSize)
    {
      throw new InvalidDataException($"'{_fileName}' declares {numElements} {name} elements, which is too many.");
    }

    return (int)numElements;
  }

  /// <summary>
  /// Grows the buffers and the string caches to the given element counts, dropping everything cached
  /// for the previous shape.
  /// </summary>
  private void PrepareBuffers(int sensorCount, int readingCount)
  {
    if (sensorCount == _sensorCount && readingCount == _readingCount) return;

    _sensorBytes = new byte[sensorCount * SmLayout.SensorElementSize];
    _previousSensorBytes = new byte[_sensorBytes.Length];
    _readingBytes = new byte[readingCount * SmLayout.ReadingElementSize];
    _previousReadingBytes = new byte[_readingBytes.Length];

    _sensors = new Sensor[sensorCount];
    _labelsOrig = new string[readingCount];
    _labelsUser = new string[readingCount];
    _units = new string[readingCount];

    _sensorCount = sensorCount;
    _readingCount = readingCount;
    _decodedStringsValid = false;
    _lastReadings = default;
    _sensorsView = default;
    _lastPollTime = -1;
  }

  /// <summary>
  /// Copies the leading <paramref name="copyLength"/> bytes of every element of a section into
  /// <paramref name="destination"/>, packed back to back.
  /// </summary>
  private void CopySection(uint offset, int sizeOfElement, int count, int copyLength, byte[] destination)
  {
    // HWiNFO's elements are usually larger than the part of them we know, but if they ever match
    // the whole section can be fetched in one go
    if (sizeOfElement == copyLength)
    {
      _handle.ReadSpan<byte>(offset, destination.AsSpan(0, count * copyLength));
      return;
    }

    for (var idx = 0; idx < count; idx++)
    {
      _handle.ReadSpan<byte>(
        offset + (ulong)((long)idx * sizeOfElement),
        destination.AsSpan(idx * copyLength, copyLength)
      );
    }
  }

  /// <summary>
  /// Parses both copied sections and joins every reading to its sensor.
  /// </summary>
  /// <exception cref="InvalidDataException">A reading refers to a sensor that was not read.</exception>
  private ImmutableArray<SensorReading> Parse(int sensorCount, int readingCount)
  {
    // Everything cached describes the previous copy, so it may only be reused where the incoming copy
    // matches it. Clearing the flag up front means a parse that throws half way through leaves nothing
    // half-updated behind: the next read simply decodes everything again.
    var reusable = _decodedStringsValid;
    _decodedStringsValid = false;

    ReadOnlySpan<byte> sensorBytes = _sensorBytes;
    ReadOnlySpan<byte> previousSensorBytes = _previousSensorBytes;
    var sensorsChanged = false;
    for (var idx = 0; idx < sensorCount; idx++)
    {
      var elementOffset = idx * SmLayout.SensorElementSize;
      var element = sensorBytes.Slice(elementOffset, SmLayout.SensorElementSize);
      var previous = previousSensorBytes.Slice(elementOffset, SmLayout.SensorElementSize);

      // A sensor element holds nothing but its id and its names, so an unchanged element means an
      // unchanged sensor and the readings can keep pointing at the instance they already share
      if (reusable && element.SequenceEqual(previous)) continue;

      sensorsChanged = true;
      _sensors[idx] = new Sensor(
        Id: MemoryMarshal.Read<uint>(element[SmLayout.SensorId..]),
        Instance: MemoryMarshal.Read<uint>(element[SmLayout.SensorInstance..]),
        NameOrig: Decode(element.Slice(SmLayout.SensorNameOrig, SmLayout.StringLength)),
        NameUser: Decode(element.Slice(SmLayout.SensorNameUser, SmLayout.StringLength))
      );
    }

    ReadOnlySpan<byte> readingBytes = _readingBytes;
    ReadOnlySpan<byte> previousReadingBytes = _previousReadingBytes;
    var readings = new SensorReading[readingCount];
    for (var idx = 0; idx < readingCount; idx++)
    {
      var elementOffset = idx * SmLayout.ReadingElementSize;
      var element = readingBytes.Slice(elementOffset, SmLayout.ReadingElementSize);

      var sensorIndex = MemoryMarshal.Read<uint>(element[SmLayout.ReadingSensorIndex..]);
      if (sensorIndex >= (uint)sensorCount)
      {
        throw new InvalidDataException(
          $"Reading {idx} refers to sensor {sensorIndex}, but only {sensorCount} sensors were read."
        );
      }

      // The labels and the unit are one adjacent block and practically never change, unlike the
      // values right behind them, so they are only decoded again when their bytes actually differ
      var strings = element.Slice(SmLayout.ReadingLabelOrig, SmLayout.ReadingStringsLength);
      var previousStrings = previousReadingBytes.Slice(
        elementOffset + SmLayout.ReadingLabelOrig, SmLayout.ReadingStringsLength
      );
      if (!reusable || !strings.SequenceEqual(previousStrings))
      {
        _labelsOrig[idx] = Decode(element.Slice(SmLayout.ReadingLabelOrig, SmLayout.StringLength));
        _labelsUser[idx] = Decode(element.Slice(SmLayout.ReadingLabelUser, SmLayout.StringLength));
        _units[idx] = Decode(element.Slice(SmLayout.ReadingUnit, SmLayout.UnitLength));
      }

      readings[idx] = new SensorReading(
        ReadingId: MemoryMarshal.Read<uint>(element[SmLayout.ReadingId..]),
        ReadingType: ToSensorType(MemoryMarshal.Read<uint>(element[SmLayout.ReadingType..])),
        LabelOrig: _labelsOrig[idx],
        LabelUser: _labelsUser[idx],
        Unit: _units[idx],
        Value: MemoryMarshal.Read<double>(element[SmLayout.ReadingValue..]),
        ValueMin: MemoryMarshal.Read<double>(element[SmLayout.ReadingValueMin..]),
        ValueMax: MemoryMarshal.Read<double>(element[SmLayout.ReadingValueMax..]),
        ValueAvg: MemoryMarshal.Read<double>(element[SmLayout.ReadingValueAvg..]),
        Sensor: _sensors[sensorIndex]
      );
    }

    // What was just parsed becomes what the next read compares against, and the buffer it replaces is
    // the one the next read copies into. Swapping only here keeps the two consistent: an attempt that
    // is discarded because HWiNFO updated underneath it leaves the strings and their bytes untouched.
    (_sensorBytes, _previousSensorBytes) = (_previousSensorBytes, _sensorBytes);
    (_readingBytes, _previousReadingBytes) = (_previousReadingBytes, _readingBytes);
    _decodedStringsValid = true;

    // A copy, because _sensors keeps being reused: handing out the array itself would let a later
    // read change a result a caller is still holding. It is only taken when a sensor actually
    // changed, which after the first read practically never happens.
    // The Clone() is load-bearing: AsImmutableArray wraps the array it is given rather than copying
    // it, so dropping it would publish the live _sensors under an immutable facade.
    if (sensorsChanged || _sensorsView.IsDefault)
    {
      _sensorsView = ImmutableCollectionsMarshal.AsImmutableArray((Sensor[])_sensors.Clone());
    }

    // Wrapping rather than copying is safe here: 'readings' was allocated by this call, is never
    // written again, and nothing else holds a reference to it.
    var result = ImmutableCollectionsMarshal.AsImmutableArray(readings);
    // Only kept when it can actually be handed out again, so the last result isn't held alive for
    // readers that reparse every time anyway
    if (_reuseUnchangedPolls) _lastReadings = result;
    return result;
  }

  /// <summary>
  /// Converts the header's poll time, a unix time in seconds, to a <see cref="DateTimeOffset"/>.
  /// </summary>
  /// <exception cref="InvalidDataException">The poll time is not a representable point in time.</exception>
  private DateTimeOffset ToPollTime(long pollTime)
  {
    if (pollTime < MinUnixSeconds || pollTime > MaxUnixSeconds)
    {
      throw new InvalidDataException($"'{_fileName}' reports the poll time {pollTime}, which is not a valid date.");
    }

    return DateTimeOffset.FromUnixTimeSeconds(pollTime);
  }

  /// <summary>
  /// Maps the raw reading type to <see cref="SensorType"/>.
  /// The field is four bytes of shared memory that could hold anything, so a type this library doesn't know
  /// becomes <see cref="SensorType.Other"/>.
  /// </summary>
  private static SensorType ToSensorType(uint type)
  {
    return type <= (uint)SensorType.Other ? (SensorType)type : SensorType.Other;
  }

  /// <summary>
  /// Decodes a NUL terminated fixed-length string field.
  /// <para>
  /// HWiNFO writes these as CP1252, of which Latin1 is the subset that covers everything appearing in
  /// sensor names, labels and units - notably the degree sign. ASCII would not.
  /// </para>
  /// </summary>
  private static string Decode(ReadOnlySpan<byte> field)
  {
    var terminator = field.IndexOf((byte)0);
    if (terminator >= 0) field = field[..terminator];
    return field.IsEmpty ? string.Empty : Encoding.Latin1.GetString(field);
  }
}