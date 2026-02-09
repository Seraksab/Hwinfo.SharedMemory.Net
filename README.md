# Hwinfo.SharedMemory.Net

[![Nuget](https://img.shields.io/nuget/v/Hwinfo.SharedMemory.Net?style=flat-square)](https://www.nuget.org/packages/Hwinfo.SharedMemory.Net)
![GitHub](https://img.shields.io/github/license/Seraksab/Hwinfo.SharedMemory.Net)

A small and simple library to read sensor values shared by [HWiNFO](https://www.hwinfo.com/) via shared memory.

## Prerequisites

Enable **Shared Memory Support** in HWiNFO.  
If this isn't enabled, the reader will return no values.

## Usage

```csharp
var reader = new SharedMemoryReader();
foreach (var sensorReading in reader.ReadLocal())
{
  Console.Out.WriteLine(sensorReading);
}
```

## Benchmark

| Method           |     Mean |    Error |   StdDev |    Gen0 |   Gen1 | Allocated |
|------------------|---------:|---------:|---------:|--------:|-------:|----------:|
| ReadSharedMemory | 95.88 us | 0.828 us | 0.774 us | 13.7939 | 3.5400 | 225.93 KB |

Run on:

- Windows 11 Pro 25H2
- .NET 10.0.102
- CPU: AMD Ryzen 9 7900X
- RAM: DDR5-6200 CL30

## License

See [LICENSE](LICENSE)
