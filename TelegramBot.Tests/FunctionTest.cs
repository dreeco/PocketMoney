using Application.Helpers;
using Domain.BudgetEntities;
using Domain.Repositories;
using Infrastructure.AI;
using Infrastructure.DataAccess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Notion.Client;
using Telegram.Bot;
using Telegram.Bot.Types;
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

    //[Fact]
    //public async Task TestSendMessage()
    //{

    //    var notifier = new BudgetNotifier(new TelegramBotClient(Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")));

    //    await notifier.SendMessage(8662514156, "toto", new Button("ici", "https://google.fr"));
    //}


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
        var csv = await datasetExporter.ExportToCsvAsync(Environment.GetEnvironmentVariable("recurringDebitsDataset"), ["Name"], ["Courses"]);
        var json = await datasetExporter.ExportToJsonAsync(Environment.GetEnvironmentVariable("recurringDebitsDataset"));

        Assert.NotEmpty(yml);
        Assert.NotEmpty(md);
        Assert.NotEmpty(csv);
        Assert.DoesNotContain("Notion Page Id", csv);
        Assert.Equal(2, csv.Split(Environment.NewLine).Count(l => !string.IsNullOrWhiteSpace(l)));
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

    [Theory]
    [InlineData("37.95 testeur cloture lysadis", "SaisieDépense")]
    [InlineData("piles caméra blink jardin 11.89", "SaisieDépense")]
    [InlineData("Chèque 105,26 ferme", "SaisieDépense")]

    [InlineData("Rmbst vélo 14.42", "SaisieRevenu")]
    [InlineData("13€ remboursement myriam", "SaisieRevenu")]
    [InlineData("Salaire Adrien 5142,56", "SaisieRevenu")]
    [InlineData("1988,56 justine salaire", "SaisieRevenu")]

    [InlineData("voir situation", "RésuméSituation")]
    [InlineData("Où on en est ce mois-ci", "RésuméSituation")]
    [InlineData("Budgets mois", "RésuméSituation")]
    public async Task TestRouteAction(string message, string expectedAction)
    {
        var geminiParser2 = new GenAiBudgetService(Environment.GetEnvironmentVariable("GEMINI_API_KEY"));
        var routeActionResult2 = await geminiParser2.ParseRouteFromMessage(message);

        Assert.True(routeActionResult2.IsSuccess, routeActionResult2.IsFailure ? routeActionResult2.Error : string.Empty);
        Assert.Equal(expectedAction, routeActionResult2.Value.Action);

    }

    [Theory]
    [InlineData("37.95 testeur cloture lysadis", 37.95d, false, "3b8bbbc3b4e98091ab8cf46e35a8be77")]
    [InlineData("piles caméra blink jardin 11.89", 11.89d, false, "3b8bbbc3b4e98091ab8cf46e35a8be77")]
    [InlineData("Abonnement Lunii 10.90", 10.9d, false, "3bbbbbc3b4e980dfadcee70bb32d2c0f")]
    [InlineData("Chèque 105,26 ferme", 105.26d, true, "3b7bbbc3b4e980fba81cd7ecb64c04ce")]
    [InlineData("Retrait 100€ maréchal", 100d, true, "3babbbc3b4e98050bd2af89429bc2e35")]
    public async Task TestParseExpense(string message, double expectedAmount, bool expectedIsTransfer, string expectedRecurringDebitId)
    {
        var geminiParser = new GenAiBudgetService(Environment.GetEnvironmentVariable("GEMINI_API_KEY"));

        var budgetRepository = new BudgetRepository(Configuration, TimeProvider.System);
        var recurringDebits = await budgetRepository.FetchAllRecurringDebits();

        var expenseResult = await geminiParser.ParseExpenseAsync(message, recurringDebits.Value);

        Assert.True(expenseResult.IsSuccess, expenseResult.IsFailure ? expenseResult.Error : string.Empty);
        Assert.Equal(expectedAmount, expenseResult.Value.Amount);
        Assert.True(expenseResult.Value.IsValidExpense);
        Assert.Equal(expectedIsTransfer, expenseResult.Value.IsTransfer);
        Assert.Equal(expectedRecurringDebitId, expenseResult.Value.RecurringDebitId);
    }

    [Theory]
    [InlineData("remboursement 37.95 testeur cloture lysadis", 37.95d, false, "")]
    [InlineData("Remboursement virement Alan médecin Adrien 9", 9d, true, "")]
    [InlineData("Salaire Adrien 5412.17", 5412.17d, true, "3b9bbbc3b4e980399630d21f3160f46e")]
    [InlineData("Justine Salaire 1977.89", 1977.89d, true, "3b9bbbc3b4e980098134dc32c958d783")]
    [InlineData("CAF 421€", 421d, true, "3b9bbbc3b4e980e08ba6ce81fc647979")]
    public async Task TestParseIncome(string message, double expectedAmount, bool expectedIsTransfer, string expectedRecurringDebitId)
    {
        var geminiParser = new GenAiBudgetService(Environment.GetEnvironmentVariable("GEMINI_API_KEY"));

        var budgetRepository = new BudgetRepository(Configuration, TimeProvider.System);
        var recurringDebits = await budgetRepository.FetchAllRecurringCredits();

        var expenseResult = await geminiParser.ParseIncomeAsync(message, recurringDebits.Value);

        Assert.True(expenseResult.IsSuccess, expenseResult.IsFailure ? expenseResult.Error : string.Empty);
        Assert.Equal(expectedAmount, expenseResult.Value.Amount);
        Assert.True(expenseResult.Value.IsValidExpense);
        Assert.Equal(expectedIsTransfer, expenseResult.Value.IsTransfer);
        Assert.Equal(expectedRecurringDebitId, expenseResult.Value.RecurringDebitId);
    }

    [Fact]
    public async Task TestCreateIncome()
    {
        var budgetRepository = new BudgetRepository(Configuration, TimeProvider.System);
        var createdIncome = await budgetRepository.CreateIncome(new Expense() { Amount = 42.42, Category = "Remboursement", Description = "Remboursement Amazon divers", IsTransfer = true, RecurringDebitId = "c28bbbc3b4e98398a546818226f9904a" });

        Assert.True(createdIncome.IsSuccess);
        Assert.NotEmpty(createdIncome.Value.id);
    }


    [Fact]
    public async Task TestSendNotif() 
    {
        var expense = new Expense() { 
            Amount = 34.95, 
            Category = "Travail", 
            IsTransfer = false, 
            PageUrl = "https://app.notion.com/p/Filament-3D-Bambulab-3cebbbc3b4e9811997dbf32e531f4fb5", 
            Description = "Filament 3D Bambulab", 
            IsValidExpense = true, 
            RecurringDebitId = "3b8bbbc3b4e98091ab8cf46e35a8be77", 
            RecurringDebitName = "Plaisir, Vêtements, Brico, divers" };


        var budgetNotifier = new BudgetNotifier(new TelegramBotClient(Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")));

        var budgetRepository = new BudgetRepository(Configuration, TimeProvider.System);
        var budgetLeftResult = await budgetRepository.GetBudgetInformation(expense.RecurringDebitId);
        Assert.True(budgetLeftResult.IsSuccess);

        var messageResult = await budgetNotifier.NotifyBudgetUsersFromNewExpense(8662514156, expense, budgetLeftResult.Value);
        Assert.True(messageResult.Result.IsSuccess);
        Assert.NotNull(messageResult.Reply);
    }
}
