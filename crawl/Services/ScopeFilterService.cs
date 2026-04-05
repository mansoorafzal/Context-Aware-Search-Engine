using Microsoft.Extensions.Options;

namespace crawl.Services {
    public class ScopeFilterService(ILogger<ScopeFilterService> logger, IOptions<Configurations> options, ChannelBus bus) : PipelineServiceBase(logger, options, bus) {
        private readonly ILogger<ScopeFilterService> _logger = logger;

        protected override Task ExecuteAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Scope Filter Service is starting!");
            return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => {}, TaskContinuationOptions.OnlyOnCanceled);
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            _logger.LogDebug("Scope Filter Service is up and running!");

            using var workers = new SemaphoreSlim(_configurations.Concurrent_Workers);
            var tasks = new List<Task>();

            try {
                await foreach (var rankedLink in _scopeFilterChannel.Reader.ReadAllAsync(cancellationToken)) {
                    await workers.WaitAsync(cancellationToken);

                    var task = Task.Run(async () => {
                        try {
                            _logger.LogDebug("Scope Filter Service recieves work!");

                            if (!_configurations.Domain_Scopes.Any(domain => rankedLink.Uri.Host.EndsWith(domain))) {
                                return;
                            }

                            await _prefetchDedupChannel.Writer.WriteAsync(rankedLink, cancellationToken);
                        }
                        catch (OperationCanceledException) {
                            _logger.LogDebug("Scope Filter Service is cancelling its worker!");
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
                _logger.LogInformation("Scope Filter Service is down and exiting!");
            }
            catch (OperationCanceledException) {
                _logger.LogDebug("Scope Filter Service is shutting down!");
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
            }
        }
    }
}
