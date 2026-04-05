using shared;
using System.Threading.Channels;

namespace index_retrieve.Services {
    public class ChannelBus {
        public readonly Channel<string> InputChannel;
        public readonly Channel<EncryptedPage> DocumentIngestorChannel;
        public readonly Channel<IndexedDocument> LuceneChannel;
        public readonly Channel<string> QueryParserChannel;
        public readonly Channel<QueryDocument> BooleanExpressionEvaluatorChannel;
        public readonly Channel<CandidateDocument> RankerChannel;
        public readonly Channel<List<RankedDocument>> OutputChannel;

        public ChannelBus(int capacity) {
            var boundedChannelOptions = new BoundedChannelOptions(capacity) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            };

            InputChannel = Channel.CreateUnbounded<string>();
            DocumentIngestorChannel = Channel.CreateBounded<EncryptedPage>(boundedChannelOptions);
            LuceneChannel = Channel.CreateBounded<IndexedDocument>(boundedChannelOptions);
            QueryParserChannel = Channel.CreateUnbounded<string>();
            BooleanExpressionEvaluatorChannel = Channel.CreateBounded<QueryDocument>(boundedChannelOptions);
            RankerChannel = Channel.CreateBounded<CandidateDocument>(boundedChannelOptions);
            OutputChannel = Channel.CreateUnbounded<List<RankedDocument>>();
        }
    }
}
