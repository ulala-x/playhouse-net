```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
Intel Core Ultra 7 265K, 1 CPU, 20 logical and 20 physical cores
.NET SDK 8.0.122
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  Job-OODSAH : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

Force=True  InvocationCount=1  UnrollFactor=1  

```
| Method                               | Mean       | Error    | StdDev     | Median     | Ratio | RatioSD | Rank | Allocated | Alloc Ratio |
|------------------------------------- |-----------:|---------:|-----------:|-----------:|------:|--------:|-----:|----------:|------------:|
| &#39;FramePayload create (byte[] copy)&#39;  |   762.8 ns | 118.8 ns |   348.4 ns |   738.0 ns |  1.25 |    0.89 |    1 |     760 B |        1.00 |
| &#39;ByteArrayPayload create&#39;            |   755.3 ns | 108.6 ns |   300.9 ns |   694.0 ns |  1.24 |    0.83 |    1 |     760 B |        1.00 |
| &#39;FromFrames(byte[], byte[])&#39;         | 4,406.9 ns | 448.2 ns | 1,321.4 ns | 4,364.0 ns |  7.21 |    4.34 |    3 |     480 B |        0.63 |
| &#39;FromFrames(Span) with fixed buffer&#39; | 3,901.2 ns | 379.9 ns | 1,120.2 ns | 3,367.5 ns |  6.39 |    3.79 |    3 |    1208 B |        1.59 |
| &#39;RouteHeader parse from byte[]&#39;      | 4,426.4 ns | 405.4 ns | 1,182.5 ns | 4,297.5 ns |  7.25 |    4.22 |    3 |    1088 B |        1.43 |
| &#39;RouteHeader parse from Span&#39;        | 4,201.2 ns | 406.4 ns | 1,198.1 ns | 4,282.0 ns |  6.88 |    4.07 |    3 |     920 B |        1.21 |
| &#39;ServerId encode (new alloc)&#39;        | 1,429.0 ns | 231.9 ns |   654.0 ns | 1,202.5 ns |  2.34 |    1.67 |    2 |     776 B |        1.02 |
| &#39;ServerId decode from fixed buffer&#39;  | 1,699.1 ns | 297.9 ns |   854.8 ns | 1,258.5 ns |  2.78 |    2.09 |    2 |     792 B |        1.04 |
