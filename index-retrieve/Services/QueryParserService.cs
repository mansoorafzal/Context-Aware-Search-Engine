using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Queries.Function;
using Lucene.Net.Queries.Function.ValueSources;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Microsoft.Extensions.Options;
using shared;

namespace index_retrieve.Services {
    public class QueryParserService(ILogger<QueryParserService> logger, IOptions<Configurations> options, ChannelBus bus) : PipelineServiceBase(logger, options, bus) {
        private static readonly LuceneVersion _luceneVersion = LuceneVersion.LUCENE_48;
        private readonly ILogger<QueryParserService> _logger = logger;
        private readonly StandardAnalyzer _standardAnalyzer = new(_luceneVersion);
        private readonly string[] _fields = ["title", "content"];

        protected override Task ExecuteAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Query Parser Service is starting!");
            return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => {}, TaskContinuationOptions.OnlyOnCanceled);
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            _logger.LogDebug("Query Parser Service is up and running!");

            using var workers = new SemaphoreSlim(_configurations.Concurrent_Workers);
            var tasks = new List<Task>();

            try {
                await foreach (var input in _queryParserChannel.Reader.ReadAllAsync(cancellationToken)) {
                    await workers.WaitAsync(cancellationToken);

                    var task = Task.Run(async () => {
                        try {
                            _logger.LogDebug("Query Parser Service recieves work!");

                            await _booleanExpressionEvaluatorChannel.Writer.WriteAsync(BuildQueryDocument(input), cancellationToken);
                        }
                        catch (OperationCanceledException) {
                            _logger.LogDebug("Query Parser Service is cancelling its worker!");
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
                _logger.LogDebug("Query Parser Service is down and exiting!");
            }
            catch (OperationCanceledException) {
                _logger.LogInformation("Query Parser Service is shutting down!");
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
            }
        }

        private QueryDocument BuildQueryDocument(string input) {
            string[] terms = [.. input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(t => t.ToLowerInvariant())];
            PhraseQuery titlePhraseQuery = [];
            PhraseQuery bodyPhraseQuery = [];
            MultiFieldQueryParser multiFieldQueryParser = new(_luceneVersion, _fields, _standardAnalyzer);
            Query query = multiFieldQueryParser.Parse(input);
            BooleanQuery booleanQuery = new() {
                { query, Occur.MUST },
            };

            if (_configurations.Refinements_Enabled) {
                if (terms.Length > 1) {
                    foreach (var term in terms) {
                        titlePhraseQuery.Add(new Term("title", term.ToLowerInvariant()));
                        bodyPhraseQuery.Add(new Term("content", term.ToLowerInvariant()));
                    }

                    booleanQuery.Add(ApplyBooster(titlePhraseQuery, _configurations.Context_Aware_Phrase_Reward), Occur.SHOULD);
                    booleanQuery.Add(ApplyBooster(bodyPhraseQuery, _configurations.Context_Aware_Phrase_Reward), Occur.SHOULD);
                }

                foreach (var keyword in _configurations.Context_Aware_Keywords) {
                    booleanQuery.Add(ApplyBooster("title", keyword, _configurations.Context_Aware_Title_Reward), Occur.SHOULD);
                    booleanQuery.Add(ApplyBooster("content", keyword, _configurations.Context_Aware_Body_Reward), Occur.SHOULD);
                }
            }

            return new QueryDocument(terms, query, [titlePhraseQuery, bodyPhraseQuery], booleanQuery);
        }

        private static BoostedQuery ApplyBooster(Query query, float reward) {
            ValueSource valueSource = new QueryValueSource(query, reward);
            return new(query, valueSource);
        }

        private static BoostedQuery ApplyBooster(string field, string keyword, float reward) {
            TermQuery termQuery = new(new Term(field, keyword.ToLowerInvariant()));
            return ApplyBooster(termQuery, reward);
        }
    }
}
