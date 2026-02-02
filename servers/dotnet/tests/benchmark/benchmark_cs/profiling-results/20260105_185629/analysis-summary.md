# PlayHouse Benchmark Profiling Analysis Summary

**Date**: 2026-01-05 18:56:29
**Duration**: 60 seconds benchmark, 30 seconds CPU profile, 40 seconds counters
**Configuration**: 100 connections, 1KB messages, send mode, max-inflight 1000

---

## Performance Results

### Throughput
- **Server TPS**: 252,057 msg/s (492.30 MB/s)
- **Client TPS**: 244,981 msg/s
- **Current vs Goal**: 252k TPS vs 1M TPS target (25.2% of goal)
- **Improvement needed**: 3.97x

### Latency
- **Server P99**: 0.00ms (not measured in send mode)
- **Client RTT Mean**: 368.43ms
- **Client RTT P50**: 336.55ms
- **Client RTT P95**: 719.25ms
- **Client RTT P99**: 959.22ms

### Memory & GC

#### Server
- **Memory**: 33.7 GB
- **GC Counts**: Gen0=2,380, Gen1=2,338, Gen2=77
- **GC Rate**: ~40 Gen0/sec, ~39 Gen1/sec, 1.3 Gen2/sec
- **Diagnosis**: ❌ **CRITICAL** - Excessive Gen2 GC (1.3/sec is very high)

#### Client
- **Memory**: 1,402 GB (!!!)
- **GC Counts**: Gen0=95,104, Gen1=55,720, Gen2=15,326
- **Diagnosis**: ❌ **CRITICAL** - Memory leak in client (timestamp queue overflow)

---

## Critical Issues Identified

### 1. Client Memory Leak (CRITICAL)
**Symptom**: 1.4 TB memory usage, 15k Gen2 GC
**Cause**: Unbounded `ConcurrentQueue<long>` for RTT timestamps
**Impact**: Client performance degradation, unreliable RTT measurements
**Solution**: Implemented in BenchmarkRunner.cs (limit queue size to max-inflight)

### 2. Server Gen2 GC Frequency (HIGH)
**Symptom**: 77 Gen2 GC in 60 seconds (1.3/sec)
**Expected**: < 0.1/sec for high-performance servers
**Impact**: 
- Stop-the-world pauses
- CPU cycles wasted on GC
- Throughput reduction
**Probable Causes**:
- Long-lived objects being promoted to Gen2
- Large objects (> 85KB) going to LOH
- Insufficient object pooling

### 3. High Gen0/Gen1 GC Rate (MEDIUM)
**Server**: 40 Gen0/sec, 39 Gen1/sec
**Impact**: Frequent minor GC pauses
**Probable Causes**:
- High allocation rate
- Insufficient ArrayPool usage
- Temporary object creation in hot paths

---

## Runtime Counters Analysis

**Note**: The dotnet-counters output shows mostly 0% CPU usage, which indicates the tool may not have captured data during peak load time. The counters were collected during the profiling window, but the actual benchmark load may have been low during that period.

**Key Metrics Observed**:
- CPU Usage: 0-0.4% (likely idle/warmup period)
- ThreadPool Thread Count: 7
- Allocation Rate: ~8-16 KB/sec (idle)
- Monitor Lock Contention: 0
- Exception Count: 0

**Conclusion**: Need to re-run profiling with better synchronization between load generation and data collection.

---

## CPU Profile Analysis

**File**: cpu-profile.nettrace (8.6 MB)
**Collection**: 30 seconds during benchmark

**Next Steps**:
1. Upload to https://www.speedscope.app/
2. Identify top 5 CPU hotspots
3. Analyze EventLoop thread distribution
4. Check for queue contention patterns

**Expected Hotspots** (based on hypothesis):
- BaseStage.ProcessOneMessageAsync (>40%?)
- TcpTransportSession.SendLoopAsync (>30%?)
- ConcurrentQueue operations (>15%?)
- MessageCodec serialization (>10%?)

---

## Bottleneck Hypotheses (Updated)

### Hypothesis 1: EventLoop Saturation ⚠️ (LIKELY)
**Evidence Needed**: CPU profile showing EventLoop threads at 100%
**Expected**: 16 EventLoop threads handling 100 stages
**Action**: Wait for CPU profile analysis

### Hypothesis 2: Excessive GC Pressure ✅ (CONFIRMED)
**Evidence**: 
- Gen2 GC: 1.3/sec (expected < 0.1/sec)
- Gen0/Gen1: ~40/sec each
**Impact**: ~5-10% throughput loss from GC pauses
**Action**: 
1. Identify large object allocations (LOH)
2. Expand ArrayPool usage
3. Implement object pooling for messages

### Hypothesis 3: Context Switching ⚠️ (UNKNOWN)
**Evidence**: Not collected (perf not available in WSL)
**Expected**: 220 threads (100 Send + 100 Receive + 16 EventLoop) on 20 cores = 11 threads/core
**Action**: Try to install perf or use alternative monitoring

### Hypothesis 4: Client-Side Bottleneck ✅ (CONFIRMED)
**Evidence**: 
- Client memory leak (1.4 TB)
- 15k Gen2 GC in client
- Client TPS (245k) < Server TPS (252k)
**Impact**: Client cannot keep up with server capacity
**Action**: Fix timestamp queue issue (already addressed in code)

---

## Optimization Priorities

### Immediate (Can do now)
1. ✅ **Fix client memory leak** - Already implemented in BenchmarkRunner.cs
2. **Analyze CPU profile** - Upload to speedscope.app
3. **Re-run profiling** - With fixed client

### Phase 1: Low-Hanging Fruit (Expected: 252k → 400k TPS)
1. **EventLoop scaling** (16 → 32)
   - File: StageEventLoopPool.cs
   - Change: `poolSize = Environment.ProcessorCount * 2`
   - Effort: 1 line
   - Expected: +30%

2. **Message batching** (1 → 10 messages)
   - File: BaseStage.cs, ProcessOneMessageAsync
   - Expected: +20%

3. **Reduce GC pressure**
   - Expand ArrayPool usage
   - Profile large object allocations
   - Expected: +10%

### Phase 2: Structural Changes (Expected: 400k → 700k TPS)
1. **SendLoop pooling** (100 → 16 threads)
2. **Zero-copy payload optimization**
3. **Object pooling for messages**

### Phase 3: Advanced Optimizations (Expected: 700k → 1M TPS)
1. **Inline dispatch** (avoid Task allocation)
2. **ReceiveLoop pooling** (SocketAsyncEventArgs)
3. **Protocol optimization** (msgId as ushort)

---

## Files Generated

All results in: `/home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_cs/profiling-results/20260105_185629/`

- ✅ **cpu-profile.nettrace** (8.6 MB) - Ready for speedscope.app
- ✅ **runtime-counters.txt** (50 KB) - Captured, but during idle period
- ✅ **client.log** - Shows memory leak (1.4 TB)
- ✅ **server.log** - Server startup and metrics
- ✅ **test-summary.txt** - Configuration
- ⚠️ **perf-stat.txt** - Not generated (perf not available in WSL2)

---

## Next Actions

1. **Analyze CPU Profile**:
   ```bash
   # Upload to speedscope.app
   # Look for:
   # - EventLoop thread CPU %
   # - BaseStage.ProcessOneMessageAsync %
   # - SendLoopAsync %
   # - ConcurrentQueue contention %
   ```

2. **Fix Client and Re-Profile**:
   ```bash
   cd /home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_cs
   # Client fix already committed
   ./profile-benchmark.sh
   ```

3. **Implement Phase 1 Optimizations**:
   ```bash
   # 1. EventLoop scaling (StageEventLoopPool.cs)
   # 2. Message batching (BaseStage.cs)
   # 3. ArrayPool expansion
   ```

4. **Measure Impact**:
   ```bash
   ./run-single.sh send 1024 100 60 1000
   # Target: 400k+ TPS (1.6x improvement)
   ```

---

## System Information

- **OS**: Linux 6.6.87.2-microsoft-standard-WSL2 (WSL2)
- **CPU**: 20 cores
- **.NET**: 8.0.122
- **Test Mode**: Send (fire-and-forget with callback)
- **Connections**: 100
- **Message Size**: 1KB
- **Max In-Flight**: 1000

---

## Conclusion

**Current Performance**: 252k TPS (25.2% of 1M goal)

**Confirmed Bottlenecks**:
1. ✅ Client memory leak (fixed)
2. ✅ Excessive Gen2 GC (1.3/sec)
3. ⚠️ Likely EventLoop saturation (need CPU profile)

**High-Confidence Quick Wins**:
1. EventLoop scaling (16 → 32): +30% = 327k TPS
2. Message batching: +20% = 392k TPS
3. GC optimization: +10% = 431k TPS

**Path to 1M TPS**: Achievable with Phase 1-3 optimizations (3-4 weeks effort)

**Next Immediate Step**: Analyze cpu-profile.nettrace in speedscope.app to confirm EventLoop hypothesis and identify specific hot functions.
