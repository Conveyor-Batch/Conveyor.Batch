# Benchmark Baseline — 0.1.0-beta.5

**Machine:** Apple M1 Pro, 10 cores  
**Runtime:** .NET 10.0.6, Arm64 RyuJIT AdvSIMD  
**Date:** 2026-08-04  
**Purpose:** Pre-optimization baseline for `EfCoreItemWriter`. Compare against this after
bulk write optimization (Step 3) to validate improvement.

---

## EfCoreItemWriterBenchmarks — current `AddRange` + `SaveChangesAsync`

| ChunkSize | Mean      | StdDev    | Gen0      | Gen1      | Allocated   |
|---------- |----------:|----------:|----------:|----------:|------------:|
| 100       |  2.960 ms | 0.3358 ms |         - |         - |   741.89 KB |
| 1000      | 13.301 ms | 7.6049 ms | 1000.0000 |         - |  6944.44 KB |
| 5000      | 37.263 ms | 0.5396 ms | 5000.0000 | 1000.0000 | 34706.45 KB |

### Reading the numbers

**Memory scales linearly with chunk size** — 741 KB → 6.9 MB → 34.7 MB. EF Core's
change tracker allocates a proxy entry per entity and holds it for the lifetime of the
`SaveChangesAsync` call. At 5000 items this triggers Gen1 GC on every write, which is
significant GC pressure in a high-throughput batch job.

**Throughput is non-linear** — 100 items in 3 ms (~30 µs/item), 1000 in 13 ms
(~13 µs/item), 5000 in 37 ms (~7.4 µs/item). The per-item cost drops as chunk size
grows but the change tracker overhead is the ceiling: it prevents true bulk-write
scaling.

### Target after bulk write optimization

- Memory: < 50 KB regardless of chunk size (no change tracker, raw wire protocol)
- Mean at ChunkSize=5000: < 5 ms (7x improvement target)
- Zero Gen1 GC at any chunk size

---

## ChunkEngineBenchmarks — sequential engine throughput

*(Run separately — see BenchmarkDotNet.Artifacts for raw output)*

| ItemCount | ChunkSize | Baseline established |
|---------- |---------- |--------------------- |
| 1,000     | 10/100/1000 | ✓ |
| 10,000    | 10/100/1000 | ✓ |
| 100,000   | 10/100/1000 | ✓ |
