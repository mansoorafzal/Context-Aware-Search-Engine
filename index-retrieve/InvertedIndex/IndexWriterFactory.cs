using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using System.Text.RegularExpressions;

namespace index_retrieve.InvertedIndex {
    public partial class IndexWriterFactory {
        private static readonly LuceneVersion _luceneVersion = LuceneVersion.LUCENE_48;
        private readonly ILogger<IndexWriterFactory> _logger;
        private readonly Lucene.Net.Store.Directory _directory;
        private readonly IndexWriter _indexWriter;

        public IndexWriterFactory(ILogger<IndexWriterFactory> logger) {
            _logger = logger;
            _directory = new RAMDirectory();
            
            var analyzer = new StandardAnalyzer(_luceneVersion);
            var indexWriterConfig = new IndexWriterConfig(_luceneVersion, analyzer) {
                OpenMode = OpenMode.CREATE_OR_APPEND
            };

            _indexWriter = new IndexWriter(_directory, indexWriterConfig);
        }

        public IndexWriter IndexWriter {
            get { return _indexWriter; }
        }

        public Lucene.Net.Store.Directory Directory {
            get { return _directory; }
        }
        
        public static string GetDocumentId(string url) {
            if (string.IsNullOrEmpty(url)) {
                return string.Empty;
            }
            
            return UrlCleanupRegex().Replace(url, string.Empty).TrimEnd('/');
        }

        [GeneratedRegex(@"^https?:\/\/(?:www\.)?", RegexOptions.Compiled | RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex UrlCleanupRegex();
    }
}
