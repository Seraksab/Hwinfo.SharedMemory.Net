using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace Hwinfo.SharedMemory.Benchmark;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, warmupCount: 1)]
public class Benchmark
{
  private readonly SharedMemoryReader _sharedMemoryReader = new();
  private readonly SharedMemoryReader _reusingReader = new(reuseUnchangedPolls: true);

  [Benchmark(Baseline = true)]
  public IReadOnlyList<SensorReading> ReadSharedMemory() => _sharedMemoryReader.ReadLocal();

  [Benchmark]
  public IReadOnlyList<SensorReading> ReadSharedMemoryReusingPolls() => _reusingReader.ReadLocal();
}
