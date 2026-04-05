using Lucene.Net.Documents;
using Lucene.Net.Search;
using System.Text.Json.Serialization;

namespace shared {
    public record RankedLink(
        Uri Uri,
        int Score,
        int Depth,
        int Retry
    );
    
    public record FetchedPage(
        RankedLink RankedLink,
        int StatusCode,
        string Content,
        Dictionary<string, string> Headers,
        TimeSpan Latency
    );
    
    public record ExtractedPage(
        FetchedPage FetchedPage,
        string Metadata,
        string Title,
        string Body,
        IEnumerable<Uri> OutboundLinks,
        float Score,
        int Bytes
    );

    public record EncryptedPage(
        ExtractedPage ExtractedPage,
        string Sha256,
        ulong Simhash
    );

    public record IndexedDocument(
        string Id,
        Document Document
    );

    public record QueryDocument(
        string[] Terms,
        Query Query,
        PhraseQuery[] PhraseQueries,
        BooleanQuery BooleanQuery
    );

    public record CandidateDocument(
        QueryDocument QueryDocument,
        IndexSearcher IndexSearcher,
        TopDocs TopDocs
    );

    public record RankedDocument(
        string Url,
        string Title,
        float Score
    );

    public record CrawlData(
        [property: JsonPropertyName("Mode")]
        string Mode,

        [property: JsonPropertyName("Status")]
        string Status,

        [property: JsonIgnore]
        [property: JsonPropertyName("Running Time (Seconds)")]
        double RunningSeconds,

        [property: JsonIgnore]
        [property: JsonPropertyName("Running Time (Minutes)")]
        double RunningMinutes,

        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        [property: JsonPropertyName("Elapsed Time (Seconds)")]
        double ElapsedSeconds,

        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        [property: JsonPropertyName("Elapsed Time (Minutes)")]
        double ElapsedMinutes,

        [property: JsonPropertyName("Average Network Latency (Milliseconds)")]
        double AverageLatencyMilliseconds,

        [property: JsonIgnore]
        [property: JsonPropertyName("Total Extracted Pages")]
        double TotalExtractedPages,

        [property: JsonPropertyName("Average Page Size (KB)")]
        double AveragePageSizeKiloBytes,

        [property: JsonPropertyName("Total Downloaded Content (MB)")]
        double TotalDownloadedContentMegaBytes,

        [property: JsonIgnore]
        [property: JsonPropertyName("Total Duplicate Pages")]
        double TotalDuplicatePages,

        [property: JsonIgnore]
        [property: JsonPropertyName("Total Near Duplicate Pages")]
        double TotalNeaDuplicatePages,

        [property: JsonPropertyName("Total Crawled Pages")]
        double TotalOutput,

        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        [property: JsonPropertyName("Average Crawl Speed (Pages / Second)")]
        double AverageOutputPerSecond,

        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        [property: JsonPropertyName("Average Crawl Speed (Pages / Minute)")]
        double AverageOutputPerMinute
    );

    public record IndexRetrieveData(
        [property: JsonPropertyName("Mode")]
        string Mode,

        [property: JsonPropertyName("Status")]
        string Status,

        [property: JsonIgnore]
        [property: JsonPropertyName("Running Time (Seconds)")]
        double RunningSeconds,

        [property: JsonIgnore]
        [property: JsonPropertyName("Running Time (Minutes)")]
        double RunningMinutes,

        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        [property: JsonPropertyName("Elapsed Time (Seconds)")]
        double ElapsedSeconds,

        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        [property: JsonPropertyName("Elapsed Time (Minutes)")]
        double ElapsedMinutes,

        [property: JsonPropertyName("Total Indexed Documents")]
        double TotalOutput,

        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        [property: JsonPropertyName("Average Index Speed (Pages / Second)")]
        double AverageOutputPerSecond,

        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        [property: JsonPropertyName("Average Index Speed (Pages / Minute)")]
        double AverageOutputPerMinute,

        [property: JsonPropertyName("Average Network Latency (Milliseconds)")]
        double AverageLatencyMilliseconds,

        [property: JsonPropertyName("Retrieval Latency P95 (Milliseconds)")]
        double P95Milliseconds
    );
}
