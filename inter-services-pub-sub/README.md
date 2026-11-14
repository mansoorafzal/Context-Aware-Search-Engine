# Inter-services Pub-Sub (HTTP queue)

`inter-services-pub-sub` is the message handoff service between `crawl` and `index-retrieve`. It exposes a small HTTP API to push a serialized page message into an in-memory queue and to pull one message out. With the supplied `http` launch profile, it listens on **http://localhost:5081**.

**Despite the name, this implementation is a work queue, not a publish/subscribe fan-out broker.** A successful pull removes an item; messages are not broadcast to multiple subscribers. See the [root README](../README.md) for the end-to-end architecture.

## Data flow

```text
crawl/OutputService
      |
      | POST /api/v1/push
      | JSON string containing serialized EncryptedPage
      v
inter-services-pub-sub
      |
      | shared.MessageQueue<string> / BlockingCollection<string>
      v
      | GET /api/v1/pull
      v
index-retrieve/InputService
      |
      | JsonSerializer.Deserialize<EncryptedPage>(response body)
      v
Indexing pipeline
```

`crawl/Services/OutputService.cs` serializes an `EncryptedPage` into a string using `JsonSerializer.Serialize` and then posts that **string** using `PostAsJsonAsync`. The controller receives it as `[FromBody] string? item`, calls `MessageQueue<string>.Push(item)`, and returns `202 Accepted`. The indexer polls `/pull` and reads the item back for deserialization.

`EncryptedPage` is defined in [`shared/Records.cs`](../shared/README.md). It wraps an `ExtractedPage`, its underlying fetched page, and hashes used for deduplication.

## HTTP API

All routes are defined in `Controllers/HomeController.cs` under `/api/v1`.

| Method | Route | Input | Result |
| --- | --- | --- | --- |
| `POST` | `/push` | JSON **string** body | `202 Accepted` if enqueued; `400` for `null`; `503` on a caught queue capacity exception; `500` for other exceptions. |
| `GET` | `/pull` | None | `200 OK` with a dequeued string, `204 No Content` on a poll timeout, or `500` for errors. |

For a simple connectivity check, the request body must be a JSON-encoded string, including its surrounding quotes:

```bash
curl -i -X POST http://localhost:5081/api/v1/push \
  -H 'Content-Type: application/json' \
  --data '"hello from the queue"'

curl -i http://localhost:5081/api/v1/pull
```

The example pushes test text only. The real indexer expects the returned text to contain valid serialized `EncryptedPage` JSON. Do not use arbitrary test messages while the indexer is consuming production crawl messages.

## Implementation

- `Program.cs` binds the local `Configurations` section, registers controllers and Swagger, calls `app.UseInitializations()`, then maps the controllers.
- `Extensions/ApplicationBuilderExtension.cs` obtains the configured capacity and pull timeout and calls `MessageQueue<EncryptedPage>.Initialize(...)`.
- `Controllers/HomeController.cs` uses **`MessageQueue<string>`** for the actual HTTP push and pull endpoints.
- `shared/MessageQueue.cs` implements a static generic queue backed by `BlockingCollection<T>` and a `ConcurrentQueue<T>`. `Push` calls `Add`; `Pull` calls `TryTake` using a configured timeout. Each `T` has its own static fields and queue instance.

### Important queue initialization mismatch

The startup initialization uses `MessageQueue<EncryptedPage>` but the endpoints use `MessageQueue<string>`. Since static generic fields are separate for each type, these calls **do not configure the same queue**.

As written:

- The HTTP string queue is never explicitly initialized. `Push` checks `_isInitialized`, finds it `false` on **every** call (the lazy path never sets it), and creates a new `BlockingCollection` with the defaults (capacity `100`, timeout `200 ms`). Each push therefore replaces the queue and **discards any messages not yet pulled**. Under load, pages can be lost between the crawler and the indexer.
- `GET /pull` before that first push can throw because the string queue has not been initialized.
- `Message_Queue_Processing_Capacity = 10000` from appsettings is applied to the unused `EncryptedPage` queue, **not** to the HTTP string queue.
- The configuration's default timeout is also `200 ms`; changing it in appsettings would not affect the actual string queue without fixing the type mismatch.

These are limitations of the checked-in code, not intended design guarantees.

## Configuration

`inter-services-pub-sub/appsettings.json` contains:

| Key | Default | Intended meaning |
| --- | --- | --- |
| `Message_Queue_Processing_Capacity` | `10000` | Maximum queued items. Currently initialized for `MessageQueue<EncryptedPage>`, not the string queue used by the API. |
| `Message_Queue_Pull_Timeout_Milliseconds` | `200` | Timeout for a `TryTake` poll. Subject to the same type mismatch. |

The `http` launch profile in `Properties/launchSettings.json` sets `ASPNETCORE_ENVIRONMENT=Development`, which enables Swagger at `http://localhost:5081/swagger`.

## Run the service

From the solution root:

```bash
dotnet run --project inter-services-pub-sub --launch-profile http
```

Run it before the crawler and indexer. Both of those services use `Inter_Services_Pub_Sub_Url` (default `http://localhost:5081`) to send or receive page messages.

## Limitations and operational notes

- **Not durable:** Messages are stored only in process memory and disappear when the service exits.
- **Not fan-out:** A message is consumed by a single successful pull. There are no subscriptions, consumer groups, acknowledgments, or replay APIs.
- **No persistence or dead-letter handling:** Delivery/recovery is not guaranteed after process failures.
- **Queue configuration bug:** The `EncryptedPage`/`string` generic type mismatch above must be resolved before relying on configured capacity or startup-time initialization.
- **Full-queue handling:** The underlying `BlockingCollection<T>.Add` blocks when full in its standard behavior. The controller's `InvalidOperationException` catch is not a reliable nonblocking full-queue signal. With the initialization mismatch above, the queue is replaced on every push and does not fill in practice.

For the producer and consumer pipelines, see [crawl/README.md](../crawl/README.md) and [index-retrieve/README.md](../index-retrieve/README.md).
