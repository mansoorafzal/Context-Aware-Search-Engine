using Microsoft.Extensions.Options;
using shared;
using System.Threading.Channels;

namespace crawl.Services {
    public abstract class PipelineServiceBase(ILogger<PipelineServiceBase> logger, IOptions<Configurations> options, ChannelBus bus) : BackgroundService {
        private readonly ILogger<PipelineServiceBase> _logger = logger;
        protected readonly Configurations _configurations = options.Value;
        protected Channel<string> _inputChannel = bus.InputChannel;
        protected Channel<RankedLink> _scopeFilterChannel = bus.ScopeFilterChannel;
        protected Channel<RankedLink> _prefetchDedupChannel = bus.PrefetchDedupChannel;
        protected Channel<RankedLink> _frontierChannel = bus.FrontierChannel;
        protected Channel<RankedLink> _fetcherChannel = bus.FetcherChannel;
        protected Channel<FetchedPage> _extractorChannel = bus.ExtractorChannel;
        protected Channel<ExtractedPage> _postfetchDedupChannel = bus.PostfetchDedupChannel;
        protected Channel<EncryptedPage> _outputChannel = bus.OutputChannel;

        protected override abstract Task ExecuteAsync(CancellationToken stoppingToken);
    }
}
