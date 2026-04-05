using Microsoft.Extensions.Options;
using shared;
using System.Threading.Channels;

namespace index_retrieve.Services {
    public abstract class PipelineServiceBase(ILogger<PipelineServiceBase> logger, IOptions<Configurations> options, ChannelBus bus) : BackgroundService {
        private readonly ILogger<PipelineServiceBase> _logger = logger;
        protected readonly Configurations _configurations = options.Value;
        protected Channel<string> _inputChannel = bus.InputChannel;
        protected Channel<EncryptedPage> _documentIngestorChannel = bus.DocumentIngestorChannel;
        protected Channel<IndexedDocument> _luceneChannel = bus.LuceneChannel;
        protected Channel<string> _queryParserChannel = bus.QueryParserChannel;
        protected Channel<QueryDocument> _booleanExpressionEvaluatorChannel = bus.BooleanExpressionEvaluatorChannel;
        protected Channel<CandidateDocument> _rankerChannel = bus.RankerChannel;
        protected Channel<List<RankedDocument>> _outputChannel = bus.OutputChannel;

        protected override abstract Task ExecuteAsync(CancellationToken stoppingToken);
    }
}
