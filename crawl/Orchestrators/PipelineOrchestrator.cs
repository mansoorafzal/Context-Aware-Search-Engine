using crawl.Services;
using Microsoft.Extensions.Options;
using shared;

namespace crawl.Orchestrators {
    public class PipelineOrchestrator(ILogger<PipelineOrchestrator> logger, IOptions<Configurations> options, InputService inputService, ScopeFilterService scopeFilterService
        , PrefetchDedupService prefetchDedupService, FrontierService frontierService, FetcherService fetcherService, ExtractorService extractorService, PostfetchDedupService postfetchDedupService
        , OutputService outputService) : IOrchestrator {
        private readonly ILogger<PipelineOrchestrator> _logger = logger;
        private readonly Configurations _configurations = options.Value;
        private readonly InputService _inputService = inputService;
        private readonly ScopeFilterService _scopeFilterService = scopeFilterService;
        private readonly PrefetchDedupService _prefetchDedupService = prefetchDedupService;
        private readonly FrontierService _frontierService = frontierService;
        private readonly FetcherService _fetcherService = fetcherService;
        private readonly ExtractorService _extractorService = extractorService;
        private readonly PostfetchDedupService _postfetchDedupService = postfetchDedupService;
        private readonly OutputService _outputService = outputService;
        private CancellationTokenSource? _cancellationTokenSource;

        public async Task<int> StartAsync(string[] seeds) {
            if (Metrics.IsRunning) {
                _logger.LogInformation("Pipeline is already running!");
                return 1;
            }

            _logger.LogInformation("Pipeline is started!");

            try {
                _cancellationTokenSource = new CancellationTokenSource();
                _ = Task.Run(() => _inputService.RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _ = Task.Run(() => _scopeFilterService.RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _ = Task.Run(() => _prefetchDedupService.RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _ = Task.Run(() => _frontierService.RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _ = Task.Run(() => _fetcherService.RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _ = Task.Run(() => _extractorService.RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _ = Task.Run(() => _postfetchDedupService.RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _ = Task.Run(() => _outputService.RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

                foreach (var seed in seeds) {
                    await _inputService.EnqueueAsync(seed, _cancellationTokenSource.Token);
                }

                if (_configurations.Quiescence_Monitor_Enabled) {
                    _ = Task.Run(() => QuiescenceMonitor(_cancellationTokenSource.Token));
                }

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

        public Task<CrawlData?> StatusAsync() {
            try {
                string mode = _configurations.Refinements_Enabled ? "Context-Aware Search Engine (CASE)" : "Baseline";
                string status = Metrics.IsRunning ? "Running" : "Stopped";

                var data = new CrawlData(
                    Mode: mode,
                    Status: status,
                    RunningSeconds: Metrics.RunningSeconds,
                    RunningMinutes: Metrics.RunningMinutes,
                    ElapsedSeconds: Metrics.ElapsedSeconds < 300 ? Metrics.ElapsedSeconds : 0,
                    ElapsedMinutes: Metrics.ElapsedMinutes >= 5 ? Metrics.ElapsedMinutes : 0,
                    AverageLatencyMilliseconds: Metrics.AverageLatencyMilliseconds,
                    TotalExtractedPages: Metrics.TotalExtractedPages,
                    AveragePageSizeKiloBytes: Metrics.AveragePageSizeKiloBytes,
                    TotalDownloadedContentMegaBytes: Metrics.TotalDownloadedContentMegaBytes,
                    TotalDuplicatePages: Metrics.TotalDuplicatePages,
                    TotalNeaDuplicatePages: Metrics.TotalNearDuplicatePages,
                    TotalOutput: Metrics.TotalOutput,
                    AverageOutputPerSecond: Metrics.ElapsedSeconds < 300 ? Metrics.AverageOutputPerSecond : 0,
                    AverageOutputPerMinute: Metrics.ElapsedMinutes >= 5 ? Metrics.AverageOutputPerMinute : 0
                );

                return Task.FromResult<CrawlData?>(data);
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
                return Task.FromResult<CrawlData?>(null);
            }
        }

        private async Task QuiescenceMonitor(CancellationToken cancellationToken) {
            _logger.LogInformation("Quiescence Monitor is starting!");
            int empty = 0;

            while (!cancellationToken.IsCancellationRequested) {
                try {
                    await Task.Delay(1000, cancellationToken);
                }
                catch (OperationCanceledException) {
                    _logger.LogInformation("Quiescence Monitor is waiting but cancellation is requested!");
                    break;
                }

                if (_frontierService.Count == 0) {
                    empty++;
                    _logger.LogInformation("Quiescence Monitor finds an empty queue in Frontier Service for {empty}/{threshold} seconds!", empty, _configurations.Quiescence_Monitor_Shutdown_Seconds);

                    if (empty >= _configurations.Quiescence_Monitor_Shutdown_Seconds) {
                        _logger.LogInformation("Quiescence Monitor triggers shutdown!");

                        Stop();
                        return;
                    }
                }
                else {
                    if (empty > 0) {
                        empty = 0;
                        _logger.LogInformation("Quiescence Monitor resets as Pipeline finds work!");
                    }
                }
            }

            _logger.LogInformation("Quiescence Monitor is shutting down!");
        }
    }
}
