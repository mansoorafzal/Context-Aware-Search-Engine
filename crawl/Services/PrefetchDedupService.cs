using BloomFilter;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace crawl.Services {
    public class PrefetchDedupService : PipelineServiceBase {
        private readonly ILogger<PrefetchDedupService> _logger;
        private readonly ConcurrentDictionary<string, bool> _seen = [];
        private readonly IBloomFilter _bloomFilter;

        public PrefetchDedupService(ILogger<PrefetchDedupService> logger, IOptions<Configurations> options, ChannelBus bus) : base(logger, options, bus) {
            _logger = logger;
            _bloomFilter = FilterBuilder.Build(_configurations.Bloom_Filter_Capacity, _configurations.Bloom_Filter_False_Positive_Probability);
        }

        protected override Task ExecuteAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Pre-fetch Dedup Service is starting!");
            return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => {}, TaskContinuationOptions.OnlyOnCanceled);
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            _logger.LogDebug("Pre-fetch Dedup Service is up and running!");

            using var workers = new SemaphoreSlim(_configurations.Concurrent_Workers);
            var tasks = new List<Task>();

            try {
                await foreach (var rankedLink in _prefetchDedupChannel.Reader.ReadAllAsync(cancellationToken)) {
                    await workers.WaitAsync(cancellationToken);

                    var task = Task.Run(async () => {
                        try {
                            _logger.LogDebug("Pre-fetch Dedup Service recieves work!");
                            var url = Canonicalize(rankedLink.Uri);

                            if (_bloomFilter.Contains(url) && _seen.ContainsKey(url)) {
                                return;
                            }

                            _bloomFilter.Add(url);

                            if (!_seen.TryAdd(url, true)) {
                                return;
                            }

                            await _frontierChannel.Writer.WriteAsync(rankedLink, cancellationToken);
                        }

                        catch (OperationCanceledException) {
                            _logger.LogDebug("Pre-fetch Dedup Service is cancelling its worker!");
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
                _logger.LogDebug("Pre-fetch Dedup Service is down and exiting!");
            }
            catch (OperationCanceledException) {
                _logger.LogInformation("Pre-fetch Dedup Service is shutting down!");
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
            }
        }

        private static string Canonicalize(Uri uri) {
            var builder = new UriBuilder(uri) {
                Fragment = string.Empty,
                Port = -1
            };

            string canonical = builder.ToString().ToLowerInvariant();

            if (canonical.EndsWith('/')) {
                canonical = canonical.TrimEnd('/');
            }

            return canonical;
        }
    }
}
