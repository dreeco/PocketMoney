using Application.Budget;
using Domain.Repositories;
using Domain.Services;
using Infrastructure.AI;
using Infrastructure.DataAccess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        AddLogging(services);

        RegisterConfiguration(services);

        services.AddSingleton(Configuration);
        services.AddSingleton(TimeProvider.System);

        RegisterTelegramBot(services);
        RegisterGeminiParser(services);

        services.AddTransient<IBudgetRepository, BudgetRepository>();

        services.AddTransient<IUserRequestHandler, UserRequestHandler>();

        return services.BuildServiceProvider();
    }

    private static void AddLogging(ServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddLambdaLogger(new LambdaLoggerOptions
            {
                IncludeCategory = true,
                IncludeLogLevel = true
            });

            builder.SetMinimumLevel(LogLevel.Information);
        });
    }

    private static void RegisterGeminiParser(ServiceCollection services)
    {
        var geminiApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                               ?? throw new InvalidOperationException("GEMINI_API_KEY missing.");

        services.AddSingleton<IGenAiBudgetService>(b =>
        new GenAiBudgetService(
            b.GetRequiredService<ILogger<GenAiBudgetService>>(),
            geminiApiKey
        ));
    }

    private static void RegisterTelegramBot(ServiceCollection services)
    {
        var botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
                               ?? throw new InvalidOperationException("TELEGRAM_BOT_TOKEN missing.");
        var bot = new TelegramBotClient(botToken);
        services.AddSingleton<ITelegramBotClient>(bot);

        services.AddTransient<IBudgetNotifier, BudgetNotifier>();
    }

    private static void RegisterConfiguration(ServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
    }
}