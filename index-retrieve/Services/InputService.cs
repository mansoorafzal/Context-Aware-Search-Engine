using Microsoft.Extensions.Options;
using shared;
using System.Text.Json;

namespace index_retrieve.Services {
    public class InputService(ILogger<InputService> logger, IOptions<Configurations> options, ChannelBus bus, IHttpClientFactory httpClientFactory) : PipelineServiceBase(logger, options, bus) {
        private readonly ILogger<InputService> _logger = logger;
        private readonly HttpClient _httpClient = httpClientFactory.CreateClient("InterServicesClient");

        protected override Task ExecuteAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Input Service is starting!");
            return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => {}, TaskContinuationOptions.OnlyOnCanceled);
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            _logger.LogDebug("Input Service is up and running!");

            using var workers = new SemaphoreSlim(_configurations.Concurrent_Workers);
            var tasks = new List<Task>();

            try {
                var index = Task.Run(async () => {
                    while (!cancellationToken.IsCancellationRequested) {
                        using var response = await _httpClient.GetAsync("/api/v1/pull", cancellationToken);

                        if (response != null && response.StatusCode == System.Net.HttpStatusCode.OK) {
                            var content = await response.Content.ReadAsStringAsync(cancellationToken);

                            if (content != null) {
                                var encryptedPage = JsonSerializer.Deserialize<EncryptedPage>(content);

                                if (encryptedPage != null) {
                                    await _documentIngestorChannel.Writer.WriteAsync(encryptedPage, cancellationToken);
                                    Metrics.RecordFirstInput();
                                }
                            }
                        }

                        await Task.Delay(100, cancellationToken);
                    }
                }, cancellationToken);

                var retrieve = Task.Run(async () => {
                    await foreach (var input in _inputChannel.Reader.ReadAllAsync(cancellationToken)) {
                        await workers.WaitAsync(cancellationToken);

                        var task = Task.Run(async () => {
                            try {
                                _logger.LogDebug("Input Service recieves work!");
                                await _queryParserChannel.Writer.WriteAsync(input, cancellationToken);
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
                }, cancellationToken);

                await Task.WhenAll(retrieve, index);
            }
            catch (OperationCanceledException) {
                _logger.LogInformation("Input Service is shutting down!");
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
            }
        }

        public async Task EnqueueAsync(string input, CancellationToken cancellationToken) {
            if (string.IsNullOrEmpty(input)) {
                return;
            }

            await _inputChannel.Writer.WriteAsync(input, cancellationToken);
        }
    }
}
