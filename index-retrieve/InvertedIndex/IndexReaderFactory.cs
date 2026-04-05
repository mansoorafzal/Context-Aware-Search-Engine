using Lucene.Net.Index;
using Lucene.Net.Search;

namespace index_retrieve.InvertedIndex {
    public partial class IndexReaderFactory(ILogger<IndexReaderFactory> logger, IndexWriterFactory _indexWriterFactory) {
        private readonly ILogger<IndexReaderFactory> _logger = logger;
        private readonly Lucene.Net.Store.Directory _directory = _indexWriterFactory.Directory;
        private readonly IndexWriter _indexWriter = _indexWriterFactory.IndexWriter;
        private volatile IndexReader? _indexReader;
        private volatile IndexSearcher? _indexSearcher;

        public void Refresh() {
            try {
                IndexReader reader;
                
                if (_indexWriter.HasDeletions() || _indexWriter.HasUncommittedChanges()) {
                    _indexWriter.Commit(); 
                    reader = DirectoryReader.Open(_indexWriter, applyAllDeletes: true);
                }
                else if (_indexReader == null) {
                    reader = DirectoryReader.Open(_directory);
                } 
                else {
                    var directoryReader = DirectoryReader.OpenIfChanged((DirectoryReader)_indexReader);
                    
                    if (directoryReader == null) {
                        return;
                    }

                    reader = directoryReader;
                }

                _indexReader = reader;
                _indexSearcher = new IndexSearcher(_indexReader);
            }
            catch (Exception exception) {
                _logger.LogError("{message}, {stackTrace}", exception.Message, exception.StackTrace);
            }
        }

        public IndexSearcher? IndexSearcher {
            get {  return _indexSearcher; }
        }
    }
}
