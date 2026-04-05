using Microsoft.Extensions.Options;
using shared;

namespace crawl.Services {
    public class InputService(ILogger<InputService> logger, IOptions<Configurations> options, ChannelBus bus) : PipelineServiceBase(logger, options, bus) {
        private readonly ILogger<InputService> _logger = logger;

        protected override Task ExecuteAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Input Service is starting!");
            return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => {}, TaskContinuationOptions.OnlyOnCanceled);
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            _logger.LogDebug("Input Service is up and running!");

            using var workers = new SemaphoreSlim(_configurations.Concurrent_Workers);
            var tasks = new List<Task>();

            try {
                await foreach (var seed in _inputChannel.Reader.ReadAllAsync(cancellationToken)) {
                    await workers.WaitAsync(cancellationToken);

                    var task = Task.Run(async () => {
                        try {
                            _logger.LogDebug("Input Service recieves work!");

                            if (Uri.TryCreate(seed, UriKind.Absolute, out var uri)) {
                                await _scopeFilterChannel.Writer.WriteAsync(new RankedLink(Uri: uri, Score: 0, Depth: 0, Retry: 0), cancellationToken);
                                Metrics.RecordFirstInput();
                            }
                        }
                        catch (OperationCanceledException) {
                            _logger.LogDebug("Input Service is cancelling its worker!");
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
                _logger.LogDebug("Input Service is down and exiting!");
            }
            catch (OperationCanceledException) {
                _logger.LogInformation("Input Service is shutting down!");
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
            }
        }

        public async Task EnqueueAsync(string seed, CancellationToken cancellationToken) {
            await _inputChannel.Writer.WriteAsync(seed, cancellationToken);
        }
    }
}
