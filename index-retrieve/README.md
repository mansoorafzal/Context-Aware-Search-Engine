# Index & Retrieve service

The `index-retrieve` ASP.NET Core service consumes the crawler's processed page messages, converts them to Lucene.NET documents, builds a searchable in-memory inverted index, and exposes endpoints to search it. With the provided `http` launch profile, it listens on **http://localhost:5080**.

See the [root README](../README.md) for the full system architecture and startup order.

## How it works

`Program.cs` binds the local `appsettings.json` `Configurations` section and registers the orchestrator, index factories, named HTTP client, and seven pipeline services. These hosted services are activated for pipeline work when `POST /api/v1/start` calls `PipelineOrchestrator.StartAsync()`.

There are two flows running inside the service:

```text
INDEXING
  HTTP GET /api/v1/pull (poll queue service)
          |
          v
  InputService -> DocumentIngestorService -> LuceneService
                   (Lucene Document)     (RAMDirectory)

SEARCH
  HTTP GET /api/v1/search?input=...
          |
          v
  InputService -> QueryParserService -> BooleanExpressionEvaluatorService
                                                 |
                                                 v
                                          RankerService -> OutputService
                                                               |
                                                               v
                                                        HTTP JSON response
```

`Services/ChannelBus.cs` uses `System.Threading.Channels`. Some channels are bounded to `Channel_Processing_Capacity` and others are unbounded; stages use up to `Concurrent_Workers` concurrent tasks. The orchestrator holds a cancellation token shared by these loops and records metrics for status responses.

## Ingestion and indexing pipeline

1. **`InputService.cs`** polls the queue via `GET /api/v1/pull` approximately every 100 ms while running. On `200 OK`, it reads the response content and deserializes it into `shared.EncryptedPage`. This record contains the original fetched/extracted page plus SHA-256 and Simhash values from the crawler.
2. **`DocumentIngestorService.cs`** builds a Lucene `Document` from each page. It sets `id` and `url` to the original absolute URL, puts the original fetched HTML in `content`, uses the extracted title for `title`, and copies the crawler score to `score`.
3. **`LuceneService.cs`** calls `IndexWriter.UpdateDocument`, commits the index, and records a completed indexing operation. `IndexReaderFactory` refreshes the reader/searcher on queries.

| Lucene field | Type | Stored? | Meaning |
| --- | --- | --- | --- |
| `id` | `StringField` | Yes | Document identifier based on URL. |
| `url` | `StringField` | Yes | URL returned in search results. |
| `content` | `TextField` | No | Indexed HTML source (the crawler's fetched content), used for matching. |
| `title` | `TextField` | Yes | Indexed title and search-result title. |
| `score` | `SingleField` | Yes | Crawler-computed page score; **not currently used in final ranking**. |

`InvertedIndex/IndexWriterFactory.cs` creates `RAMDirectory` with a Lucene 4.8 `StandardAnalyzer`. The index is therefore **process-local and lost on restart**; there is no disk-backed index or external datastore in the current project.

## Search pipeline

1. **`HomeController.SearchAsync`** validates the `input` query parameter and calls `PipelineOrchestrator.SearchAsync(input)`. If the pipeline has not been started, it returns `503 Service Unavailable`.
2. **`InputService.cs`** writes the input string to the query parser channel.
3. **`QueryParserService.cs`** uses Lucene's `MultiFieldQueryParser` with `StandardAnalyzer` over the `title` and `content` fields. It creates a `BooleanQuery` requiring the parsed query (`MUST`). If refinements are enabled, optional title/body phrase and configured keyword query clauses are added (`SHOULD`).
4. **`BooleanExpressionEvaluatorService.cs`** refreshes the Lucene reader and calls `IndexSearcher.Search` with `Maximum_Search_Documents` as the result limit.
5. **`RankerService.cs`** maps Lucene hits to `RankedDocument(Url, Title, Score)` and sorts the list by the Lucene `ScoreDoc.Score` in descending order.
6. **`OutputService.cs`** completes the awaiting search task with the ranked list. The controller serializes it as JSON. The orchestrator records elapsed search latency; `GET /status` reports average and P95 latencies using `shared.Metrics`.

Example request:

```bash
curl --get 'http://localhost:5080/api/v1/search' \
  --data-urlencode 'input=machine learning'
```

Example **response shape** (illustrative; values depend on indexed pages):

```json
[
  {
    "url": "https://example.org/ai",
    "title": "Introduction to AI",
    "score": 1.42
  }
]
```

The query string is parsed as Lucene query syntax; it is not treated exclusively as an opaque exact phrase. The result cap is `Maximum_Search_Documents` (default `10`).

### Context-aware query refinements

With `Refinements_Enabled = true`, the parser adds optional boosts for:

- Configured `Context_Aware_Keywords` appearing in **title** (`Context_Aware_Title_Reward`, default `3.0`) or **content** (`Context_Aware_Body_Reward`, default `6.0`).
- When the submitted input contains more than one space-delimited term, a title phrase query and a content phrase query, each using `Context_Aware_Phrase_Reward` (default `9.0`).

The original parsed query remains mandatory; the extra clauses are optional ranking signals, not separate matching criteria. The code uses Lucene `BoostedQuery` with `QueryValueSource` for these clauses. The final score returned by the API is the Lucene search score, **not** the extracted crawler page score.

When `Refinements_Enabled = false`, only the base parsed query is required and no optional contextual clauses are added.

## HTTP API

All routes are under `/api/v1`:

| Method | Path | Purpose | Representative responses |
| --- | --- | --- | --- |
| `POST` | `/start` | Start queue polling, indexing and search pipeline stages. | `200` started, `208` already running. |
| `POST` | `/stop` | Cancel the pipeline. | `200` stopped, `208` already stopped. |
| `GET` | `/status` | Mode, state, indexing throughput, average and P95 latency. | `200` metrics object. |
| `GET` | `/search?input=...` | Search the current index. | `200` result list, `400` blank input, `503` pipeline not started. |
| `GET` | `/load-test?input=...` | Call the orchestrator's load-test loop with the configured request count. | `200` after the loop, `400` blank input. |

```bash
curl -X POST http://localhost:5080/api/v1/start
curl http://localhost:5080/api/v1/status
curl --get 'http://localhost:5080/api/v1/search' --data-urlencode 'input=robotics'
curl -X POST http://localhost:5080/api/v1/stop
```

Swagger UI is available at `http://localhost:5080/swagger` when started under the included development `http` launch profile.

## Configuration

The `Configurations` section in `index-retrieve/appsettings.json` includes:

| Setting | Default | Purpose |
| --- | --- | --- |
| `Inter_Services_Pub_Sub_Url` | `http://localhost:5081` | Queue service base address. |
| `Http_Client_Request_Timeout_Seconds` | `200` | HTTP timeout for queue polling. |
| `Channel_Processing_Capacity` | `100` | Capacity for bounded pipeline channels. |
| `Concurrent_Workers` | `15` | Per-stage concurrency limit. |
| `Maximum_Search_Documents` | `10` | Maximum returned Lucene hits. |
| `Concurrent_Search_Requests` | `100` | Number of iterations in `LoadTestAsync`; they are awaited sequentially in the current method. |
| `Context_Aware_Keywords` | AI/ML-related terms | Keyword list for optional query boosts. |
| `Context_Aware_Title_Reward` | `3.0` | Title keyword query boost setting. |
| `Context_Aware_Body_Reward` | `6.0` | Content keyword query boost setting. |
| `Context_Aware_Phrase_Reward` | `9.0` | Multi-term phrase query boost setting. |
| `Refinements_Enabled` | `true` | Enable optional contextual clauses. |

The queue service must be running, and its HTTP queue must have received page messages before indexing has anything to consume. Start this service's pipeline before starting the crawler.

## Implementation limitations

- **In-memory index:** `RAMDirectory` is not persisted. Stopping the process loses indexed documents.
- **Response correlation:** `OutputService.WaitAsync()` writes to a single `_taskCompletionSource` field. Multiple overlapping queries can overwrite each other's waiting task and result in missing/misrouted responses. This should be changed to request-correlated responses before supporting concurrent search traffic.
- **Empty index:** If `BooleanExpressionEvaluatorService` cannot obtain a searcher (for example before the first index has been created), it skips output for that query; callers may continue waiting rather than receiving an empty result immediately.
- **Load test:** `LoadTestAsync` runs `await SearchAsync(input)` inside a `for` loop. It does not issue `Concurrent_Search_Requests` truly concurrent requests, despite the configuration name.
- **Document ID mismatch:** `LuceneService` normalizes the URL for the update term via `GetDocumentId`, while `DocumentIngestorService` stores the unnormalized URL in the `id` field. Update matching may therefore not replace an existing document as intended.
- **No separate semantic/vector index:** The current retrieval uses Lucene keyword queries and optional keyword/phrase clauses, not embeddings or a neural reranker.

## Source map

- API: `Controllers/HomeController.cs`
- Startup / dependency injection: `Program.cs`, `Extensions/ServiceCollectionExtension.cs`
- Start/stop/search coordination: `Orchestrators/PipelineOrchestrator.cs`
- Index storage and refresh: `InvertedIndex/IndexWriterFactory.cs`, `InvertedIndex/IndexReaderFactory.cs`
- Pipeline: `Services/InputService.cs`, `DocumentIngestorService.cs`, `LuceneService.cs`, `QueryParserService.cs`, `BooleanExpressionEvaluatorService.cs`, `RankerService.cs`, `OutputService.cs`

Message record types and shared metrics are documented in [shared/README.md](../shared/README.md).
