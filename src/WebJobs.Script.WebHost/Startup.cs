// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddWebJobsScriptHostAuthentication();
            services.AddWebJobsScriptHostAuthorization();
            services.AddWebJobsScriptHost(Configuration);

            var config = new Dictionary<string, string>
            {
                { "OpenTelemetry:Exporters:Otlp:Endpoint", "http://127.0.0.1:4317"},
                { "OpenTelemetry:Exporters:Otlp:Enabled", "true"},
                { "OpenTelemetry:Tracing:Enabled", "false"},
                { "OpenTelemetry:Logging:Enabled", "true"},
                { "OpenTelemetry:Metrics:Enabled", "true"},
                { "OpenTelemetry:Metrics:Function", "true"},
                { "OpenTelemetry:Metrics:Sockets", "false"},
                { "OpenTelemetry:Metrics:Custom", "false"},
                { "OpenTelemetry:Metrics:AspNet", "false"},
                { "OpenTelemetry:Metrics:HttpClient", "false"},
                { "OpenTelemetry:Metrics:Runtime", "false"},
                { "OpenTelemetry:Metrics:BuildInfo", "false"},
                { "OpenTelemetry:Metrics:Exceptions", "false"},
            };
            var opentelemetryConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(config)
                .AddEnvironmentVariables()
                .Build();
            services.AddPlatformOpenTelemetry(opentelemetryConfiguration);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IApplicationLifetime applicationLifetime, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseWebJobsScriptHost(applicationLifetime);
        }
    }
}
