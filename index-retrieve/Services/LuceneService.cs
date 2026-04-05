using index_retrieve.InvertedIndex;
using Lucene.Net.Index;
using Microsoft.Extensions.Options;
using shared;

namespace index_retrieve.Services {
    public class LuceneService(ILogger<LuceneService> logger, IOptions<Configurations> options, ChannelBus bus, IndexWriterFactory indexWriterFactory)
        : PipelineServiceBase(logger, options, bus) {
        private readonly ILogger<LuceneService> _logger = logger;
        private readonly IndexWriter _indexWriter = indexWriterFactory.IndexWriter;

        protected override Task ExecuteAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Lucene Service is starting!");
            return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => {}, TaskContinuationOptions.OnlyOnCanceled);
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            _logger.LogDebug("Lucene Service is up and running!");

            using var workers = new SemaphoreSlim(_configurations.Concurrent_Workers);
            var tasks = new List<Task>();

            try {
                await foreach (var indexedDocument in _luceneChannel.Reader.ReadAllAsync(cancellationToken)) {
                    await workers.WaitAsync(cancellationToken);

                    var task = Task.Run(() => {
                        try {
                            _logger.LogDebug("Lucene Service recieves work!");

                            var id = new Term("id", IndexWriterFactory.GetDocumentId(indexedDocument.Id));
                            _indexWriter.UpdateDocument(id, indexedDocument.Document);
                            _indexWriter.Commit();
                            Metrics.RecordLastOutput();
                        }
                        catch (OperationCanceledException) {
                            _logger.LogDebug("Lucene Service is cancelling its worker!");
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
                _logger.LogDebug("Lucene Service is down and exiting!");
            }
            catch (OperationCanceledException) {
                _logger.LogInformation("Lucene Service is shutting down!");
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
            }
        }
    }
}
