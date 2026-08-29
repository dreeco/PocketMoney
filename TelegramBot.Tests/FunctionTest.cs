using Microsoft.Extensions.Configuration;
using Telegram.Bot;
using Xunit;

namespace TelegramBot.Tests;

public class FunctionTest
{
    public FunctionTest()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        foreach (var pair in config.AsEnumerable())
        {
            if (!string.IsNullOrEmpty(pair.Value))
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }

    [Fact]
    public async Task TestSendMessage()
    {

        var notifier = new BudgetNotifier(new TelegramBotClient(Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")));

        await notifier.SendMessage(8662514156, "toto", new Button("ici", "https://google.fr"));
    }
}
