using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TelegramBot;

public class Startup
{
    public IConfiguration Configuration { get; }

    public Startup()
    {
        var builder = new ConfigurationBuilder()
            .AddEnvironmentVariables();

        Configuration = builder.Build();
    }

    public IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // 1. Enregistrer la configuration pour pouvoir l'injecter si besoin
        services.AddSingleton(Configuration);


        // 3. Enregistrer tes repositories et services métier existants
        // services.AddTransient<INotionRepository, NotionRepository>();
        // services.AddTransient<IGeminiCategorizerService, GeminiCategorizerService>();
        // services.AddTransient<IExpenseService, ExpenseService>();

        return services.BuildServiceProvider();
    }
}