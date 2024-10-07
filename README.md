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

| Method           |     Mean |    Error |   StdDev | Allocated |
|------------------|---------:|---------:|---------:|----------:|
| ReadSharedMemory | 214.3 us | 101.0 us | 297.8 us | 202.44 KB |

Run on:

- Windows 11
- .NET 8.0.7
- AMD Ryzen 9 7900X
- DDR5-6200 CL30
