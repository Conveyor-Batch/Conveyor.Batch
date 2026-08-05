
BenchmarkDotNet v0.14.0, macOS 26.5.2 (25F84) [Darwin 25.5.0]
Apple M1 Pro, 1 CPU, 10 logical and 10 physical cores
.NET SDK 10.0.106
  [Host]     : .NET 10.0.6 (10.0.626.17701), Arm64 RyuJIT AdvSIMD
  Job-TELMEM : .NET 10.0.6 (10.0.626.17701), Arm64 RyuJIT AdvSIMD

InvocationCount=1  UnrollFactor=1  

 Method     | ChunkSize | Mean      | Error     | StdDev    | Median    | Gen0      | Gen1      | Allocated   |
----------- |---------- |----------:|----------:|----------:|----------:|----------:|----------:|------------:|
 **WriteChunk** | **100**       |  **2.960 ms** | **0.1151 ms** | **0.3358 ms** |  **2.949 ms** |         **-** |         **-** |   **741.89 KB** |
 **WriteChunk** | **1000**      | **13.301 ms** | **2.5930 ms** | **7.6049 ms** |  **8.416 ms** | **1000.0000** |         **-** |  **6944.44 KB** |
 **WriteChunk** | **5000**      | **37.263 ms** | **0.6087 ms** | **0.5396 ms** | **37.186 ms** | **5000.0000** | **1000.0000** | **34706.45 KB** |
