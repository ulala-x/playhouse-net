```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
Intel Core Ultra 7 265K, 1 CPU, 20 logical and 20 physical cores
.NET SDK 8.0.122
  [Host]   : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  ShortRun : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

Job=ShortRun  InvocationCount=1  IterationCount=3  
LaunchCount=1  UnrollFactor=1  WarmupCount=3  

```
| Method                 | MessageCount | Mean        | Error        | StdDev     | Ratio | RatioSD | Rank | Allocated  | Alloc Ratio |
|----------------------- |------------- |------------:|-------------:|-----------:|------:|--------:|-----:|-----------:|------------:|
| **&#39;Sequential send&#39;**      | **100**          |  **5,881.8 μs** |  **10,228.7 μs** |   **560.7 μs** |  **1.01** |    **0.12** |    **3** |  **737.14 KB** |        **1.00** |
| &#39;Parallel send&#39;        | 100          |  1,078.9 μs |   2,843.0 μs |   155.8 μs |  0.18 |    0.03 |    2 |  450.13 KB |        0.61 |
| &#39;Fire-and-forget send&#39; | 100          |    487.6 μs |   4,717.2 μs |   258.6 μs |  0.08 |    0.04 |    1 |   88.58 KB |        0.12 |
|                        |              |             |              |            |       |         |      |            |             |
| **&#39;Sequential send&#39;**      | **1000**         | **58,467.9 μs** | **119,246.6 μs** | **6,536.3 μs** |  **1.01** |    **0.14** |    **3** | **8205.63 KB** |        **1.00** |
| &#39;Parallel send&#39;        | 1000         |  8,566.0 μs |  18,284.2 μs | 1,002.2 μs |  0.15 |    0.02 |    2 | 3750.32 KB |        0.46 |
| &#39;Fire-and-forget send&#39; | 1000         |  5,537.2 μs |  14,668.1 μs |   804.0 μs |  0.10 |    0.02 |    1 | 1975.05 KB |        0.24 |
