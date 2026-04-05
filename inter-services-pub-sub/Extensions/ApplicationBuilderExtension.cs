using Microsoft.Extensions.Options;
using shared;

namespace inter_services_pub_sub.Extensions {
    public static class ApplicationBuilderExtension {
        public static IApplicationBuilder UseInitializations(this IApplicationBuilder application) {
            using (var scope = application.ApplicationServices.CreateScope()) {
                var configurations = scope.ServiceProvider.GetRequiredService<IOptions<Configurations>>().Value;
                MessageQueue<EncryptedPage>.Initialize(configurations.Message_Queue_Processing_Capacity, configurations.Message_Queue_Pull_Timeout_Milliseconds);
            }

            return application;
        }
    }
}
