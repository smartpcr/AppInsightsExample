using Common.DocDb;
using Common.Instrumentation;
using Common.KeyVault;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.KeyVault;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Example.Events.Producer
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

            app.UseMvc();
        }
    }
}
