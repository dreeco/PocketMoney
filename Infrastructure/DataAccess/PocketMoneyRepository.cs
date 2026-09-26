using CSharpFunctionalExtensions;
using Domain.BudgetEntities;
using Domain.PocketMoneyEntities;
using Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Notion.Client;

namespace Infrastructure.DataAccess;

internal class PocketMoneyRepository : IPocketMoneyRepository
{
    private readonly ILogger _logger;

    private string PocketMoneyCalendarDataset { get; set; }
    public string BalanceDataset { get; }
    private NotionClient Client { get; set; }


    public PocketMoneyRepository(IConfiguration configuration, ILogger<PocketMoneyRepository> logger)
    {
        Client = NotionClientFactory.Create(new ClientOptions
        {
            AuthToken = configuration.GetRequiredSection("authToken").Value
        });

        PocketMoneyCalendarDataset = configuration.GetRequiredSection("pocketMoneyCalendar").Value ?? throw new ArgumentNullException(nameof(PocketMoneyCalendarDataset));
        BalanceDataset = configuration.GetRequiredSection("balanceDataset").Value ?? throw new ArgumentNullException(nameof(BalanceDataset));

        _logger = logger;
    }

    public async Task<Result<IEnumerable<PocketMoneyCalendarItem>>> SynchronizeCalendar(CancellationToken cancellationToken)
    {
        var queryParameters = NotionHelper.GetParameters(
            filters: [],
            sorts: [new Sort() { Direction = Direction.Descending, Property = "Date" }],
            pageSize: 1);

        var response = await Client.Databases.QueryAsync(PocketMoneyCalendarDataset, queryParameters);

        var result = response.Results.Select(r => GetPocketMoneyCalendarItem(r as Page)).FirstOrDefault();
        if (result.IsFailure)
            return Result.Failure<IEnumerable<PocketMoneyCalendarItem>>(result.Error);

        var balances = await FindBalances();
        if (balances.IsFailure)
            return Result.Failure<IEnumerable<PocketMoneyCalendarItem>>(balances.Error);

        IEnumerable<PocketMoneyCalendarItem> items = GetEachMemberDateMissing(result.Value.Date.DateTime, balances.Value);

        var creationResult = await CreateCalendarItems(items, cancellationToken);
        if (creationResult.IsFailure)
            return Result.Failure<IEnumerable<PocketMoneyCalendarItem>>(creationResult.Error);

        return Result.Success(items);
    }


    private async Task<Result<IEnumerable<Balance>>> FindBalances()
    {
        var queryParameters = NotionHelper.GetParameters([]);
        var response = await Client.Databases.QueryAsync(BalanceDataset, queryParameters);

        var balances = response.Results
            .Select(r => r as Page)
            .Where(p => p is not null)
            .Select(page =>
            {
                var id = page!.Id;

                var name = NotionHelper.GetString(page.Properties["Nom"]);
                var toGive = NotionHelper.GetDouble(page.Properties["A donner"]);
                var waitingForValidation = NotionHelper.GetDouble(page.Properties["En attente pour tâches ménagères"]);

                if (!name.IsSuccess || !toGive.IsSuccess || !waitingForValidation.IsSuccess)
                {
                    _logger.LogInformation($"Errors: {(toGive.IsSuccess ? "" : toGive.Error)} {(waitingForValidation.IsSuccess ? "" : waitingForValidation.Error)}");
                    return null;
                }

                return new Balance(name.Value, id, AmountToPoints(toGive), AmountToPoints(waitingForValidation));
            });

        return Result.Success(balances.Where(b => b is not null).Select(b => b!));
    }
    private static int AmountToPoints(Result<double> amount)
    {
        return (int)(amount.Value * 100d);
    }


    public async Task<Result<IEnumerable<BasePage>>> CreateCalendarItems(IEnumerable<PocketMoneyCalendarItem> items, CancellationToken cancellationToken)
    {
        var createPageParameters = items.Select(item =>
        {
            var properties = new Dictionary<string, PropertyValue>
            {
                ["Nom"] = new TitlePropertyValue()
                {
                    Title = [new RichTextText() { Text = new Text { Content = item.Kid } }]
                },
                ["Balance enfants"] = new RelationPropertyValue
                {
                    Relation = [new ObjectId { Id = item.BalanceId }]
                },
                ["Date"] = new DatePropertyValue
                {
                    Date = new Date() { Start = item.Date, IncludeTime = false },
                },
                ["Réussi"] = new SelectPropertyValue
                {
                    Select = new SelectOption { Name = "Très bien" }
                },
                ["Enfant"] = new SelectPropertyValue
                {
                    Select = new SelectOption { Name = item.Kid }
                },
            };

            return new PagesCreateParameters
            {
                Parent = new DatabaseParentInput { DatabaseId = PocketMoneyCalendarDataset },
                Properties = properties
            };
        });

        var pages = await NotionHelper.BatchCreateNotionPages(Client, _logger, createPageParameters, cancellationToken);
        return Result.Success(pages.Select(page => new BasePage(page.Id, page.Url, NotionHelper.GetString(page.Properties["Titre"]).Value)));
    }


    internal static IEnumerable<PocketMoneyCalendarItem> GetEachMemberDateMissing(DateTime result, IEnumerable<Balance> balances)
    {
        var nameAndDate = balances.SelectMany(balance =>
        {
            var startDate = result.Date.Date;
            var endDate = DateTime.Today;

            // skip date found
            int totalDays = (endDate - startDate).Days;

            return Enumerable
                .Range(1, totalDays)
                .Select(offset => new PocketMoneyCalendarItem(string.Empty, startDate.AddDays(offset), balance.balanceId, balance.name));

        });
        return nameAndDate;
    }

    private Result<PocketMoneyCalendarItem> GetPocketMoneyCalendarItem(Page? page)
    {
        if (page == null)
            return Result.Failure<PocketMoneyCalendarItem>("No item found in the calendar.");

        var name = NotionHelper.GetString(page.Properties["Nom"]);
        var points = NotionHelper.GetDouble(page.Properties["Valeur numérique"]);
        var who = NotionHelper.GetString(page.Properties["Enfant"]);
        var date = NotionHelper.GetDate(page.Properties["Date"]);
        var balance = NotionHelper.GetString(page.Properties["Balance enfants"]);


        var results = new Result[] { name, points, date, who }.Where(r => r.IsFailure);
        if (results.Any())
            return Result.Failure<PocketMoneyCalendarItem>($"Errors: {String.Join(", ", results.Select(r => r.Error))}");

        return new PocketMoneyCalendarItem(
            page.Id,
            date.Value,
            balance.Value,
            who.Value
        );
    }

}
