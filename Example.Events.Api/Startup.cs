using Common.DocDb;
using Common.KeyVault;
using Common.Instrumentation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.KeyVault;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Example.Events.Producer;
using Example.Events.Api.Services;
using Common.Client;

namespace Example.Events.Api
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(Configuration);
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            
            services.AddOptions();
            services.Configure<VaultSettings>(Configuration.GetSection("Vault"));
            services.AddKeyVault(Configuration);

            var appInsightsSettings = new AppInsightsSettings();
            Configuration.Bind("AppInsights:Context", appInsightsSettings);
            var instrumentationKey = services.GetSecret(Configuration, Configuration["AppInsights:InstrumentationKeySecret"]);
            services.AddAppInsights(appInsightsSettings, instrumentationKey);
            services.AddLogging(instrumentationKey);

            services.Configure<DocDbSettings>("SourceDb", Configuration.GetSection("SourceDb"));
            services.Configure<DocDbSettings>("ChangeDb", Configuration.GetSection("ChangeDb"));
            services.Configure<ChangeTrackSettings>(Configuration.GetSection("ChangeTracking"));

            var clientSettings = new HttpClientSettings();
            Configuration.Bind("Clients:EventsProducerApi", clientSettings);
            services.AddClient<IChangeTracker, ChangeTracker>(clientSettings);
            
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseMvc();
        }
    }
}
