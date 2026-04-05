# Context Aware Search Engine (CASE)

`CASE` is a distributed and a pipeline-driven search engine built in .NET. It crawls, processes, ranks, and indexes web content using an event-driven architecture with internal pub-sub communication.

The system is designed for scalability, modularity, performance and with a strong focus on context-aware ranking and efficient crawling.

---

## Architecture Overview

`CASE` is composed of three independent services:

- **Inter-Services Pub-Sub (Port 5081)**  
  Internal communication layer enabling decoupled and event-driven data flow.

- **Crawl Service (Port 5082)**  
  Executes the crawling pipeline: discovery, filtering, fetching, extraction and scoring.

- **Index & Retrieve Service (Port 5080)**  
  Stores processed data and exposes query endpoints for retrieval.

---

## Crawl Pipeline

The crawler is implemented as a multi-stage pipeline using asynchronous channels:

**`Input → Scope Filter → Prefetch Dedup → Frontier → Fetcher → Extractor → Postfetch Dedup → Output`**

- **Input**  
  Seeds initial URLs into the pipeline.

- **Scope Filter**  
  Restricts crawling to allowed domains.

- **Prefetch Deduplication**  
  Prevents duplicate URLs before fetching.

- **Frontier**  
  Priority-based scheduling with:
  - Max-heap for normal URLs
  - Min-heap for retry/backoff handling

- **Fetcher**  
  Handles HTTP requests with:
  - `robots.txt` compliance
  - politeness delays per host
  - redirect handling
  - exponential backoff for failures

- **Extractor**  
  Parses HTML and extracts:
  - Title
  - Metadata (title + description)
  - Body text
  - Outbound links

- **Postfetch Deduplication**  
  Detects near-duplicate pages

- **Output**  
  Publishes processed content to downstream services

---

## Context-Aware Scoring

`CASE` supports intelligent ranking based on content relevance.

Score = Heuristic + Contextual (Body) + Contextual (Host)

### Heuristic Score
- Based on content length and crawl depth
- Penalizes deeper links

### Contextual Body Score
- Matches keywords in page content
- Example:
  - `Artificial Intelligence`
  - `Machine Learning`
  - `Robotics`

### Contextual Host Score
- Rewards specific domains/subdomains

This allows the crawler to prioritize high-value pages dynamically.

---

## Key Features

- **Event-Driven Architecture**  
  Services communicate via pub-sub for loose coupling

- **High Concurrency**  
  Worker-based parallel processing using channels

- **Deduplication**
  - Bloom filter for URL-level deduplication
  - Near-duplicate detection for content

- **Politeness & Compliance**
  - `robots.txt` parsing
  - Crawl delays per host

- **Resilience**
  - Retry with exponential backoff
  - Redirect handling

- **Quiescence Detection**
  - Automatically shuts down when no work remains

- **Metrics Tracking**
  - Throughput
  - Latency
  - Output rate
  - Data volume

---

## Endpoints

Run all services in parallel:

- `Pub-Sub`: http://localhost:5081
- `Crawl`: http://localhost:5082
- `Index & Retrieve`: http://localhost:5080

Then trigger crawling via:
POST http://localhost:5082/api/v1/start

---

## Design Principles

- **Modular** – Each stage is isolated and replaceable  
- **Scalable** – Services and workers scale independently  
- **Efficient** – Bounded channels and async pipelines  
- **Extensible** – Easy to add ranking strategies or new stages  
- **Fault-Tolerant** – Handles retries and failures gracefully  

---

## Summary

`CASE` is not just a crawler — it is a full pipeline-based, context-aware search engine designed for modern distributed systems.

It combines:
- structured pipelines
- intelligent ranking
- high-performance concurrency
- and clean service boundaries

to deliver a solid foundation for scalable search systems.
