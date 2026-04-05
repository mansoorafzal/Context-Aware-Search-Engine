using System.Collections.Concurrent;

namespace shared {
    public static class MessageQueue<T> {
        private static BlockingCollection<T>? _blockingCollection;
        private static int _capacity = 100;
        private static int _timeoutMilliseconds = 200;
        private static bool _isInitialized = false;

        public static void Initialize(int capacity, int timeoutMilliseconds) {
            if (!_isInitialized) {
                _capacity = capacity;
                _timeoutMilliseconds = timeoutMilliseconds;
                _isInitialized = true;

                Initialize();
            }
        }

        private static void Initialize() {
            _blockingCollection = new BlockingCollection<T>(new ConcurrentQueue<T>(), _capacity);
        }

        public static void Push(T item) {
            if (!_isInitialized) {
                Initialize();
            }

            _blockingCollection!.Add(item);
        }

        public static T? Pull(CancellationToken cancellationToken) {
            if (_blockingCollection!.TryTake(out T? item, _timeoutMilliseconds, cancellationToken)) {
                return item;
            }

            return default;
        }
    }
}
