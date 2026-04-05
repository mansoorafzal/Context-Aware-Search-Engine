using Microsoft.Extensions.Options;
using shared;
using System.Collections.Concurrent;

namespace crawl.Services {
    public class FrontierService(ILogger<FrontierService> logger, IOptions<Configurations> options, ChannelBus bus) : PipelineServiceBase(logger, options, bus) {
        private readonly ILogger<FrontierService> _logger = logger;
        private readonly PriorityQueue<RankedLink, int> _maxHeap = new();
        private readonly PriorityQueue<(RankedLink RankedLink, long Backoff), long> _minHeap = new();
        private readonly ConcurrentDictionary<Uri, bool> _maxHeapConcurreny = new();
        private readonly ConcurrentDictionary<Uri, bool> _minHeapConcurreny = new();

        protected override Task ExecuteAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Frontier Service is starting!");
            return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => {}, TaskContinuationOptions.OnlyOnCanceled);
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            _logger.LogDebug("Frontier Service is up and running!");

            using var workers = new SemaphoreSlim(_configurations.Concurrent_Workers);
            var tasks = new List<Task>();

            try {
                var read = Task.Run(async () => {
                    await foreach (var rankedLink in _frontierChannel.Reader.ReadAllAsync(cancellationToken)) {
                        await workers.WaitAsync(cancellationToken);

                        var task = Task.Run(() => {
                            try {
                                _logger.LogDebug("Frontier Service recieves work!");

                                if (rankedLink.Retry == 0) {
                                    if (_maxHeapConcurreny.TryAdd(rankedLink.Uri, true)) {
                                        _maxHeap.Enqueue(rankedLink, -rankedLink.Score + rankedLink.Depth);
                                    }
                                }
                                else {
                                    if (_minHeapConcurreny.TryAdd(rankedLink.Uri, true)) {
                                        var jitter = new Random().NextDouble() * 0.5;
                                        var delay = TimeSpan.FromSeconds((int)Math.Pow(2, rankedLink.Retry) + jitter);
                                        var backoff = DateTime.UtcNow.Add(delay).Ticks;
                                        _minHeap.Enqueue((rankedLink, backoff), backoff);
                                    }
                                }
                            }
                            catch (OperationCanceledException) {
                                _logger.LogDebug("Frontier Service is cancelling its worker!");
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
                }, cancellationToken);

                var max = Task.Run(async () => {
                    while (!cancellationToken.IsCancellationRequested) {
                        RankedLink? rankedLink = null;

                        lock (_maxHeap) {
                            if (_maxHeap.Count > 0) {
                                rankedLink = _maxHeap.Dequeue();
                            }
                        }

                        if (rankedLink != null) {
                            _maxHeapConcurreny.TryRemove(rankedLink.Uri, out var _);
                            await _fetcherChannel.Writer.WriteAsync(rankedLink, cancellationToken);
                        }

                        await Task.Delay(100, cancellationToken);
                    }
                }, cancellationToken);

                var min = Task.Run(async () => {
                    while (!cancellationToken.IsCancellationRequested) {
                        RankedLink? rankedLink = null;

                        lock (_minHeap) {
                            if (_minHeap.Count > 0 && _minHeap.Peek().Backoff <= DateTime.UtcNow.Ticks) {
                                (rankedLink, _) = _minHeap.Dequeue();
                            }
                        }

                        if (rankedLink != null) {
                            _minHeapConcurreny.TryRemove(rankedLink.Uri, out var _);
                            await _fetcherChannel.Writer.WriteAsync(rankedLink, cancellationToken);
                        }

                        await Task.Delay(100, cancellationToken);
                    }
                }, cancellationToken);

                await Task.WhenAll(read, max, min);
                _logger.LogDebug("Frontier Service is down and exiting!");
            }
            catch (OperationCanceledException) {
                _logger.LogInformation("Frontier Service is shutting down!");
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
            }
        }

        public int Count { 
            get { return _maxHeap.Count + _minHeap.Count; } 
        }
    }
}
