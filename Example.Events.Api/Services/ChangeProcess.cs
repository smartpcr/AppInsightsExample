using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Example.Events.Api.Services
{
    public class ChangeProcess
    {
        public string OperationId { get; set; }
        public string OperationName { get; set; }
        public DateTime Timestamp { get; set; }
        public string PayloadId { get; set; }
    }
}
