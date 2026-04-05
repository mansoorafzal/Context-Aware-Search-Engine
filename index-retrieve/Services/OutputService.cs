using Microsoft.Extensions.Options;
using shared;

namespace index_retrieve.Services {
    public class OutputService(ILogger<OutputService> logger, IOptions<Configurations> options, ChannelBus bus) : PipelineServiceBase(logger, options, bus) {
        private readonly ILogger<OutputService> _logger = logger;
        private TaskCompletionSource<IEnumerable<RankedDocument>>? _taskCompletionSource;
		private readonly object _lock = new();

        protected override Task ExecuteAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Output Service is starting!");
            return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => {}, TaskContinuationOptions.OnlyOnCanceled);
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            _logger.LogDebug("Output Service is up and running!");

            using var workers = new SemaphoreSlim(_configurations.Concurrent_Workers);
            var tasks = new List<Task>();

            try {
                await foreach (var rankedDocumentList in _outputChannel.Reader.ReadAllAsync(cancellationToken)) {
                    await workers.WaitAsync(cancellationToken);

                    var task = Task.Run(() => {
                        try {
                            _logger.LogDebug("Output Service recieves work!");
                            TaskCompletionSource<IEnumerable<RankedDocument>>? taskCompletionSource;

                            lock (_lock) {
                                taskCompletionSource = _taskCompletionSource;
                                _taskCompletionSource = null;
                            }

                            if (taskCompletionSource != null && !taskCompletionSource.Task.IsCompleted) {
                                taskCompletionSource.SetResult(rankedDocumentList);
                            }
                        }
                        catch (OperationCanceledException) {
                            _logger.LogDebug("Output Service is cancelling its worker!");
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
                _logger.LogDebug("Output Service is down and exiting!");
            }
            catch (OperationCanceledException) {
                _logger.LogInformation("Output Service is shutting down!");
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
            }
        }

        public Task<IEnumerable<RankedDocument>> WaitAsync() {
			lock (_lock) {
				_taskCompletionSource = new TaskCompletionSource<IEnumerable<RankedDocument>>(TaskCreationOptions.RunContinuationsAsynchronously);
			}

			return _taskCompletionSource.Task;
		}
    }
}
