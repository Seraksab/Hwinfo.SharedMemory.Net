using System;
using System.Threading;

namespace Hwinfo.SharedMemory;

/// <summary>
/// The settings a <see cref="SharedMemoryReader"/> is created with. All of them are optional:
/// <c>new SharedMemoryReader(new SharedMemoryReaderOptions { ReuseUnchangedPolls = true })</c>
/// changes one and keeps the defaults for the rest.
/// </summary>
public sealed record SharedMemoryReaderOptions
{
  /// <summary>
  /// How long to wait for HWiNFO's mutex before a read gives up with a <see cref="TimeoutException"/>.
  /// <see cref="Timeout.InfiniteTimeSpan"/> waits indefinitely. Defaults to one second.
  /// <para>
  /// HWiNFO's mutex is advisory: it commonly runs elevated and the mutex it creates may not be
  /// openable by a normal user process, in which case reads go ahead without it. This setting has no
  /// effect then and no <see cref="TimeoutException"/> is thrown - see
  /// <see cref="SharedMemoryReader"/> for what still guards those reads.
  /// </para>
  /// </summary>
  public TimeSpan MutexTimeout { get; init; } = TimeSpan.FromSeconds(1);

  /// <summary>
  /// How long a shared memory file may go without an update by HWiNFO before it is considered stale
  /// and is reopened. <see cref="TimeSpan.Zero"/> never considers it stale. Should be well above the
  /// polling period configured in HWiNFO. Defaults to one minute.
  /// </summary>
  public TimeSpan StalenessTimeout { get; init; } = TimeSpan.FromMinutes(1);

  /// <summary>
  /// Whether a read that cannot obtain HWiNFO's mutex fails with an
  /// <see cref="InvalidOperationException"/> instead of going ahead without it. Off by default, i.e.
  /// the mutex is advisory.
  /// <para>
  /// Turn this on to be certain no result was assembled unsynchronized. It only applies once there is
  /// something to read: a missing section or one without a valid HWiNFO header still reports itself
  /// the usual way, so a polling loop on <see cref="SharedMemoryReader.TryReadLocal"/> keeps getting
  /// <c>false</c> while HWiNFO isn't publishing rather than an exception.
  /// </para>
  /// </summary>
  public bool RequireMutex { get; init; }

  /// <summary>
  /// Whether to hand out the previous result instead of reading the shared memory again while HWiNFO's
  /// poll time is unchanged, which makes reads that are more frequent than HWiNFO's polling period
  /// almost free. HWiNFO reports its poll time in whole seconds, so with a polling period below one
  /// second this can return values that are up to a second old. Off by default.
  /// </summary>
  public bool ReuseUnchangedPolls { get; init; }

  /// <exception cref="ArgumentOutOfRangeException">A timeout is negative or too large.</exception>
  internal void Validate()
  {
    var mutexMilliseconds = MutexTimeout.TotalMilliseconds;
    if (MutexTimeout != Timeout.InfiniteTimeSpan && (mutexMilliseconds < 0 || mutexMilliseconds > int.MaxValue))
    {
      throw new ArgumentOutOfRangeException(
        nameof(MutexTimeout), MutexTimeout,
        $"Must not be negative and must not exceed {int.MaxValue} ms, or be Timeout.InfiniteTimeSpan."
      );
    }

    if (StalenessTimeout < TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(StalenessTimeout), StalenessTimeout, "Must not be negative.");
    }
  }
}
