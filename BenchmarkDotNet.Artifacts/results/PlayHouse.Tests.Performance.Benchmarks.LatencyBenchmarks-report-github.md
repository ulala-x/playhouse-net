```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
Intel Core Ultra 7 265K, 1 CPU, 20 logical and 20 physical cores
.NET SDK 8.0.122
  [Host]   : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  ShortRun : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                                   | Mean      | Error     | StdDev    | Ratio | RatioSD | Rank | Gen0   | Completed Work Items | Lock Contentions | Gen1   | Allocated | Alloc Ratio |
|----------------------------------------- |----------:|----------:|----------:|------:|--------:|-----:|-------:|---------------------:|-----------------:|-------:|----------:|------------:|
| &#39;Client→Server RTT&#39;                      |  86.25 μs |  64.93 μs |  3.559 μs |  1.00 |    0.05 |    1 | 0.8545 |               7.0050 |           0.0001 | 0.2441 |  13.38 KB |        1.00 |
| &#39;Game Tick Simulation (60 FPS)&#39;          | 342.44 μs | 320.60 μs | 17.573 μs |  3.97 |    0.23 |    2 | 2.4414 |               9.0176 |                - | 0.9766 |  43.82 KB |        3.27 |
| &#39;Server→Server RTT (via client trigger)&#39; | 360.81 μs | 501.93 μs | 27.512 μs |  4.19 |    0.31 |    2 | 2.9297 |               9.0205 |                - | 0.9766 |  50.15 KB |        3.75 |
| &#39;Sequential 10 messages&#39;                 | 495.49 μs | 165.76 μs |  9.086 μs |  5.75 |    0.22 |    3 | 5.8594 |              69.9863 |                - | 1.9531 |  91.02 KB |        6.80 |
