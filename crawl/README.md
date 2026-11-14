# Crawl service

The `crawl` ASP.NET Core service discovers, downloads and processes HTML pages. It starts from URLs configured in `Seeds`, follows only hosts accepted by `Domain_Scopes`, removes duplicate URLs and page bodies, and posts processed pages to `inter-services-pub-sub` for indexing. Its controller runs on **http://localhost:5082** with the supplied `http` launch profile.

For the end-to-end architecture and startup order, see the [root README](../README.md).

## Execution model

`Program.cs` binds the `Configurations` section of `appsettings.json`, registers a singleton `PipelineOrchestrator`, and registers each pipeline stage as a hosted service. The hosted services themselves remain idle until `POST /api/v1/start` causes the orchestrator to launch their `RunAsync` loops with a shared cancellation token.

```text
Configured seeds
      |
 InputService                     input string -> RankedLink(depth=0, score=0)
      |
 ScopeFilterService              host suffix filter
      |
 PrefetchDedupService            canonical URL + Bloom filter + concurrent set
      |
 FrontierService                 priority queue and delayed-retry queue
      |
 FetcherService                  robots rules / per-host delay / HTTP HTML download
      |
 ExtractorService                metadata, title, body, links, score
      |
 PostfetchDedupService           SHA-256 exact body and Simhash near-duplicates
      |              |
      |              +------------------- outbound URLs back to ScopeFilter
      v
 OutputService                   HTTP POST /api/v1/push -> queue service
```

`Services/ChannelBus.cs` links stages using `System.Threading.Channels`. The fetcher input, extractor input, and output channels are **bounded** by `Channel_Processing_Capacity`; several other channels are unbounded. Each stage uses up to `Concurrent_Workers` asynchronous workers and reads/writes records from `shared/Records.cs`.

## Pipeline walkthrough

| Stage / source file | What it does |
| --- | --- |
| `Services/InputService.cs` | Reads seed strings, validates absolute `Uri` values and creates `RankedLink` records with `Score = 0`, `Depth = 0`, `Retry = 0`. |
| `Services/ScopeFilterService.cs` | Only forwards links whose `Uri.Host.EndsWith(...)` one of the configured `Domain_Scopes` entries. This is a literal suffix check; verify your domain scope carefully. |
| `Services/PrefetchDedupService.cs` | Canonicalizes URLs by removing fragments and explicit ports, lowercasing, and trimming trailing slashes. Combines a Bloom filter with a `ConcurrentDictionary` of seen URLs to reject repeat URLs. |
| `Services/FrontierService.cs` | Adds fresh URLs to a priority queue ordered by `-Score + Depth` (lower priority number dequeues first). Retries go to a separate queue with roughly `2^Retry` seconds of backoff plus jitter. Both queues feed the fetcher. |
| `Services/FetcherService.cs` | Checks the service's `robots.txt` rules (when enabled), applies a host delay, requests pages via `HttpClient`, and sends successful `text/html` responses to extraction. Handles selected HTTP redirect and 5xx response codes in its own branch logic. |
| `Services/ExtractorService.cs` | Parses HTML with HtmlAgilityPack and AngleSharp; extracts metadata, title, `body` text nodes (including any script/style content inside `<body>`) and HTTP(S) outbound links; measures HTML size and computes the optional contextual page score. |
| `Services/PostfetchDedupService.cs` | Hashes the extracted **body** with SHA-256 to detect exact duplicates, then uses Simhash and Hamming distance for near-duplicates. For unique pages, it emits an `EncryptedPage` and sends discovered links back to the scope filter at `Depth + 1`. |
| `Services/OutputService.cs` | JSON-serializes the `EncryptedPage` and sends the resulting **string** with `PostAsJsonAsync` to the queue's `/api/v1/push`; records successful outbound messages. |

### Politeness and retries

`Politeness/Robot.cs` and `RobotRule.cs` parse the `User-agent: *` section of `robots.txt`, support `Allow` and `Disallow` paths (including `*` and `$` patterns), and choose the longest matching rule. `FetcherService` caches the parsed rules by host and maintains last-request timestamps per host. The `robots.txt` `Crawl-delay` directive is not read, so in practice every host gets a fixed delay of `Crawl_Delay_Floor_Seconds` between requests; the ceiling only clamps that value.

The fetcher schedules retries for **HTTP 500, 502, 503 and 504** results by returning a higher-`Retry` link to the frontier. Because its `HttpClient` uses default redirect handling, HTTP redirects may be automatically followed before the explicit redirect-handling code sees them. There is no configured maximum retry count.

### Duplicate detection

- URL deduplication happens **before fetching** and is based on a normalized URL representation.
- Exact content deduplication happens **after extraction**, using SHA-256 of the extracted body text (not the raw HTML).
- Near-duplicate detection compares Simhash values against previously seen hashes and drops a page when their Hamming distance is at or below `Near_Duplicate_Threshold` (default `2`).
- Only pages surviving post-fetch deduplication contribute outbound links back into the discovery loop.

## Context-aware crawl scoring

With `Refinements_Enabled = true`, `ExtractorService.ComputeScore` calculates:

```text
pageScore = heuristicScore + contextualBodyScore + contextualHostScore

heuristicScore = max(1, bodyCharacterCount / 100 - depth * Depth_Penalty)
contextualBodyScore = Context_Aware_Body_Reward * number of configured
                      body keywords found at least once in page body
contextualHostScore = Context_Aware_Host_Reward * number of configured
                      host keywords found in hostname
```

The checks for body and host keywords are case-insensitive substring matches. A keyword earns its reward once per page, not once per occurrence. If the body is empty, the score is zero. With `Refinements_Enabled = false`, extracted pages receive a score of zero.

The value is stored in `ExtractedPage.Score` and later in the Lucene document's `score` field. **In the current implementation, newly discovered links are always enqueued with `Score = 0`; this contextual page score does not feed back into frontier priority, and it is not blended into Lucene result scores.**

## API

All routes are under `/api/v1`:

| Method | Path | Behavior |
| --- | --- | --- |
| `POST` | `/start` | Launch all eight stages and enqueue configured seeds. `200` started, `208` already running, `400` missing seeds. |
| `POST` | `/stop` | Cancel stage tasks; `200` stopped or `208` already stopped. |
| `GET` | `/status` | Return crawl mode, pipeline status, elapsed time, throughput, downloaded size and average latency metrics. |

Example:

```bash
curl -X POST http://localhost:5082/api/v1/start
curl http://localhost:5082/api/v1/status
curl -X POST http://localhost:5082/api/v1/stop
```

Start `inter-services-pub-sub` and `index-retrieve` (including the indexer's `POST /api/v1/start`) before triggering a crawl if you want documents to flow all the way to search.

## Configuration

Edit `Configurations` in `crawl/appsettings.json` (or override with ASP.NET Core configuration). Checked-in defaults include:

| Setting | Default | Usage |
| --- | --- | --- |
| `Inter_Services_Pub_Sub_Url` | `http://localhost:5081` | Destination for completed page messages. |
| `Seeds` | Georgia Tech home and computing sites | Initial URLs supplied to `InputService`. |
| `Domain_Scopes` | `["gatech.edu"]` | Accepted hostname suffixes. |
| `User_Agent` | `case-crawl` | HTTP `User-Agent`. |
| `Http_Client_Request_Timeout_Seconds` | `200` | Timeout for both named HTTP clients. |
| `Concurrent_Workers` | `15` | Maximum concurrent workers per stage. |
| `Channel_Processing_Capacity` | `100` | Capacity of selected bounded channels. |
| `Bloom_Filter_Capacity` | `10000000` | Bloom filter capacity. |
| `Bloom_Filter_False_Positive_Probability` | `0.001` | Bloom filter target false-positive rate. |
| `Depth_Penalty` | `2` | Heuristic deduction for deeper pages. |
| `Context_Aware_Body_Reward` / `Context_Aware_Host_Reward` | `5.0` / `10.0` | One-time rewards per matching configured keyword. |
| `Near_Duplicate_Threshold` | `2` | Maximum Simhash Hamming distance treated as near-duplicate. |
| `Politeness_Enabled` | `true` | Enable robots rules and host-based delays. |
| `Crawl_Delay_Floor_Seconds` / `Crawl_Delay_Ceiling_Seconds` | `1` / `2` | Per-host delay; the floor is the delay actually applied (see Politeness and retries). |
| `Quiescence_Monitor_Enabled` | `true` | Enable automatic stop monitor. |
| `Quiescence_Monitor_Shutdown_Seconds` | `15` | Consecutive seconds of empty frontier before stop. |
| `Refinements_Enabled` | `true` | Enable crawler context scoring. |

See `Context_Aware_Body_Keywords` and `Context_Aware_Host_Keywords` in the same file for the editable keyword arrays.

## Implementation notes and limitations

- `FrontierService` enqueues into its two `PriorityQueue` instances from concurrent workers without a lock, while dequeuing under a lock. `PriorityQueue` is not thread-safe, so concurrent enqueues can corrupt queue state.
- `QuiescenceMonitor` checks only `FrontierService.Count` once per second. In-flight fetches, other channels, or pending outputs do not count toward the condition, so an automatic stop can be premature.
- The host restriction is `Host.EndsWith(scope)` without a dot boundary, and does not independently restrict URI scheme. Choose safe, intended seeds and scope values.
- The `robots.txt` parser implements a limited subset of the standard (primarily wildcard user-agent rules); do not assume complete robots.txt support.
- `OutputService` logs `503 Service Unavailable` for a full queue but does not retry or re-enqueue that page after its short delay.
- The in-memory seen-URL and seen-content data remain in the running process; there is no durable crawl checkpoint or state recovery mechanism.
- `Services/ChannelBus.cs` includes unbounded channels; the system should not be described as end-to-end bounded.

### Relevant source entry points

`Program.cs` -> `Extensions/ServiceCollectionExtension.cs` -> `Controllers/HomeController.cs` -> `Orchestrators/PipelineOrchestrator.cs` -> `Services/*`.

The shared record definitions and metrics used here are documented in [shared/README.md](../shared/README.md).
