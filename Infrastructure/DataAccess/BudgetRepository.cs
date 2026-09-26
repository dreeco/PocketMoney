using Application.Helpers;
using CSharpFunctionalExtensions;
using Domain.BudgetEntities;
using Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Notion.Client;
using System.Data;
using System.Diagnostics;
using System.Net;

namespace Infrastructure.DataAccess;

public class BudgetRepository : IBudgetRepository
{
    private readonly ILogger<IBudgetRepository> _logger;

    private NotionClient Client { get; set; }
    private string DebitsDataset { get; set; }
    private string CreditsDataset { get; set; }
    private string RecurringDebitsDataset { get; set; }
    private string RecurringCreditsDataset { get; set; }
    private string BillingMonthsDataset { get; set; }
    public TimeProvider TimeProvider { get; }

    private NotionDatasetExporter NotionDatasetExporter { get; }

    public BudgetRepository(ILogger<BudgetRepository> logger, IConfiguration configuration, TimeProvider timeProvider)
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
        _logger = logger;
        TimeProvider = timeProvider;
        NotionDatasetExporter = new NotionDatasetExporter(Client, logger);
    }

    public async Task<Result<BillingMonth>> GetCurrentBillingMonth(CancellationToken cancellationToken)
    {
        var today = TimeProvider.GetUtcNow().Date;

        var startDate = new DateTime(today.Year, today.Month, 1);
        if (today.Day >= 20)
            startDate = startDate.AddMonths(1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var queryParameters = NotionHelper.GetParameters([new DateFilter("Date", onOrAfter: startDate), new DateFilter("Date", onOrBefore: endDate)]);
        var response = await QueryNotionBudgetPage(BillingMonthsDataset, queryParameters, cancellationToken);

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

    public async Task<Result<IEnumerable<RecurringDebitPage>>> GetRecurrentDebitsWithNoExpenseForCurrentMonth(CancellationToken cancellationToken)
    {
        var queryParameters = NotionHelper.GetParameters([
            new FormulaFilter("Budget mois courant", @string: new TextFilter.Condition(equal: "⏳ En attente")),
            new FormulaFilter("Date Débit Estimé", date: new DateFilter.Condition(onOrBefore: DateTime.Now.Date)),
            new CheckboxFilter("Automatique", equal: true)
            ]);
        
        var response = await QueryNotionBudgetPage(RecurringDebitsDataset, queryParameters, cancellationToken);

        return Result.Success(response.Results
            .Select(r => GetRecurringDebitFromPage(r as Page))
            .Select(r => r.Value));
    }

    private Result<RecurringDebitPage> GetRecurringDebitFromPage(Page? page)
    {
        if (page == null)
            return Result.Failure<RecurringDebitPage>("No billing month found.");

        var name = NotionHelper.GetString(page.Properties["Name"]);
        var amount = NotionHelper.GetDouble(page.Properties["Montant"]);
        var category = NotionHelper.GetString(page.Properties["Catégorie"]);
        var progressive = NotionHelper.GetBoolean(page.Properties["Progressif"]);
        var currentState = NotionHelper.GetString(page.Properties["Budget mois courant"]);
        var isTransfer = NotionHelper.GetBoolean(page.Properties["Virement"]);

        var results = new Result[] { name, amount, category, progressive, currentState, isTransfer }.Where(r => r.IsFailure);
        if (results.Any())
            return Result.Failure<RecurringDebitPage>($"Errors: {String.Join(", ", results.Select(r => r.Error))}");

        return new RecurringDebitPage(page.Id, name.Value, amount.Value, category.Value, progressive.Value, currentState.Value, isTransfer.Value);
    }

    public async Task<Result<IEnumerable<BasePage>>> CreateExpenses(IEnumerable<Expense> expenses, CancellationToken cancellationToken)
    {
        var billingMonthResult = await GetCurrentBillingMonth(cancellationToken);
        if (!billingMonthResult.IsSuccess)
            return Result.Failure<IEnumerable<BasePage>>(billingMonthResult.Error);

        var createPageParameters = expenses.Select(expense =>
        {
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

            return new PagesCreateParameters
            {
                Parent = new DatabaseParentInput { DatabaseId = DebitsDataset },
                Properties = properties
            };
        });

        var pages = await NotionHelper.BatchCreateNotionPages(Client, _logger, createPageParameters, cancellationToken);
        return Result.Success(pages.Select(page => new BasePage(page.Id, page.Url, NotionHelper.GetString(page.Properties["Titre"]).Value)));
    }


    public async Task<Result<BasePage>> CreateIncome(Expense expense, CancellationToken cancellationToken)
    {
        var billingMonthResult = await GetCurrentBillingMonth(cancellationToken);
        if (!billingMonthResult.IsSuccess)
            return Result.Failure<BasePage>(billingMonthResult.Error);

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
        
        var  page = await NotionHelper.CreateNotionPage(Client, _logger, createPageParameters, cancellationToken);
        if (page == null)
            return Result.Failure<BasePage>("Could not create Notion page");

        return Result.Success(new BasePage(page.Id, page.Url, expense.Description));
    }

    public async Task<Result<string>> FetchAllBillingMonths(CancellationToken cancellationToken)
    {
        var result = await NotionDatasetExporter.ExportToCsvAsync(BillingMonthsDataset, cancellationToken);
        if (result == null)
            return Result.Failure<string>("Could not fetch billing months");

        return result;
    }

    public async Task<Result<string>> FetchAllRecurringDebits(CancellationToken cancellationToken)
    {
        var result = await NotionDatasetExporter.ExportToCsvAsync(RecurringDebitsDataset, cancellationToken, ["Name", "Montant", "Virement", "Catégorie", "Notion Page Id"]);
        if (result == null)
            return Result.Failure<string>("Could not fetch recurring debits");

        return result;
    }


    public async Task<Result<string>> FetchAllRecurringCredits(CancellationToken cancellationToken)
    {
        var result = await NotionDatasetExporter.ExportToCsvAsync(RecurringCreditsDataset, cancellationToken);
        if (result == null)
            return Result.Failure<string>("Could not fetch recrring credits");

        return result;
    }

    public async Task<Result<BudgetInformation>> GetBudgetInformation(string recurringDebitId, CancellationToken cancellationToken)
    {
        var page = await RetrieveSinglePage(recurringDebitId, cancellationToken);

        var name = NotionHelper.GetString(page.Properties["Name"]);
        var currentMonthInfo = NotionHelper.GetString(page.Properties["Budget mois courant"]);
        var isTransfer = NotionHelper.GetBoolean(page.Properties["Virement"]);

        if (!name.IsSuccess || !currentMonthInfo.IsSuccess)
            return Result.Failure<BudgetInformation>("Impossible de récupérer le budget");

        return new BudgetInformation(page.Id, name.Value, currentMonthInfo.Value, isTransfer.Value);
    }


    private async Task<DatabaseQueryResponse> QueryNotionBudgetPage(string dataset, DatabasesQueryParameters queryParameters, CancellationToken cancellationToken)
    {
        var stopWatch = GetStartedStopWatch();

        try
        {
            return await Client.Databases.QueryAsync(dataset, queryParameters, cancellationToken);
        }
        finally
        {
            _logger.LogInformation("QueryNotionBudgetPage {dataset} for {elapsedTime}ms", dataset, stopWatch.ElapsedMilliseconds);
        }
    }

    private static Stopwatch GetStartedStopWatch()
    {
        var stopWatch = new Stopwatch();
        stopWatch.Start();
        return stopWatch;
    }

    private async Task<Page> RetrieveSinglePage(string pageId, CancellationToken cancellationToken)
    {
        var stopWatch = GetStartedStopWatch();

        try
        {
            return await Client.Pages.RetrieveAsync(pageId, cancellationToken);
        }
        finally
        {
            _logger.LogInformation("RetrieveSinglePage {pageId} for {elapsedTime}ms", pageId, stopWatch.ElapsedMilliseconds);
        }
    }


}
