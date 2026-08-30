using Domain.Repositories;
using Infrastructure.AI;
using Infrastructure.DataAccess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;

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

        RegisterConfiguration(services);

        // 1. Enregistrer la configuration pour pouvoir l'injecter si besoin
        services.AddSingleton(Configuration);
        services.AddHttpClient<GeminiExpenseParser>();

        // 3. Enregistrer tes repositories et services métier existants
        services.AddTransient<IBudgetRepository, BudgetRepository>();


        var botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
                       ?? throw new InvalidOperationException("TELEGRAM_BOT_TOKEN missing.");

        // Enregistrement du Bot Client
        var bot = new TelegramBotClient(botToken);
        services.AddSingleton<ITelegramBotClient>(bot);
        services.AddSingleton<IBudgetNotifier>(new BudgetNotifier(bot));
        services.AddSingleton(TimeProvider.System);

        // services.AddTransient<IGeminiCategorizerService, GeminiCategorizerService>();
        // services.AddTransient<IExpenseService, ExpenseService>();

        return services.BuildServiceProvider();
    }

    private static void RegisterConfiguration(ServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
    }


}