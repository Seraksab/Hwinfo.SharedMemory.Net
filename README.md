# Hwinfo.SharedMemory.Net

[![Nuget](https://img.shields.io/nuget/v/Hwinfo.SharedMemory.Net?style=flat-square)](https://www.nuget.org/packages/Hwinfo.SharedMemory.Net)
![GitHub](https://img.shields.io/github/license/Seraksab/Hwinfo.SharedMemory.Net)

A small and simple library to read sensor values shared by [HWiNFO](https://www.hwinfo.com/) via shared memory.

**Note**: This requires "Shared Memory Support" to be enabled in the HWiNFO settings

## Usage

```csharp
var reader = new SharedMemoryReader();
foreach (var sensorReading in reader.ReadLocal())
{
  Console.Out.WriteLine(sensorReading);
}
```

## Benchmark

| Method           |     Mean |     Error |    StdDev | Allocated |
|------------------|---------:|----------:|----------:|----------:|
| ReadSharedMemory | 4.463 ms | 0.3104 ms | 0.9151 ms | 180.02 KB |

