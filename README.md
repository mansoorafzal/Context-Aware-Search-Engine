# Context Aware Search Engine (CASE)

CASE is a .NET 8 prototype that crawls websites, extracts and deduplicates HTML pages, passes them through an HTTP-backed queue, and indexes them in Lucene.NET for keyword search. Its two main processing services use asynchronous `System.Threading.Channels` pipelines, while configuration controls crawl scope and optional context-oriented scoring and query boosts.

> **Implementation note:** This repository is a prototype, not a durable distributed search platform. The Lucene index and inter-service queue are in memory, and the current queue initialization and search concurrency behavior have important limitations described below.

## Projects

| Project | Default HTTP port | Responsibility | Detailed documentation |
| --- | --- | --- | --- |
| `crawl` | `5082` | Seed URLs, enforce scope, fetch HTML, extract content and links, score and deduplicate pages, send results downstream. | [Crawler README](crawl/README.md) |
| `inter-services-pub-sub` | `5081` | Expose HTTP `push`/`pull` endpoints around a process-local queue for handing documents from crawler to indexer. | [Queue README](inter-services-pub-sub/README.md) |
| `index-retrieve` | `5080` | Poll for crawled pages, build an in-memory Lucene index, parse search queries, rank matches, and return results. | [Index & Retrieve README](index-retrieve/README.md) |
| `shared` | — | Shared message records, queue implementation, and process-local metrics. Not an executable service. | [Shared README](shared/README.md) |

The solution file is `context-aware-search-engine.sln`. All four projects target `net8.0`.

## How the system works

```text
POST /api/v1/start (crawler)
            |
            v
  Configured seed URLs
            |
            v
 Scope -> URL dedup -> Frontier -> HTTP fetch -> HTML extraction
                                                  |
                                                  v
                                    SHA-256 / Simhash dedup
                                       |             |
                                       |             +--> Discovered links return to Scope
                                       v
                          Crawl OutputService (HTTP POST)
                                       |
                                       v
             inter-services-pub-sub: POST /api/v1/push
                                       |
                              In-memory queue
                                       |
                                       v
                index-retrieve: GET /api/v1/pull (polling)
                                       |
                                       v
                        Document conversion -> Lucene RAMDirectory
                                       |
  GET /api/v1/search?input=...         |
                 |                     |
                 v                     v
          Parse query -> Evaluate against index -> Rank -> JSON results
```

**Crawler:** `crawl/Orchestrators/PipelineOrchestrator.cs` starts eight stages. Seed URLs enter the scope filter; accepted, unseen URLs are scheduled by the frontier and fetched. HTML is parsed for title, description, body text and links. Duplicate/near-duplicate bodies are filtered; unique pages are published and their links are fed back into the crawl pipeline. Optional crawl-time contextual scoring is computed during extraction.

**Queue:** The crawler posts a JSON-serialized `EncryptedPage` as a JSON **string** to `POST /api/v1/push`. The indexer polls `GET /api/v1/pull` and deserializes each returned string to `EncryptedPage`. Despite the project name, this is a single-consumer work queue, not a broadcast pub/sub broker.

**Index/search:** After its pipeline starts, the indexer ingests queue messages into a `RAMDirectory` through Lucene.NET. Searches use the query parser over `title` and `content`, with optional context keyword and phrase boosts. The response includes matching URLs, titles and Lucene scores. The crawler's stored page score is **not** currently added to the Lucene query ranking.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Network access to restore NuGet dependencies and crawl the configured seed websites
- Three terminal sessions (or an IDE capable of running multiple startup projects)

No external database or message broker is configured. The default seeds target Georgia Tech sites and the default domain scope is `gatech.edu`; review `crawl/appsettings.json` before requesting a crawl, and only crawl sites you are authorized to access.

## Quick start

From the repository root, restore and build:

```bash
dotnet restore context-aware-search-engine.sln
dotnet build context-aware-search-engine.sln
```

Start each HTTP service in a separate terminal. Start the queue first, then index/retrieve, then crawler:

```bash
# Terminal 1
dotnet run --project inter-services-pub-sub --launch-profile http

# Terminal 2
dotnet run --project index-retrieve --launch-profile http

# Terminal 3
dotnet run --project crawl --launch-profile http
```

Start the indexer **before** the crawler so it can consume pages as they arrive:

```bash
curl -X POST http://localhost:5080/api/v1/start
curl -X POST http://localhost:5082/api/v1/start
```

Inspect progress and search after documents have been indexed:

```bash
curl http://localhost:5082/api/v1/status
curl http://localhost:5080/api/v1/status
curl --get 'http://localhost:5080/api/v1/search' \
  --data-urlencode 'input=machine learning'
```

Both processing pipelines also support `POST /api/v1/stop`. To avoid losing in-memory state, leave the services running while experimenting. Swagger UI is available in development mode at `http://localhost:5080/swagger`, `http://localhost:5081/swagger`, and `http://localhost:5082/swagger` when using the provided `http` profiles.

### HTTP API summary

| Service | Method | Path | Purpose |
| --- | --- | --- | --- |
| Crawler | `POST` | `/api/v1/start` | Start crawling the configured `Seeds`. |
| Crawler | `POST` | `/api/v1/stop` | Cancel crawling. |
| Crawler | `GET` | `/api/v1/status` | Crawl mode, state, and metrics. |
| Queue | `POST` | `/api/v1/push` | Accept a JSON string containing the serialized page message (`202`). |
| Queue | `GET` | `/api/v1/pull` | Remove one queued message (`200`) or report no item (`204`). |
| Index/retrieve | `POST` | `/api/v1/start` | Begin queue ingestion and query-processing stages. |
| Index/retrieve | `POST` | `/api/v1/stop` | Cancel the index/search pipeline. |
| Index/retrieve | `GET` | `/api/v1/status` | Indexing/search metrics and state. |
| Index/retrieve | `GET` | `/api/v1/search?input=...` | Return URL/title/score matches for the query. |
| Index/retrieve | `GET` | `/api/v1/load-test?input=...` | Invoke searches in the load-test loop; not a separate test harness. |

### Configuration

Each web service reads its `Configurations` section from its own `appsettings.json`; ASP.NET Core environment variables can override values. Key settings include:

| File | Settings | Meaning |
| --- | --- | --- |
| `crawl/appsettings.json` | `Seeds`, `Domain_Scopes` | Initial URLs and accepted host suffixes. |
| | `Context_Aware_Body_Keywords`, `Context_Aware_Host_Keywords`, rewards, `Depth_Penalty` | Crawl-time page scoring. |
| | `Concurrent_Workers`, `Channel_Processing_Capacity` | Stage worker concurrency and selected channel capacities. |
| | `Politeness_Enabled`, crawl-delay bounds, `Near_Duplicate_Threshold` | Fetch rules and content deduplication. |
| | `Quiescence_Monitor_Enabled`, `Quiescence_Monitor_Shutdown_Seconds` | Optional automatic stop check. |
| `inter-services-pub-sub/appsettings.json` | `Message_Queue_Processing_Capacity`, `Message_Queue_Pull_Timeout_Milliseconds` | Intended queue settings; see known limitations below. |
| `index-retrieve/appsettings.json` | `Maximum_Search_Documents`, `Concurrent_Search_Requests` | Result cap and load-test loop count. |
| | `Context_Aware_Keywords`, title/body/phrase rewards, `Refinements_Enabled` | Optional query-time boosts. |

The crawler and indexer point to the queue using `Inter_Services_Pub_Sub_Url` (default `http://localhost:5081`). See the project READMEs for the full stage-by-stage walkthrough and specific configuration values.

## Current implementation limitations

- **Volatile storage:** `IndexWriterFactory` uses `RAMDirectory`; the index disappears when `index-retrieve` exits. The queue is also in process memory.
- **Queue initialization mismatch:** Startup initializes `MessageQueue<EncryptedPage>`, while HTTP `push` and `pull` use `MessageQueue<string>`. These are separate generic static instances. As written, the HTTP queue gets lazily initialized with the class defaults (capacity `100`, pull timeout `200 ms`) on first push; a pull before that can fail. The queue appsettings capacity of `10000` is therefore not applied to the HTTP string queue.
- **Single in-flight response slot:** `index-retrieve/Services/OutputService.cs` has one `TaskCompletionSource` field rather than per-request correlation. Overlapping search requests can replace each other's completion slot and may hang or receive mismatched results; use one request at a time for manual testing.
- **Scoring separation:** Crawl-time page scores are computed/stored but not used to reprioritize discovered URLs or directly added to retrieval scores. Search results are sorted by Lucene scores.
- **Quiescence heuristic:** The crawler's automatic-stop monitor checks only the frontier heap count, not every pipeline channel or in-flight request. It can stop while work remains elsewhere.
- **Prototype behavior:** Queue messages are consumed once, with no persistence or acknowledgment/replay mechanism. The crawler's HTTP output logs a full-queue response but does not re-enqueue that document.

These are descriptions of the checked-in implementation, not guarantees of production behavior. No automated test suite is included in this repository.

## Further reading

- [Crawler: pipeline, scoring, politeness and endpoints](crawl/README.md)
- [Index & Retrieve: ingestion, Lucene query processing and API](index-retrieve/README.md)
- [Inter-service queue: wire format, push/pull behavior and constraints](inter-services-pub-sub/README.md)
- [Shared: records, metrics, and queue internals](shared/README.md)

See [LICENSE](LICENSE) for the project's license terms.
