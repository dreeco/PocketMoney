using Application.Helpers;
using Domain.BudgetEntities;
using Domain.Repositories;
using Infrastructure.DataAccess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Notion.Client;
using Telegram.Bot;
using Xunit;

namespace TelegramBot.Tests;

public class FunctionTest
{
    IConfiguration Configuration;
    public FunctionTest()
    {
        Configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        foreach (var pair in Configuration.AsEnumerable())
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


    [Theory]
    [InlineData("2026-08-19", "Août 2026")]
    [InlineData("2026-08-20", "Septembre 2026")]
    [InlineData("2026-08-21", "Septembre 2026")]
    [InlineData("2026-09-01", "Septembre 2026")]
    public async Task TestFetchMonth(string givenDate, string expectedBillingMonth)
    {
        var initialTime = DateTimeOffset.Parse(givenDate);
        var fakeTimeProvider = new FakeTimeProvider(initialTime);

        var budgetRepository = new BudgetRepository(Configuration, fakeTimeProvider);

        var month = await budgetRepository.GetCurrentBillingMonth();

        Assert.True(month.IsSuccess);
        Assert.Equal(expectedBillingMonth, month.Value.Name);
    }


    [Fact]
    public async Task TestNotionDatasetExporter()
    {
        var client = NotionClientFactory.Create(new ClientOptions
        {
            AuthToken = Configuration.GetRequiredSection("authToken").Value
        });
        var datasetExporter = new NotionDatasetExporter(client);
        var yml = await datasetExporter.ExportToYamlAsync(Environment.GetEnvironmentVariable("recurringDebitsDataset"));
        var md = await datasetExporter.ExportToMarkdownAsync(Environment.GetEnvironmentVariable("recurringDebitsDataset"));
        var csv = await datasetExporter.ExportToCsvAsync(Environment.GetEnvironmentVariable("recurringDebitsDataset"));
        var json = await datasetExporter.ExportToJsonAsync(Environment.GetEnvironmentVariable("recurringDebitsDataset"));

        Assert.NotEmpty(yml);
        Assert.NotEmpty(md);
        Assert.NotEmpty(csv);
        Assert.NotEmpty(json);
    }

    [Fact]
    public async Task TestGetBudgetInformation() 
    {
        var budgetRepository = new BudgetRepository(Configuration, TimeProvider.System);
        var budgetLeftResult = await budgetRepository.GetBudgetInformation("3b8bbbc3b4e98091ab8cf46e35a8be77");

        Assert.True(budgetLeftResult.IsSuccess);
        Assert.NotEmpty(budgetLeftResult.Value.CurrentMonthInfo);

    }
}
