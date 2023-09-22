using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace Hwinfo.SharedMemory.Benchmark;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.ColdStart, warmupCount: 0)]
public class Benchmark
{
  private readonly SharedMemoryReader _sharedMemoryReader = new();

  [Benchmark]
  public IList<SensorReading> ReadSharedMemory() => _sharedMemoryReader.Read().ToList();
}