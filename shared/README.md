# Shared library

`shared` is a .NET 8 class library referenced by all three executable services (`crawl`, `index-retrieve`, and `inter-services-pub-sub`). It defines the records passed between pipeline stages, an in-process generic queue, and metrics used by the service status endpoints. It has **no HTTP server or standalone launch command**.

See the [root README](../README.md) for how the projects fit together.

## Shared data contracts (`Records.cs`)

| Record | Main fields | Used by |
| --- | --- | --- |
| `RankedLink` | `Uri`, `Score`, `Depth`, `Retry` | URL discovery, crawl prioritization and retries. |
| `FetchedPage` | `RankedLink`, HTTP `StatusCode`, HTML `Content`, `Headers`, `Latency` | Fetcher to extractor. |
| `ExtractedPage` | `FetchedPage`, `Metadata`, `Title`, `Body`, `OutboundLinks`, `Score`, `Bytes` | Extractor to content deduplication. |
| `EncryptedPage` | `ExtractedPage`, `Sha256`, `Simhash` | Final crawler output and queue payload consumed by indexer. Despite the name, this record is **hashed, not encrypted**. |
| `IndexedDocument` | `Id`, Lucene `Document` | Index ingestion to Lucene writer. |
| `QueryDocument` | `Terms`, Lucene `Query`, `PhraseQueries`, `BooleanQuery` | Query parsing to evaluation. |
| `CandidateDocument` | `QueryDocument`, `IndexSearcher`, `TopDocs` | Search evaluation to ranker. |
| `RankedDocument` | `Url`, `Title`, `Score` | Search results returned by the indexer HTTP API. |
| `CrawlData` | Mode, state, elapsed time, fetch latency, page size, duplicate and output counts | Crawler `GET /api/v1/status`. |
| `IndexRetrieveData` | Mode, state, indexing throughput, network latency and P95 latency | Indexer `GET /api/v1/status`. |

The data flow is:

```text
RankedLink -> FetchedPage -> ExtractedPage -> EncryptedPage
                                                  |
                                                  v
                                         HTTP queue transport
                                                  |
                                                  v
                                             IndexedDocument

query string -> QueryDocument -> CandidateDocument -> RankedDocument[]
```

Some records contain Lucene.NET types (`Document`, `Query`, `IndexSearcher`), so the shared `.csproj` references `Lucene.Net` `4.8.0-beta00017`.

### Status serialization

`CrawlData` and `IndexRetrieveData` use `JsonPropertyName` attributes for human-readable metric keys (such as `Average Page Size (KB)`) and `JsonIgnore`/`JsonIgnoreCondition` to suppress some fields from the HTTP status response. These record shapes are distinct from the internal `Metrics` fields.

## Generic message queue (`MessageQueue.cs`)

`MessageQueue<T>` is a static generic class that wraps:

```text
BlockingCollection<T>(ConcurrentQueue<T>, capacity)
```

- `Initialize(capacity, timeoutMilliseconds)` sets the queue's parameters once for each closed generic type `T`.
- `Push(item)` lazily initializes a queue using defaults if necessary, then calls `BlockingCollection<T>.Add`.
- `Pull(cancellationToken)` calls `TryTake` with the per-type timeout; if no item is available it returns `default(T)`.
- Default settings (when not initialized explicitly): capacity `100`, poll timeout `200 ms`.

**Each closed generic type has independent static state.** In the queue web project, initialization currently happens for `MessageQueue<EncryptedPage>` but controller push/pull use `MessageQueue<string>`; this causes the actual HTTP queue to use defaults on first push and can cause a pull-before-push failure. See the [queue README](../inter-services-pub-sub/README.md) for details.

This queue is process-local and one-consumer-per-item, rather than a cross-process broker. Communication across services is achieved by the queue web project's HTTP endpoints.

## Metrics (`Metrics.cs`)

`Metrics` stores state in static fields and provides helpers for each processing service:

- `Start()` / `Stop()` track pipeline state and running duration.
- `RecordFirstInput()` / `RecordLastOutput()` track time between first input and latest output and increment completed output count.
- `RecordLatency(TimeSpan)` stores latency samples, used to calculate average and P95 milliseconds.
- `RecordBytes(int)` tracks downloaded content sizes and extracted-page count.
- `RecordDuplicatePages()` / `RecordNearDuplicatePages()` track crawler deduplication outcomes.
- Derived properties report throughput, elapsed duration, average page size and total downloaded megabytes.

Concurrent bags hold latency and byte samples; counts and state are held in static fields. Since the executable services run in **separate processes**, each has its own metrics instance, even though they reference the same library. The counters are not persisted across restarts or aggregated across replicas. Metric collections and counters are not reset by `Start()`.

## How to build

From the solution root:

```bash
dotnet build shared/shared.csproj
```

There is no `dotnet run` command for this project. Its public types are used by `crawl`, `inter-services-pub-sub`, and `index-retrieve` through their `<ProjectReference>` entries.

## Files

- `Records.cs` — data transfer objects and status response records.
- `MessageQueue.cs` — generic in-memory work queue implementation.
- `Metrics.cs` — time, volume, duplication and latency metrics.
- `shared.csproj` — target framework and Lucene.NET package reference.
