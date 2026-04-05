using crawl.Politeness;
using Microsoft.Extensions.Options;
using shared;
using System.Collections.Concurrent;

namespace crawl.Services {
    public class FetcherService(ILogger<FetcherService> logger, IOptions<Configurations> options, ChannelBus bus, IHttpClientFactory httpClientFactory) : PipelineServiceBase(logger, options, bus) {
        private readonly ILogger<FetcherService> _logger = logger;
        private readonly HttpClient _httpClient = httpClientFactory.CreateClient("CrawlClient");
        private readonly ConcurrentDictionary<string, (Robot?, int)> _robots = new();
        private readonly ConcurrentDictionary<string, DateTime> _hosts = new();

        protected override Task ExecuteAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Fetcher Service is starting!");
            return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => {}, TaskContinuationOptions.OnlyOnCanceled);
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            _logger.LogDebug("Fetcher Service is up and running!");

            await LoadRobotsAsync(cancellationToken);
            using var workers = new SemaphoreSlim(_configurations.Concurrent_Workers);
            var tasks = new List<Task>();

            try {
                await foreach (var rankedLink in _fetcherChannel.Reader.ReadAllAsync(cancellationToken)) {
                    await workers.WaitAsync(cancellationToken);

                    var task = Task.Run(async () => {
                        try {
                            _logger.LogDebug("Fetcher Service recieves work!");
                            (bool isAllowed, int delaySeconds) = await GetPolitnessParametersAsync(rankedLink.Uri, cancellationToken);

                            if (!isAllowed) {
                                return;
                            }

                            if (delaySeconds > 0 && _hosts.TryGetValue(rankedLink.Uri.Host, out var dateTime)) {
                                var elapsed = DateTime.UtcNow - dateTime;

                                if (elapsed.TotalSeconds < delaySeconds) {
                                    await Task.Delay((delaySeconds - (int)elapsed.TotalSeconds) * 1000, cancellationToken);
                                }
                            }

                            var startTime = DateTime.UtcNow;
                            _hosts[rankedLink.Uri.Host] = startTime;

                            using var response = await _httpClient.GetAsync(rankedLink.Uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                            var contentType = response.Content.Headers.ContentType?.MediaType;

                            if (contentType == null || !contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)) {
                                return;
                            }

                            var content = await response.Content.ReadAsStringAsync(cancellationToken);
                            var headers = response.Content.Headers.ToDictionary(header => header.Key, header => string.Join(", ", header.Value));
                            var latency = DateTime.UtcNow - startTime;
                            var statusCode = (int)response.StatusCode;

                            // 200 - Extract
                            if (statusCode == StatusCodes.Status200OK) {
                                var fetchedPage = new FetchedPage(
                                    RankedLink: rankedLink,
                                    StatusCode: StatusCodes.Status200OK,
                                    Content: content,
                                    Headers: headers,
                                    Latency: latency
                                );

                                await _extractorChannel.Writer.WriteAsync(fetchedPage, cancellationToken);
                            }

                            // 301, 302, 303, 307, 308 - Redirect
                            else if (statusCode == StatusCodes.Status301MovedPermanently || statusCode == StatusCodes.Status302Found || statusCode == StatusCodes.Status303SeeOther
                                || statusCode == StatusCodes.Status307TemporaryRedirect || statusCode == StatusCodes.Status308PermanentRedirect) {
                                if (response.Headers.TryGetValues("Location", out var locations)) {
                                    foreach (var location in locations) {
                                        var uri = new Uri(location);
                                        await _scopeFilterChannel.Writer.WriteAsync(new RankedLink(Uri: uri, Score: 0, Depth: rankedLink.Depth, Retry: 0), cancellationToken);
                                    }
                                }
                            }

                            // 500, 502, 503, 504 - Exponential Backoff
                            else if (statusCode == StatusCodes.Status500InternalServerError || statusCode == StatusCodes.Status502BadGateway || statusCode == StatusCodes.Status503ServiceUnavailable
                                || statusCode == StatusCodes.Status504GatewayTimeout) {
                                await _frontierChannel.Writer.WriteAsync(new RankedLink(Uri: rankedLink.Uri, Score: rankedLink.Score, Depth: rankedLink.Depth, Retry: rankedLink.Retry + 1), cancellationToken);
                            }

                            Metrics.RecordLatency(latency);
                        }
                        catch (OperationCanceledException) {
                            _logger.LogDebug("Fetcher Service is cancelling its worker!");
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
                _logger.LogDebug("Fetcher Service is down and exiting!");
            }
            catch (OperationCanceledException) {
                _logger.LogInformation("Fetcher Service is shutting down!");
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
            }
        }

        private async Task<(bool, int)> GetPolitnessParametersAsync(Uri uri, CancellationToken cancellationToken) {
            if (!_configurations.Politeness_Enabled) {
                return (true, 0);
            }

            Robot? robot = null;
            bool isAllowed = true;
            int delaySeconds = 0;

            if (!_robots.ContainsKey(uri.Host)) {
                try {
                    using var response = await _httpClient.GetAsync($"{uri.GetLeftPart(UriPartial.Authority).TrimEnd('/')}/robots.txt", cancellationToken);

                    if (response.IsSuccessStatusCode && (int)response.StatusCode >= 200 && (int)response.StatusCode <= 299) {
                        string content = await response.Content.ReadAsStringAsync(cancellationToken);
                        robot = Robot.Parse(content);
                    }
                }
                catch (Exception exception) {
                    _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
                }
                finally {
                    _robots[uri.Host] = (robot, Math.Max(delaySeconds, _configurations.Crawl_Delay_Floor_Seconds));
                }
            }

            (robot, delaySeconds) = _robots[uri.Host];

            if (robot != null) {
                isAllowed = robot.IsAllowed(uri);

                if (isAllowed) {
                    delaySeconds = Math.Min(Math.Max(delaySeconds, _configurations.Crawl_Delay_Floor_Seconds), _configurations.Crawl_Delay_Ceiling_Seconds);
                }
            }

            return (isAllowed, delaySeconds);
        }

        private async Task LoadRobotsAsync(CancellationToken cancellationToken) {
            if (!_configurations.Politeness_Enabled) {
                return;
            }

            foreach (var domain in _configurations.Domain_Scopes) {
                try {
                    var host = domain.Trim().TrimEnd('/');

                    if (string.IsNullOrWhiteSpace(host)) {
                        continue;
                    }
                    
                    var baseUri = new Uri($"https://{host}");
                    await GetPolitnessParametersAsync(baseUri, cancellationToken);
                }
                catch (Exception exception) {
                    _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
                }
            }
        }
    }
}
