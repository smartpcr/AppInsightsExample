namespace Example.Events.Producer
{
    public class ChangeTrackSettings
    {
        public string[] DocumentTypes { get; set; }
        public double Percentage { get; set; }
        public int ScanIntervalInSeconds { get; set; }
    }
}
