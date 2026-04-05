using AngleSharp.Html.Parser;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using shared;
using System.Text;

namespace crawl.Services {
    public class ExtractorService(ILogger<ExtractorService> logger, IOptions<Configurations> options, ChannelBus bus) : PipelineServiceBase(logger, options, bus) {
        private readonly ILogger<ExtractorService> _logger = logger;

        protected override Task ExecuteAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Extractor Service is starting!");
            return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => {}, TaskContinuationOptions.OnlyOnCanceled);
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            _logger.LogDebug("Extractor Service is up and running!");

            using var workers = new SemaphoreSlim(_configurations.Concurrent_Workers);
            var tasks = new List<Task>();

            try {
                await foreach (var fetchedPage in _extractorChannel.Reader.ReadAllAsync(cancellationToken)) {
                    await workers.WaitAsync(cancellationToken);

                    var task = Task.Run(async () => {
                        try {
                            _logger.LogDebug("Extractor Service recieves work!");

                            var htmlDocument = new HtmlDocument();
                            htmlDocument.LoadHtml(fetchedPage.Content);

                            var metadata = ExtractMetadata(fetchedPage.Content);
                            var title = htmlDocument.DocumentNode.SelectSingleNode("//title")?.InnerText.Trim() ?? fetchedPage.RankedLink.Uri.Host;
                            var body = GetBody(htmlDocument);
                            var outboundLinks = ExtractOutboundLinks(htmlDocument, fetchedPage.RankedLink.Uri);
                            var bytes = Encoding.UTF8.GetByteCount(fetchedPage.Content);

                            var extractedPage = new ExtractedPage(
                                FetchedPage: fetchedPage,
                                Metadata: metadata,
                                Title: title,
                                Body: body,
                                OutboundLinks: outboundLinks,
                                Score: 0.0f,
                                Bytes: bytes
                            );

                            if (_configurations.Refinements_Enabled) {
                                extractedPage = extractedPage with { Score = ComputeScore(body, fetchedPage.RankedLink) };
                            }

                            await _postfetchDedupChannel.Writer.WriteAsync(extractedPage, cancellationToken);
                            Metrics.RecordBytes(bytes);
                        }
                        catch (OperationCanceledException) {
                            _logger.LogDebug("Extractor Service is cancelling its worker!");
                        }
                        catch (Exception exception) {
                            _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
                        }
                        finally {
                            workers.Release();
                        }
                    }, cancellationToken);

                    tasks.Add(task);
                }

                await Task.WhenAll(tasks);
                _logger.LogDebug("Extractor Service is down and exiting!");
            }
            catch (OperationCanceledException) {
                _logger.LogInformation("Extractor Service is shutting down!");
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
            }
        }

        private static string GetBody(HtmlDocument htmlDocument) {
            return htmlDocument.DocumentNode.SelectNodes("//body//text()")
                ?.Select(node => node.InnerText.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Aggregate((current, next) => current + " " + next) ?? string.Empty;
        }

        private static List<Uri> ExtractOutboundLinks(HtmlDocument htmlDocument, Uri uri) {
            var links = new List<Uri>();
            var nodes = htmlDocument.DocumentNode.SelectNodes("//a[@href]");

            if (nodes == null) {
                return links;
            }

            foreach (var node in nodes) {
                var href = node.GetAttributeValue("href", string.Empty);

                if (string.IsNullOrEmpty(href) || href.StartsWith('#') || href.StartsWith("mailto:")) {
                    continue;
                }

                if (Uri.TryCreate(uri, href, out var absoluteUri)) {
                    if (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps) {
                        links.Add(absoluteUri);
                    }
                }
            }

            return links;
        }

        private static string ExtractMetadata(string htmlContent) {
            HtmlParser htmlParser = new();
            var document = htmlParser.ParseDocument(htmlContent);
            string title = document.QuerySelector("title")?.TextContent ?? string.Empty;
            string description = document.QuerySelector("meta[name='description']")?.GetAttribute("content") ?? string.Empty;
            
            return $"TITLE: {title}. DESCRIPTION: {description}";
        }

        private float ComputeScore(string body, RankedLink rankedLink) {
            if (string.IsNullOrEmpty(body)) {
                return 0.0f;
            }

            body = body.ToLowerInvariant();
            float heuristicScore = ComputeHeuristicScore(body, rankedLink.Depth);
            float contextualBodyScore = ComputeContextualScore(body, _configurations.Context_Aware_Body_Keywords, _configurations.Context_Aware_Body_Reward);
            float contextualHostScore = ComputeContextualScore(rankedLink.Uri.Host, _configurations.Context_Aware_Host_Keywords, _configurations.Context_Aware_Host_Reward);

            return heuristicScore + contextualBodyScore + contextualHostScore;
        }

        private float ComputeHeuristicScore(string body, int depth) {
            return Math.Max(1.0f, body.Length / 100.0f - depth * _configurations.Depth_Penalty);
        }

        private static float ComputeContextualScore(string text, string[] keywords, float reward) {
            float score = 0.0f;

            foreach (var keyword in keywords) {
                if (text.Contains(keyword, StringComparison.InvariantCultureIgnoreCase)) {
                    score += reward; 
                }
            }

            return score;
        }
    }
}
