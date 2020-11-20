namespace Example.DocDb.Sync
{
    public class DataSyncSettings
    {
        public string[] DocumentTypes { get; set; }
        public int MaxDegreeOfParallelism { get; set; }
    }
}
