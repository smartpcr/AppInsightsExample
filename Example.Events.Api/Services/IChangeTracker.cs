using System.Collections.Generic;
using System.Threading.Tasks;
using Example.Events.Producer;
using Newtonsoft.Json.Linq;

namespace Example.Events.Api.Services
{
    public interface IChangeTracker
    {
        Task<IEnumerable<ChangeDocument>> CreateChanges(int count);
        Task<IEnumerable<ChangeProcess>> FindChangeProcesses(string payloadId);
    }
}
