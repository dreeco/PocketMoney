using Application.Helpers;
using CSharpFunctionalExtensions;
using Domain.BudgetEntities;
using Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Notion.Client;

namespace Infrastructure.DataAccess;

public class BudgetRepository : IBudgetRepository
{
    private NotionClient Client { get; set; }
    private string DebitsDataset { get; set; }
    private string CreditsDataset { get; set; }
    private string RecurringDebitsDataset { get; set; }
    private string RecurringCreditsDataset { get; set; }
    private string BillingMonthsDataset { get; set; }
    public TimeProvider TimeProvider { get; }

    private NotionDatasetExporter NotionDatasetExporter { get; }

    public BudgetRepository(IConfiguration configuration, TimeProvider timeProvider)
    {
        Client = NotionClientFactory.Create(new ClientOptions
        {
            AuthToken = configuration.GetRequiredSection("authToken").Value
        });

        DebitsDataset = configuration.GetRequiredSection("debitsDataset").Value ?? throw new ArgumentNullException(nameof(DebitsDataset));
        CreditsDataset = configuration.GetRequiredSection("creditsDataset").Value ?? throw new ArgumentNullException(nameof(CreditsDataset));
        RecurringDebitsDataset = configuration.GetRequiredSection("recurringDebitsDataset").Value ?? throw new ArgumentNullException(nameof(RecurringDebitsDataset));
        RecurringCreditsDataset = configuration.GetRequiredSection("recurringCreditsDataset").Value ?? throw new ArgumentNullException(nameof(RecurringCreditsDataset));
        BillingMonthsDataset = configuration.GetRequiredSection("billingMonthsDataset").Value ?? throw new ArgumentNullException(nameof(BillingMonthsDataset));
        TimeProvider = timeProvider;
        NotionDatasetExporter = new NotionDatasetExporter(Client);
    }

    public async Task<Result<BillingMonth>> GetCurrentBillingMonth()
    {
        var today = TimeProvider.GetUtcNow().Date;

        var startDate = new DateTime(today.Year, today.Month, 1);
        if (today.Day >= 20)
            startDate = startDate.AddMonths(1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var queryParameters = NotionHelper.GetParameters([new DateFilter("Date", onOrAfter: startDate), new DateFilter("Date", onOrBefore: endDate)]);

        var response = await Client.Databases.QueryAsync(BillingMonthsDataset, queryParameters);

        return response.Results
            .Select(r => GetBillingMonthFromPage(r as Page))
            .Select(r => r.Value)
            .Single();
    }

    private Result<BillingMonth> GetBillingMonthFromPage(Page? page)
    {
        if (page == null)
            return Result.Failure<BillingMonth>("No billing month found.");

        var name = NotionHelper.GetString(page.Properties["Name"]);

        if (!name.IsSuccess)
            return Result.Failure<BillingMonth>($"Errors: {(name.IsSuccess ? "" : name.Error)}");

        return new BillingMonth(
            page.Id,
            name.Value,
            new AccountSituation());
    }

    public async Task<Result<ExpensePage>> CreateExpense(Expense expense)
    {
        var billingMonthResult = await GetCurrentBillingMonth();
        if (!billingMonthResult.IsSuccess)
            return Result.Failure<ExpensePage>(billingMonthResult.Error);

        var properties = new Dictionary<string, PropertyValue>
        {
            ["Titre"] = new TitlePropertyValue()
            {
                Title = [new RichTextText() { Text = new Text { Content = expense.Description } }]
            },
            ["Mois"] = new RelationPropertyValue
            {
                Relation = [new ObjectId { Id = billingMonthResult.Value.Id }]
            },
            ["Date"] = new DatePropertyValue
            {
                Date = new Date() { Start = DateTimeOffset.UtcNow.Date, IncludeTime = false },
            },
            ["Montant"] = new NumberPropertyValue
            {
                Number = expense.Amount
            },
            ["Catégorie"] = new SelectPropertyValue
            {
                Select = new SelectOption { Name = expense.Category }
            },
            ["Virement"] = new CheckboxPropertyValue
            {
                Checkbox = expense.IsTransfer
            },
        };

        if (!string.IsNullOrWhiteSpace(expense.RecurringDebitId))
        {
            properties["Dépenses récurrentes"] = new RelationPropertyValue
            {
                Relation = [new ObjectId { Id = expense.RecurringDebitId }]
            };
        }

        var createPageParameters = new PagesCreateParameters
        {
            Parent = new DatabaseParentInput { DatabaseId = DebitsDataset },
            Properties = properties
        };

        var page = await Client.Pages.CreateAsync(createPageParameters);
        if (page == null)
            return Result.Failure<ExpensePage>("Could not create Notion page");
        return Result.Success(new ExpensePage(page.Id, page.Url));
    }


    public async Task<Result<ExpensePage>> CreateIncome(Expense expense)
    {
        var billingMonthResult = await GetCurrentBillingMonth();
        if (!billingMonthResult.IsSuccess)
            return Result.Failure<ExpensePage>(billingMonthResult.Error);

        var properties = new Dictionary<string, PropertyValue>
        {
            ["Titre"] = new TitlePropertyValue()
            {
                Title = [new RichTextText() { Text = new Text { Content = expense.Description } }]
            },
            ["Mois"] = new RelationPropertyValue
            {
                Relation = [new ObjectId { Id = billingMonthResult.Value.Id }]
            },
            ["Date"] = new DatePropertyValue
            {
                Date = new Date() { Start = DateTimeOffset.UtcNow.Date, IncludeTime = false },
            },
            ["Montant"] = new NumberPropertyValue
            {
                Number = expense.Amount
            },
            ["Catégorie"] = new SelectPropertyValue
            {
                Select = new SelectOption { Name = expense.Category }
            },
            ["Virement"] = new CheckboxPropertyValue
            {
                Checkbox = expense.IsTransfer
            },

        };

        if (!string.IsNullOrWhiteSpace(expense.RecurringDebitId))
        {
            properties["Revenus Récurrents"] = new RelationPropertyValue
            {
                Relation = [new ObjectId { Id = expense.RecurringDebitId }]
            };
        }

        var createPageParameters = new PagesCreateParameters
        {
            Parent = new DatabaseParentInput { DatabaseId = CreditsDataset },
            Properties = properties
        };

        var page = await Client.Pages.CreateAsync(createPageParameters);
        if (page == null)
            return Result.Failure<ExpensePage>("Could not create Notion page");
        return Result.Success(new ExpensePage(page.Id, page.Url));
    }

    public async Task<Result<string>> FetchAllBillingMonths()
    {
        var result = await NotionDatasetExporter.ExportToCsvAsync(BillingMonthsDataset);
        if (result == null)
            return Result.Failure<string>("Could not fetch billing months");

        return result;
    }

    public async Task<Result<string>> FetchAllRecurringDebits()
    {
        var result = await NotionDatasetExporter.ExportToCsvAsync(RecurringDebitsDataset);
        if (result == null)
            return Result.Failure<string>("Could not fetch recurring debits");

        return result;
    }

    public async Task<Result<string>> FetchAllRecurringCredits()
    {
        var result = await NotionDatasetExporter.ExportToCsvAsync(RecurringCreditsDataset);
        if (result == null)
            return Result.Failure<string>("Could not fetch recrring credits");

        return result;
    }

    public async Task<Result<BudgetInformation>> GetBudgetInformation(string recurringDebitId) 
    {
        var page = await Client.Pages.RetrieveAsync(recurringDebitId);

        var name = NotionHelper.GetString(page.Properties["Name"]);
        var currentMonthInfo = NotionHelper.GetString(page.Properties["Budget mois courant"]);
        var isCB = NotionHelper.GetBoolean(page.Properties["Sur la CB"]);

        if (!name.IsSuccess || !currentMonthInfo.IsSuccess)
            return Result.Failure<BudgetInformation>("Impossible de récupérer le budget");

        return new BudgetInformation(page.Id, name.Value, currentMonthInfo.Value, !isCB.Value);
    }

}
