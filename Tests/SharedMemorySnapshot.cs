using System.IO.MemoryMappedFiles;

namespace Hwinfo.SharedMemory.Tests;

/// <summary>
/// A snapshot of the HWiNFO shared memory, published under a session local name so tests can read
/// real sensor data without a running HWiNFO instance.
/// </summary>
internal sealed class SharedMemorySnapshot : IDisposable
{
  // Offsets within the SmSensorsSharedMem2 header
  internal const int SignatureOffset = 0;
  internal const int VersionOffset = 4;
  internal const int PollTimeOffset = 12;
  internal const int SensorSectionNumElementsOffset = 28;
  internal const int ReadingSectionOffsetOffset = 32;

  // Offset of SensorIndex within a reading element
  internal const int ReadingSensorIndexOffset = 4;

  private static readonly byte[] Snapshot =
    File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "hwinfo-sm2-snapshot.bin"));

  private readonly MemoryMappedFile _mmf;
  private readonly MemoryMappedViewAccessor _accessor;

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

  internal static uint ReadUInt32(byte[] data, int offset) => BitConverter.ToUInt32(data, offset);

  public void Dispose()
  {
    _accessor.Dispose();
    _mmf.Dispose();
  }
}
