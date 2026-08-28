using CSharpFunctionalExtensions;
using Domain.BudgetEntities;
using Domain.PocketMoneyEntities;
using Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Notion.Client;

namespace Infrastructure.DataAccess;

public class BudgetRepository : IBudgetRepository
{
    private NotionClient Client { get; set; }
    private string DebitsDataset { get; set; }

    public BudgetRepository(IConfiguration configuration)
    {
        Client = NotionClientFactory.Create(new ClientOptions
        {
            AuthToken = configuration.GetRequiredSection("authToken").Value
        });

        DebitsDataset = configuration.GetRequiredSection("debitsDataset").Value ?? throw new ArgumentNullException(nameof(DebitsDataset));
    }

    public async Task<Result<BillingMonth>> GetCurrentBillingMonth()
    {
        var today = DateTime.Today;

        DateTime startDate;
        DateTime endDate;

        if (today.Day > 20)
        {
            startDate = new DateTime(today.Year, today.Month, 21);
            var nextMonth = today.AddMonths(1);
            endDate = new DateTime(nextMonth.Year, nextMonth.Month, 20);
        }
        else
        {
            var previousMonth = today.AddMonths(-1);
            startDate = new DateTime(previousMonth.Year, previousMonth.Month, 21);
            endDate = new DateTime(today.Year, today.Month, 20);
        }

        var queryParameters = NotionHelper.GetParameters([new DateFilter("Date", before: endDate, after: startDate)]);

        var response = await Client.Databases.QueryAsync(DebitsDataset, queryParameters);

        return response.Results
            .Select(r => GetBillingMonthFromPage(r as Page))
            .Select(r => r.Value)
            .Single();
    }

    private Result<BillingMonth> GetBillingMonthFromPage(Page? page)
    {
        if (page == null)
            return Result.Failure<BillingMonth>("No billing month found.");

        //var name = NotionHelper.GetString(page.Properties["Name"]);

        //if (!name.IsSuccess )
        //    return Result.Failure<BillingMonth>($"Errors: {(name.IsSuccess ? "" : name.Error)}");

        return new BillingMonth(
            page.Id,
            "",
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

}
