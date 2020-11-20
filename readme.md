# Goals
- use AsyncLocal<Activity> to setup correlation
- make sure call graph is represented in app insights

# structure 
- Common
  - Client: create typed HttpClient with policy, inject correlation id to header (TODO: add auth access token)
  - Instrumentation: start runtime metrics collector, and write metric/logging/tracing to app insights
  - KeyVault: inject KV dependency
  - DocDb: additional metrics from doc db
- Examples
  - DocDb.Sync: test perf with bulk executor compared to one-by-one, trying to get throttling metrics
  - Events.Api: use HttpClientFactory to pass request to events.Producer and create change documents 
  - Events.Producer: API that will create change documents and put them to a new collection, this is to test correlation-id is passed from Events.Api via http headers
  - Events.Consumer: cosmosdb change feed processor running as background job, this is to test correlation-id is passed from change document payload

# Steps
1. create AppInsights, KV, SPN used to access KV, instrumentation key of AppInsights is stored in KV, Cert is downloaded and used to authenticate SPN
``` PowerShell
.\BootstrapKeyVaultAccess.ps1
```
2. Run the application (Events.Api --> Events.Producer --> Events.Consumer)
3. create a change document via API endpoint (GET): http://localhost:9003/api/impact/1, which calls producer (http://localhost:9001/api/changedocuments/1), then picked by consumer
4. Explore logs in azure portal --> application insights --> search 
![distributed trace](https://github.com/smartpcr/app-insights-spike/blob/master/app-insights-trace.PNG)