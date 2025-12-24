```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
Intel Core Ultra 7 265K, 1 CPU, 20 logical and 20 physical cores
.NET SDK 8.0.122
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  Job-TAJYCD : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

Force=True  InvocationCount=1  UnrollFactor=1  

```
| Method                                           | Mean       | Error    | StdDev   | Median     | Ratio | RatioSD | Rank | Allocated | Alloc Ratio |
|------------------------------------------------- |-----------:|---------:|---------:|-----------:|------:|--------:|-----:|----------:|------------:|
| &#39;Client Packet create/dispose&#39;                   | 2,013.8 ns | 248.7 ns | 725.3 ns | 1,856.0 ns |  1.12 |    0.56 |    3 |     816 B |        1.00 |
| &#39;RuntimeRoutePacket (byte[]) create/dispose&#39;     |   922.7 ns | 112.4 ns | 311.4 ns |   827.0 ns |  0.51 |    0.25 |    1 |     800 B |        0.98 |
| &#39;RuntimeRoutePacket (ByteString) create/dispose&#39; | 1,995.2 ns | 163.8 ns | 448.3 ns | 2,034.0 ns |  1.11 |    0.45 |    3 |     888 B |        1.09 |
| &#39;GetPayloadBytes() allocation&#39;                   | 1,593.3 ns | 213.6 ns | 619.6 ns | 1,441.5 ns |  0.89 |    0.46 |    2 |     856 B |        1.05 |
| &#39;RouteHeader serialize&#39;                          | 3,319.2 ns | 308.6 ns | 895.4 ns | 2,940.5 ns |  1.85 |    0.80 |    5 |     864 B |        1.06 |
| &#39;Protobuf message serialize&#39;                     | 2,792.0 ns | 336.6 ns | 992.4 ns | 2,342.5 ns |  1.55 |    0.77 |    4 |     856 B |        1.05 |
