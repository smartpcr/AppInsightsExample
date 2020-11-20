using System;
using Newtonsoft.Json.Linq;

namespace Example.Events.Producer
{
    /// <summary>
    /// 
    /// </summary>
    public class ChangeDocument
    {
        public string PartitionKey { get; set; }
        public DateTime Timestamp { get; set; }
        public ChangeType ChangeType { get; set; }
        public string OperationId { get; set; }
        public string PayloadId { get; set; }
        public JObject Payload { get; set; }
    }

    public enum ChangeType
    {
        Create,
        Update,
        Delete
    }
}
