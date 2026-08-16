using System.IO.MemoryMappedFiles;
using System.Text;

namespace Hwinfo.SharedMemory.Tests;

/// <summary>
/// A snapshot of the HWiNFO shared memory, published under a session local name so tests can read
/// real sensor data without a running HWiNFO instance.
/// <para>
/// The <c>Patch*</c> members write into the published section after the fact. A reader that has
/// already mapped it sees the change on its next read, which is what makes the paths that only run
/// on a <em>cached</em> section reachable: the buffer reallocation on a changed element count, the
/// per-element string and sensor reuse, and the retry when the section moves underneath a read.
/// </para>
/// </summary>
internal sealed class SharedMemorySnapshot : IDisposable
{
  // Offsets within the SmSensorsSharedMem2 header
  internal const int SignatureOffset = 0;
  internal const int VersionOffset = 4;
  internal const int PollTimeOffset = 12;
  internal const int SensorSectionOffsetOffset = 20;
  internal const int SensorSectionSizeOfElementOffset = 24;
  internal const int SensorSectionNumElementsOffset = 28;
  internal const int ReadingSectionOffsetOffset = 32;
  internal const int ReadingSectionSizeOfElementOffset = 36;
  internal const int ReadingElementsNumElementsOffset = 40;

  // Offsets within a reading element
  internal const int ReadingTypeOffset = 0;
  internal const int ReadingSensorIndexOffset = 4;

  private static readonly byte[] Snapshot =
    File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "hwinfo-sm2-snapshot.bin"));

  /// <summary>
  /// The raw bytes of the snapshot, as dumped from a live section.
  /// </summary>
  internal static ReadOnlySpan<byte> Bytes => Snapshot;

  /// <summary>The number of reading elements the unmodified snapshot declares.</summary>
  internal static uint ReadingCount => ReadUInt32(Snapshot, ReadingElementsNumElementsOffset);

  /// <summary>The number of sensor elements the unmodified snapshot declares.</summary>
  internal static uint SensorCount => ReadUInt32(Snapshot, SensorSectionNumElementsOffset);

  private readonly MemoryMappedFile _mmf;
  private readonly MemoryMappedViewAccessor _accessor;
  private readonly uint _sensorSectionOffset;
  private readonly uint _sensorElementSize;

  /// <summary>
  /// The name of the memory mapped file the snapshot is published under.
  /// </summary>
  internal string FileName { get; }

  private SharedMemorySnapshot(byte[] data)
  {
    FileName = $"Local\\Hwinfo.SharedMemory.Tests_{Guid.NewGuid():N}";
    _mmf = MemoryMappedFile.CreateNew(FileName, data.Length);
    _accessor = _mmf.CreateViewAccessor();
    _accessor.WriteArray(0, data, 0, data.Length);
    _accessor.Flush();

    _sensorSectionOffset = ReadUInt32(data, SensorSectionOffsetOffset);
    _sensorElementSize = ReadUInt32(data, SensorSectionSizeOfElementOffset);
  }

  /// <summary>
  /// Publishes the snapshot, optionally after modifying a copy of its bytes.
  /// The poll time is set to now so the snapshot doesn't count as stale.
  /// </summary>
  internal static SharedMemorySnapshot Publish(Action<byte[]>? modify = null)
  {
    var data = Snapshot.ToArray();
    Write(data, PollTimeOffset, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    modify?.Invoke(data);
    return new SharedMemorySnapshot(data);
  }

  internal static void Write(byte[] data, int offset, uint value) =>
    BitConverter.GetBytes(value).CopyTo(data, offset);

  internal static void Write(byte[] data, int offset, long value) =>
    BitConverter.GetBytes(value).CopyTo(data, offset);

  internal static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) => BitConverter.ToUInt32(data[offset..]);

  /// <summary>
  /// Overwrites the poll time of the published section.
  /// </summary>
  internal void PatchPollTime(long unixSeconds) => _accessor.Write(PollTimeOffset, unixSeconds);

  /// <summary>
  /// Moves the poll time of the published section to now, so it doesn't count as stale.
  /// </summary>
  internal void PatchPollTimeToNow() => PatchPollTime(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

  /// <summary>
  /// Overwrites the number of reading elements the published section declares, which is what HWiNFO
  /// itself changes while it republishes them.
  /// </summary>
  internal void PatchReadingCount(uint count) => _accessor.Write(ReadingElementsNumElementsOffset, count);

  /// <summary>
  /// Overwrites the original name of one sensor element of the published section.
  /// </summary>
  internal void PatchSensorName(int sensorIndex, string name)
  {
    // The whole fixed-length field is rewritten, so what is left of the previous name can't trail
    // behind the NUL terminator
    var field = new byte[SmLayout.StringLength];
    Encoding.Latin1.GetBytes(name, field);

    var offset = _sensorSectionOffset + (long)sensorIndex * _sensorElementSize + SmLayout.SensorNameOrig;
    _accessor.WriteArray(offset, field, 0, field.Length);
  }

  public void Dispose()
  {
    _accessor.Dispose();
    _mmf.Dispose();
  }
}
