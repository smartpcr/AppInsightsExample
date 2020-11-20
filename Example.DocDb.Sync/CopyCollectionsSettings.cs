using System.Collections.Generic;
using Common.DocDb;

namespace Example.DocDb.Sync
{
    public class CopyCollectionsSettings
    {
        public DocDbSettings Source { get; set; }
        public DocDbSettings Target { get; set; }
        public List<string> Collections { get; set; }
        public int BatchSize { get; set; }
    }
}