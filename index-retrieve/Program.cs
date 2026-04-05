using index_retrieve.Extensions;

namespace index_retrieve {
    public class Program {
        public static void Main(string[] args) {
            var builder = WebApplication.CreateBuilder(args);
            var section = builder.Configuration.GetSection("Configurations");

            builder.Services.Configure<Configurations>(section);
            builder.Services.AddServices();
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            if (app.Environment.IsDevelopment()) {
                app.UseSwagger();
                app.UseSwaggerUI();
            }
            
            app.UseAuthorization();
            app.MapControllers();
            app.Run();
        }
    }
}
