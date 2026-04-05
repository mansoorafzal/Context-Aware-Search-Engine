namespace crawl {
    public class Configurations {
        public required string Inter_Services_Pub_Sub_Url { get; set; } = default!;

        public int Http_Client_Request_Timeout_Seconds { get; set; } = default!;

        public required string User_Agent { get; set; } = default!;
        
        public string[] Seeds { get; set; } = [];

        public string[] Domain_Scopes { get; set; } = [];

        public string[] Context_Aware_Body_Keywords { get; set; } = [];

        public string[] Context_Aware_Host_Keywords { get; set; } = [];

        public float Context_Aware_Body_Reward { get; set; } = default!;

        public float Context_Aware_Host_Reward { get; set; } = default!;

        public int Channel_Processing_Capacity { get; set; } = default!;

        public int Concurrent_Workers { get; set; } = default!;

        public long Bloom_Filter_Capacity { get; set; } = default!;

        public float Bloom_Filter_False_Positive_Probability { get; set; } = default!;

        public int Depth_Penalty { get; set; } = default!;

        public bool Quiescence_Monitor_Enabled { get; set; } = default!;

        public int Quiescence_Monitor_Shutdown_Seconds { get; set; } = default!;
        
        public bool Politeness_Enabled { get; set; } = default!;

        public int Crawl_Delay_Floor_Seconds { get; set; } = default!;

        public int Crawl_Delay_Ceiling_Seconds { get; set; } = default!;

        public int Near_Duplicate_Threshold { get; set; } = default!;

        public bool Refinements_Enabled { get; set; } = default!;
    }
}
