using System.Collections.Concurrent;

namespace shared {
    public static class Metrics {
        private static bool _isRunning;
        private static DateTime _startDateTime;
        private static DateTime _stopDateTime;
        private static DateTime? _firstInput;
        private static DateTime? _lastOutput;
        private static readonly ConcurrentBag<double> _latencies;
        private static readonly ConcurrentBag<double> _bytes;
        private static double _totalExtractedPages;
        private static double _totalDuplicatePages;
        private static double _totalNearDuplicatePages;
        private static double _totalOutput;

        static Metrics() {
            _isRunning = false;
            _firstInput = null;
            _lastOutput = null;
            _latencies = [];
            _bytes = [];
            _totalDuplicatePages = 0.0;
            _totalNearDuplicatePages = 0.0;
            _totalExtractedPages = 0.0;
            _totalOutput = 0.0;
        }

        public static bool IsRunning {
            get { return _isRunning; }
        }

        public static void Start() {
            _startDateTime = DateTime.UtcNow;
            _isRunning = true;
        }

        public static void Stop() {
            _stopDateTime = DateTime.UtcNow;
            _isRunning = false;
        }

        public static void RecordFirstInput() {
            if (_isRunning && _firstInput == null) {
                _firstInput = DateTime.UtcNow;
            }
        }

        public static void RecordLastOutput() {
            if (_isRunning) {
                _lastOutput = DateTime.UtcNow;
                _totalOutput++;
            }
        }

        public static void RecordLatency(TimeSpan latency) {
            if (_isRunning) {
                _latencies.Add(latency.TotalMilliseconds);
            }
        }

        public static void RecordBytes(int bytes) {
            if (_isRunning) {
                _bytes.Add(bytes);
                _totalExtractedPages++;
            }
        }

        public static void RecordDuplicatePages() {
            if (_isRunning) {
                _totalDuplicatePages++;
            }
        }

        public static void RecordNearDuplicatePages() {
            if (_isRunning) {
                _totalNearDuplicatePages++;
            }
        }

        public static double RunningSeconds {
            get {
                double seconds;

                if (IsRunning) {
                    seconds = (DateTime.UtcNow - _startDateTime).TotalSeconds;
                }
                else {
                    seconds = (_stopDateTime - _startDateTime).TotalSeconds;
                }

                return Math.Round(seconds, 2);
            }
        }

        public static double RunningMinutes {
            get { return Math.Round(RunningSeconds / 60.0, 2); }
        }

        public static double ElapsedSeconds {
            get {
                double seconds = 0.0;

                if (_firstInput != null && _lastOutput != null) {
                    seconds = (_lastOutput.Value - _firstInput.Value).TotalSeconds;
                }

                return Math.Round(seconds, 2);
            }
        }

        public static double ElapsedMinutes {
            get { return Math.Round(ElapsedSeconds / 60.0, 2); }
        }

        public static double AverageLatencyMilliseconds {
            get { return Math.Round(_latencies.IsEmpty ? 0.0 : _latencies.Average(), 2); }
        }

        public static double P95Milliseconds {
            get {
                var latencies = _latencies.ToList();

                if (latencies.Count == 0) {
                    return 0.0;
                }

                latencies.Sort();
                int count = latencies.Count;
                int index = (int)Math.Ceiling(0.95 * count) - 1;

                if (index < 0) {
                    index = 0;
                }

                if (index >= count) {
                    index = count - 1;
                }

                return Math.Round(latencies[index], 2);
            }
        }

        public static double TotalExtractedPages {
            get { return _totalExtractedPages; }
        }

        public static double AveragePageSizeKiloBytes {
            get { return Math.Round(_bytes.IsEmpty ? 0.0 : _bytes.Average() / 1024.0, 2); }
        }

        public static double TotalDownloadedContentMegaBytes {
            get { return Math.Round(_bytes.IsEmpty ? 0.0 : _bytes.Sum() / 1024.0 / 1024.0, 2); }
        }

        public static double TotalDuplicatePages {
            get { return _totalDuplicatePages; }
        }

        public static double TotalNearDuplicatePages {
            get { return _totalNearDuplicatePages; }
        }

        public static double TotalOutput {
            get { return _totalOutput; }
        }

        public static double AverageOutputPerSecond {
            get {
                double output = 0.0;

                if (ElapsedSeconds > 0.0) {
                    output = _totalOutput / ElapsedSeconds;
                }

                return Math.Round(output, 2);
            }
        }

        public static double AverageOutputPerMinute {
            get { return Math.Round(AverageOutputPerSecond * 60.0, 2); }
        }
    }
}
