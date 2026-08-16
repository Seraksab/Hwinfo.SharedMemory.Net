using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace Hwinfo.SharedMemory.Benchmark;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, warmupCount: 1)]
public class Benchmark
{
  private readonly SharedMemoryReader _sharedMemoryReader = new();

  private readonly SharedMemoryReader _reusingReader = new(new SharedMemoryReaderOptions
    { ReuseUnchangedPolls = true });

  [Benchmark(Baseline = true)]
  public SensorReadings ReadSharedMemory() => _sharedMemoryReader.ReadLocal();

  [Benchmark]
  public SensorReadings ReadSharedMemoryReusingPolls() => _reusingReader.ReadLocal();
}