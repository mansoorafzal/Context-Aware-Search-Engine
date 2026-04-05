using Microsoft.Extensions.Options;
using shared;
using System.Net;
using System.Text.Json;

namespace crawl.Services {
    public class OutputService(ILogger<OutputService> logger, IOptions<Configurations> options, ChannelBus bus, IHttpClientFactory httpClientFactory) : PipelineServiceBase(logger, options, bus) {
        private readonly ILogger<OutputService> _logger = logger;
        private readonly HttpClient _httpClient = httpClientFactory.CreateClient("InterServicesClient");

        protected override Task ExecuteAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Output Service is starting!");
            return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => {}, TaskContinuationOptions.OnlyOnCanceled);
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            _logger.LogDebug("Output Service is up and running!");

            using var workers = new SemaphoreSlim(_configurations.Concurrent_Workers);
            var tasks = new List<Task>();

            try {
                await foreach (var encryptedPage in _outputChannel.Reader.ReadAllAsync(cancellationToken)) {
                    await workers.WaitAsync(cancellationToken);

                    var task = Task.Run(async () => {
                        try {
                            _logger.LogDebug("Output Service recieves work!");

                            var request = JsonSerializer.Serialize<EncryptedPage>(encryptedPage);
                            var response = await _httpClient.PostAsJsonAsync("/api/v1/push", request, cancellationToken);

                            if (response.IsSuccessStatusCode) {
                                Metrics.RecordLastOutput();
                            }
                            else if (response.StatusCode == HttpStatusCode.ServiceUnavailable) {
                                _logger.LogDebug("Queue is full!");
                                await Task.Delay(100, cancellationToken);

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
    }
}
