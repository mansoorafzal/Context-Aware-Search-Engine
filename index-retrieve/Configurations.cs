namespace index_retrieve {
    public class Configurations {
        public required string Inter_Services_Pub_Sub_Url { get; set; } = default!;

        public int Http_Client_Request_Timeout_Seconds { get; set; } = default!;

        public int Channel_Processing_Capacity { get; set; } = default!;

        public int Concurrent_Workers { get; set; } = default!;

        public int Maximum_Search_Documents { get; set; } = default!;

        public int Concurrent_Search_Requests { get; set; } = default!;

        public required string[] Context_Aware_Keywords { get; set; } = [];

        public float Context_Aware_Title_Reward { get; set; } = default!;

        public float Context_Aware_Body_Reward { get; set; } = default!;

        public float Context_Aware_Phrase_Reward { get; set; } = default!;

        public bool Refinements_Enabled { get; set; } = default!;
    }
}
