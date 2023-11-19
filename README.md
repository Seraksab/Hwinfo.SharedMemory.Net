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

| Method           | Mean     | Error     | StdDev    | Allocated |
|----------------- |---------:|----------:|----------:|----------:|
| ReadSharedMemory | 1.546 ms | 0.2834 ms | 0.8356 ms | 156.91 KB |

Run on: AMD Ryzen 7 1800X, DDR4-3000 CL14
