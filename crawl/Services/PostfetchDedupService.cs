using Microsoft.Extensions.Options;
using shared;
using SimhashLib;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace crawl.Services {
    public class PostfetchDedupService(ILogger<PostfetchDedupService> logger, IOptions<Configurations> options, ChannelBus bus) : PipelineServiceBase(logger, options, bus) {
        private readonly ILogger<PostfetchDedupService> _logger = logger;
        private static readonly Simhash _simhash = new();
        private static readonly ConcurrentDictionary<string, bool> _seenSha256 = new();
        private static readonly ConcurrentDictionary<ulong, bool> _seenSimhash = new();

        protected override Task ExecuteAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Post-fetch Dedup Service is starting!");
            return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled);
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            _logger.LogDebug("Post-fetch Dedup Service is up and running!");

            using var workers = new SemaphoreSlim(_configurations.Concurrent_Workers);
            var tasks = new List<Task>();

            try {
                await foreach (var extractedPage in _postfetchDedupChannel.Reader.ReadAllAsync(cancellationToken)) {
                    await workers.WaitAsync(cancellationToken);

                    var task = Task.Run(async () => {
                        try {
                            _logger.LogDebug("Post-fetch Dedup Service recieves work!");

                            var sha256 = ComputeSha256(extractedPage.Body);

                            if (!_seenSha256.TryAdd(sha256, true)) {
                                Metrics.RecordDuplicatePages();
                                return;
                            }

                            var simhash = _simhash.ComputeHashByMurmurHash3(extractedPage.Body).Value;

                            foreach (var (key, _) in _seenSimhash) {
                                if (Distance(simhash, key) <= _configurations.Near_Duplicate_Threshold) {
                                    Metrics.RecordNearDuplicatePages();
                                    return;
                                }
                            }

                            _seenSimhash.TryAdd(simhash, true);

                            var encryptedPage = new EncryptedPage(
                                ExtractedPage: extractedPage,
                                Sha256: sha256,
                                Simhash: simhash
                            );

                            await _outputChannel.Writer.WriteAsync(encryptedPage, cancellationToken);

                            foreach (var outboundLink in extractedPage.OutboundLinks) {
                                var rankedLink = new RankedLink(Uri: outboundLink, Score: 0, Depth: extractedPage.FetchedPage.RankedLink.Depth + 1, Retry: 0);
                                await _scopeFilterChannel.Writer.WriteAsync(rankedLink, cancellationToken);
                            }
                        }

                        catch (OperationCanceledException) {
                            _logger.LogDebug("Post-fetch Dedup Service is cancelling its worker!");
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
                _logger.LogDebug("Post-fetch Dedup Service is down and exiting!");
            }
            catch (OperationCanceledException) {
                _logger.LogInformation("Post-fetch Dedup Service is shutting down!");
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
            }
        }

        private static string ComputeSha256(string content) {
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            byte[] hashBytes = SHA256.HashData(bytes);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        private static int Distance(ulong x, ulong y) {
            ulong v = x ^ y;
            int count = 0;
            
            while (v != 0) {
                v &= v - 1;
                count++;
            }

            return count;
        }
    }
}
