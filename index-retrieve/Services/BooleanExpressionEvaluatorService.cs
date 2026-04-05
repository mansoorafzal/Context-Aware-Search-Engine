using index_retrieve.InvertedIndex;
using Lucene.Net.Search;
using Microsoft.Extensions.Options;

namespace index_retrieve.Services {
    public class BooleanExpressionEvaluatorService(ILogger<BooleanExpressionEvaluatorService> logger, IOptions<Configurations> options, ChannelBus bus, IndexReaderFactory indexReaderFactory)
        : PipelineServiceBase(logger, options, bus) {
        private readonly ILogger<BooleanExpressionEvaluatorService> _logger = logger;
        private readonly IndexReaderFactory _indexReaderFactory = indexReaderFactory;

        protected override Task ExecuteAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Boolean Expression Evaluator Service is starting!");
            return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => {}, TaskContinuationOptions.OnlyOnCanceled);
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            _logger.LogDebug("Boolean Expression Evaluator Service is up and running!");

            using var workers = new SemaphoreSlim(_configurations.Concurrent_Workers);
            var tasks = new List<Task>();

            try {
                await foreach (var queryDocument in _booleanExpressionEvaluatorChannel.Reader.ReadAllAsync(cancellationToken)) {
                    await workers.WaitAsync(cancellationToken);

                    var task = Task.Run(async () => {
                        try {
                            _logger.LogDebug("Boolean Expression Evaluator Service recieves work!");

                            _indexReaderFactory.Refresh();
                            IndexSearcher? indexSearcher = _indexReaderFactory.IndexSearcher;

                            if (indexSearcher == null) {
                                return;
                            }

                            TopDocs topDocs = indexSearcher!.Search(queryDocument.BooleanQuery, _configurations.Maximum_Search_Documents);
                            await _rankerChannel.Writer.WriteAsync(new(queryDocument, indexSearcher, topDocs), cancellationToken);
                        }
                        catch (OperationCanceledException) {
                            _logger.LogDebug("Boolean Expression Evaluator Service is cancelling its worker!");
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
                _logger.LogDebug("Boolean Expression Evaluator Service is down and exiting!");
            }
            catch (OperationCanceledException) {
                _logger.LogInformation("Boolean Expression Evaluator Service is shutting down!");
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
            }
        }
    }
}
