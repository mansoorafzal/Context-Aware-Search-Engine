using index_retrieve.InvertedIndex;
using index_retrieve.Orchestrators;
using index_retrieve.Services;
using Microsoft.Extensions.Options;

namespace index_retrieve.Extensions {
    public static class ServiceCollectionExtension {
        public static IServiceCollection AddServices(this IServiceCollection services) {
            services.AddSingleton<IOrchestrator, PipelineOrchestrator>();
            services.AddSingleton<IndexReaderFactory>();
            services.AddSingleton<IndexWriterFactory>();
            services.AddSingleton(service => {
                var configurations = service.GetRequiredService<IOptions<Configurations>>().Value;
                return new ChannelBus(configurations.Channel_Processing_Capacity);
            });

            services.AddSingleton<InputService>();
            services.AddHostedService(provider => provider.GetRequiredService<InputService>());

            services.AddSingleton<DocumentIngestorService>();
            services.AddHostedService(provider => provider.GetRequiredService<DocumentIngestorService>());

            services.AddSingleton<LuceneService>();
            services.AddHostedService(provider => provider.GetRequiredService<LuceneService>());

            services.AddSingleton<QueryParserService>();
            services.AddHostedService(provider => provider.GetRequiredService<QueryParserService>());

            services.AddSingleton<BooleanExpressionEvaluatorService>();
            services.AddHostedService(provider => provider.GetRequiredService<BooleanExpressionEvaluatorService>());

            services.AddSingleton<RankerService>();
            services.AddHostedService(provider => provider.GetRequiredService<RankerService>());

            services.AddSingleton<OutputService>();
            services.AddHostedService(provider => provider.GetRequiredService<OutputService>());

            services.AddHttpClient("InterServicesClient", (service, client) => {
                var configurations = service.GetRequiredService<IOptions<Configurations>>().Value;

                client.BaseAddress = new Uri(configurations.Inter_Services_Pub_Sub_Url);
                client.Timeout = TimeSpan.FromSeconds(configurations.Http_Client_Request_Timeout_Seconds);
                client.DefaultRequestHeaders.Accept.Clear();
            });

            return services;
        }
    }
}
