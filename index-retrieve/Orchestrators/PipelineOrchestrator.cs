using index_retrieve.Services;
using Microsoft.Extensions.Options;
using shared;

namespace index_retrieve.Orchestrators {
    public class PipelineOrchestrator(ILogger<PipelineOrchestrator> logger, IOptions<Configurations> options, InputService inputService, DocumentIngestorService  _documentIngestorService
        , LuceneService luceneService, QueryParserService queryParserService, BooleanExpressionEvaluatorService booleanExpressionEvaluatorService, RankerService rankerService
        , OutputService outputService) : IOrchestrator {
        private readonly ILogger<PipelineOrchestrator> _logger = logger;
        private readonly Configurations _configurations = options.Value;
        private readonly InputService _inputService = inputService;
        private readonly DocumentIngestorService _documentIngestorService = _documentIngestorService;
        private readonly LuceneService _luceneService = luceneService;
        private readonly QueryParserService _queryParserService = queryParserService;
        private readonly BooleanExpressionEvaluatorService _booleanExpressionEvaluatorService = booleanExpressionEvaluatorService;
        private readonly RankerService _rankerService = rankerService;
        private readonly OutputService _outputService = outputService;
        private CancellationTokenSource? _cancellationTokenSource;

        public async Task<int> StartAsync() {
            if (Metrics.IsRunning) {
                _logger.LogInformation("Pipeline is already running!");
                return 1;
            }
            
            _logger.LogInformation("Pipeline is started!");

            try {
                _cancellationTokenSource = new CancellationTokenSource();
                _ = Task.Run(() => _inputService.RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _ = Task.Run(() => _documentIngestorService.RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _ = Task.Run(() => _luceneService.RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _ = Task.Run(() => _queryParserService.RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _ = Task.Run(() => _booleanExpressionEvaluatorService.RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _ = Task.Run(() => _rankerService.RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _ = Task.Run(() => _outputService.RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

                await _inputService.EnqueueAsync(string.Empty, _cancellationTokenSource.Token);
                Metrics.Start();
                return 0;
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
                return -1;
            }
        }

        public int Stop() {
            if (!Metrics.IsRunning) {
                _logger.LogInformation("Pipeline is alredy stopped!");
                return 1;
            }

            _cancellationTokenSource?.Cancel();
            Metrics.Stop();
            _logger.LogInformation("Pipeline is stopped!");
            return 0;
        }

        public Task<IndexRetrieveData?> StatusAsync() {
            try {
                string mode = _configurations.Refinements_Enabled ? "Context-Aware Search Engine (CASE)" : "Baseline";
                string status = Metrics.IsRunning ? "Running" : "Stopped";

                var data = new IndexRetrieveData(
                    Mode: mode,
                    Status: status,
                    RunningSeconds: Metrics.RunningSeconds,
                    RunningMinutes: Metrics.RunningMinutes,
                    ElapsedSeconds: Metrics.ElapsedSeconds < 300 ? Metrics.ElapsedSeconds : 0,
                    ElapsedMinutes: Metrics.ElapsedMinutes >= 5 ? Metrics.ElapsedMinutes : 0,
                    TotalOutput: Metrics.TotalOutput,
                    AverageOutputPerSecond: Metrics.ElapsedSeconds < 300 ? Metrics.AverageOutputPerSecond : 0,
                    AverageOutputPerMinute: Metrics.ElapsedMinutes >= 5 ? Metrics.AverageOutputPerMinute : 0,
                    AverageLatencyMilliseconds: Metrics.AverageLatencyMilliseconds,
                    P95Milliseconds: Metrics.P95Milliseconds
                );

                return Task.FromResult<IndexRetrieveData?>(data);
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
                return Task.FromResult<IndexRetrieveData?>(null);
            }
        }

        public async Task<(int, IEnumerable<RankedDocument>?)> SearchAsync(string input) {
            if (_cancellationTokenSource == null) {
                return (1, null);
            }

            try {
                var task = _outputService.WaitAsync();
                var startTime = DateTime.UtcNow;

                await _inputService.EnqueueAsync(input, _cancellationTokenSource.Token);
                var rankedDocuments = await task.WaitAsync(_cancellationTokenSource.Token);
                
                var latency = DateTime.UtcNow - startTime;
                Metrics.RecordLatency(latency);
                
                return (0, rankedDocuments);
            }
            catch (OperationCanceledException) {
                return (1, null);
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
                return (-1, null);
            }
        }

        public async Task LoadTestAsync(string input) {
            for (int i = 0; i < _configurations.Concurrent_Search_Requests; i++) {
                _ = await SearchAsync(input);
            }
        }
    }
}
