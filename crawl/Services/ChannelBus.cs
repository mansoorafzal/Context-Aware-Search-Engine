using shared;
using System.Threading.Channels;

namespace crawl.Services {
    public class ChannelBus {
        public readonly Channel<string> InputChannel;
        public readonly Channel<RankedLink> ScopeFilterChannel;
        public readonly Channel<RankedLink> PrefetchDedupChannel;
        public readonly Channel<RankedLink> FrontierChannel;
        public readonly Channel<RankedLink> FetcherChannel;
        public readonly Channel<FetchedPage> ExtractorChannel;
        public readonly Channel<ExtractedPage> PostfetchDedupChannel;
        public readonly Channel<EncryptedPage> OutputChannel;

        public ChannelBus(int capacity) {
            var boundedChannelOptions = new BoundedChannelOptions(capacity) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            };

            InputChannel = Channel.CreateUnbounded<string>();
            ScopeFilterChannel = Channel.CreateUnbounded<RankedLink>();
            PrefetchDedupChannel = Channel.CreateUnbounded<RankedLink>();
            FrontierChannel = Channel.CreateUnbounded<RankedLink>();
            FetcherChannel = Channel.CreateBounded<RankedLink>(boundedChannelOptions);
            ExtractorChannel = Channel.CreateBounded<FetchedPage>(boundedChannelOptions);
            PostfetchDedupChannel = Channel.CreateUnbounded<ExtractedPage>();
            OutputChannel = Channel.CreateBounded<EncryptedPage>(boundedChannelOptions);
        }
    }
}
