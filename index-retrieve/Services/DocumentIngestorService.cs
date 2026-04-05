using Lucene.Net.Documents;
using Microsoft.Extensions.Options;
using shared;

namespace index_retrieve.Services {
    public class DocumentIngestorService(ILogger<DocumentIngestorService> logger, IOptions<Configurations> options, ChannelBus bus) : PipelineServiceBase(logger, options, bus) {
        private readonly ILogger<DocumentIngestorService> _logger = logger;

        protected override Task ExecuteAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Document Ingestor Service is starting!");
            return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => {}, TaskContinuationOptions.OnlyOnCanceled);
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            _logger.LogDebug("Document Ingestor Service is up and running!");

            using var workers = new SemaphoreSlim(_configurations.Concurrent_Workers);
            var tasks = new List<Task>();

            try {
                await foreach (var encryptedPage in _documentIngestorChannel.Reader.ReadAllAsync(cancellationToken)) {
                    await workers.WaitAsync(cancellationToken);

                    var task = Task.Run(async () => {
                        try {
                            _logger.LogDebug("Document Ingestor Service recieves work!");

                            var url = encryptedPage.ExtractedPage.FetchedPage.RankedLink.Uri.AbsoluteUri;

                            var document = new Document {
                                new StringField("id", url, Field.Store.YES),
                                new StringField("url", url, Field.Store.YES),
                                new TextField("content", encryptedPage.ExtractedPage.FetchedPage.Content, Field.Store.NO),
                                new TextField("title", encryptedPage.ExtractedPage.Title, Field.Store.YES),
                                new SingleField("score", encryptedPage.ExtractedPage.Score, Field.Store.YES)
                            };

                            await _luceneChannel.Writer.WriteAsync(new IndexedDocument(url, document), cancellationToken);
                        }
                        catch (OperationCanceledException) {
                            _logger.LogDebug("Document Ingestor Service is cancelling its worker!");
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
                _logger.LogDebug("Document Ingestor Service is down and exiting!");
            }
            catch (OperationCanceledException) {
                _logger.LogInformation("Document Ingestor Service is shutting down!");
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
            }
        }
    }
}
