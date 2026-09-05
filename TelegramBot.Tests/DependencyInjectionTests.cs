using Application.Budget;
using Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TelegramBot.Tests;

public class DependencyInjectionTests : IDisposable
{
    public DependencyInjectionTests()
    {
        // 1. Mock required environment variables to prevent InvalidOperationException during setup
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "dummy_api_key");
        Environment.SetEnvironmentVariable("TELEGRAM_BOT_TOKEN", "8322456646:AAJGdghf6786fdkjh-dskhk6765");
        Environment.SetEnvironmentVariable("authToken", string.Empty);
        Environment.SetEnvironmentVariable("debitsDataset", string.Empty);
        Environment.SetEnvironmentVariable("creditsDataset", string.Empty);
        Environment.SetEnvironmentVariable("recurringDebitsDataset", string.Empty);
        Environment.SetEnvironmentVariable("recurringCreditsDataset", string.Empty);
        Environment.SetEnvironmentVariable("billingMonthsDataset", string.Empty);
        Environment.SetEnvironmentVariable("ALLOWED_USER_IDS", string.Empty);

    }

    [Fact]
    public void ConfigureServices_ShouldResolveAllDependencies()
    {
        // Arrange
        var startup = new Startup();
        var serviceProvider = startup.ConfigureServices();

        // Act & Assert
        // Resolving the root entry points validates their entire dependency tree down to the singletons
        var requestHandler = serviceProvider.GetRequiredService<IUserRequestHandler>();
        var budgetNotifier = serviceProvider.GetRequiredService<IBudgetNotifier>();
        var geminiService = serviceProvider.GetRequiredService<IGenAiBudgetService>();

        Assert.NotNull(requestHandler);
        Assert.NotNull(budgetNotifier);
        Assert.NotNull(geminiService);
    }

    public void Dispose()
    {
        // 2. Clean up environment variables so it doesn't affect other tests
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);
        Environment.SetEnvironmentVariable("TELEGRAM_BOT_TOKEN", null);
    }
}