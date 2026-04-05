using shared;

namespace crawl.Orchestrators {
    public interface IOrchestrator {
        public Task<int> StartAsync(string[] seeds);
        
        public int Stop();

        public Task<CrawlData?> StatusAsync();
    }
}
