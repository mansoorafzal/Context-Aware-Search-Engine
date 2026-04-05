using crawl.Orchestrators;
using crawl.Services;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;

namespace crawl.Extensions {
    public static class ServiceCollectionExtension {
        public static IServiceCollection AddServices(this IServiceCollection services) {
            services.AddSingleton<IOrchestrator, PipelineOrchestrator>();
            services.AddSingleton(service => {
                var configurations = service.GetRequiredService<IOptions<Configurations>>().Value;
                return new ChannelBus(configurations.Channel_Processing_Capacity);
            });

            services.AddSingleton<InputService>();
            services.AddHostedService(provider => provider.GetRequiredService<InputService>());

            services.AddSingleton<ScopeFilterService>();
            services.AddHostedService(provider => provider.GetRequiredService<ScopeFilterService>());

            services.AddSingleton<PrefetchDedupService>();
            services.AddHostedService(provider => provider.GetRequiredService<PrefetchDedupService>());

            services.AddSingleton<FrontierService>();
            services.AddHostedService(provider => provider.GetRequiredService<FrontierService>());

            services.AddSingleton<FetcherService>();
            services.AddHostedService(provider => provider.GetRequiredService<FetcherService>());

            services.AddSingleton<ExtractorService>();
            services.AddHostedService(provider => provider.GetRequiredService<ExtractorService>());

            services.AddSingleton<PostfetchDedupService>();
            services.AddHostedService(provider => provider.GetRequiredService<PostfetchDedupService>());

            services.AddSingleton<OutputService>();
            services.AddHostedService(provider => provider.GetRequiredService<OutputService>());
            
            services.AddHttpClient("InterServicesClient", (service, client) => {
                var configurations = service.GetRequiredService<IOptions<Configurations>>().Value;

                client.BaseAddress = new Uri(configurations.Inter_Services_Pub_Sub_Url);
                client.Timeout = TimeSpan.FromSeconds(configurations.Http_Client_Request_Timeout_Seconds);
                client.DefaultRequestHeaders.Accept.Clear();
            });
            services.AddHttpClient("CrawlClient", (service, client) => {
                var configurations = service.GetRequiredService<IOptions<Configurations>>().Value;

                client.Timeout = TimeSpan.FromSeconds(configurations.Http_Client_Request_Timeout_Seconds);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
                client.DefaultRequestHeaders.Add("User-Agent", configurations.User_Agent);
            });

            return services;
        }
    }
}
