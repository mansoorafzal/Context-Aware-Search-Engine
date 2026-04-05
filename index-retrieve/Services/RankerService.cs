using Microsoft.Extensions.Options;
using shared;

namespace index_retrieve.Services {
    public class RankerService(ILogger<RankerService> logger, IOptions<Configurations> options, ChannelBus bus) : PipelineServiceBase(logger, options, bus) {
        private readonly ILogger<RankerService> _logger = logger;

        protected override Task ExecuteAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Ranker Service is starting!");
            return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => {}, TaskContinuationOptions.OnlyOnCanceled);
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            _logger.LogDebug("Ranker Service is up and running!");

            using var workers = new SemaphoreSlim(_configurations.Concurrent_Workers);
            var tasks = new List<Task>();

            try {
                await foreach (var candidateDocument in _rankerChannel.Reader.ReadAllAsync(cancellationToken)) {
                    await workers.WaitAsync(cancellationToken);

                    var task = Task.Run(async () => {
                        try {
                            _logger.LogDebug("Ranker Service recieves work!");
                            List<RankedDocument> rankedDocuments = [];

                            foreach (var scoreDoc in candidateDocument.TopDocs.ScoreDocs) {
                                var document = candidateDocument.IndexSearcher.Doc(scoreDoc.Doc);
                                var url = document.Get("url") ?? string.Empty;
                                var title = document.Get("title") ?? string.Empty;
                                var body = document.Get("content") ?? string.Empty;

                                rankedDocuments.Add(new RankedDocument(url, title, scoreDoc.Score));
                            }

                            rankedDocuments = [.. rankedDocuments.OrderByDescending(d => d.Score)];
                            await _outputChannel.Writer.WriteAsync(rankedDocuments, cancellationToken);
                        }
                        catch (OperationCanceledException) {
                            _logger.LogDebug("Ranker Service is cancelling its worker!");
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
                _logger.LogDebug("Ranker Service is down and exiting!");
            }
            catch (OperationCanceledException) {
                _logger.LogInformation("Ranker Service is shutting down!");
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
            }
        }
    }
}
