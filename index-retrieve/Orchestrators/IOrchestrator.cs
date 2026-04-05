using shared;

namespace index_retrieve.Orchestrators {
    public interface IOrchestrator {
        public Task<int> StartAsync();
        
        public int Stop();

        public Task<IndexRetrieveData?> StatusAsync();

        public Task<(int, IEnumerable<RankedDocument>?)> SearchAsync(string input);

        public Task LoadTestAsync(string input);
    }
}
