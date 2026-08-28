using CSharpFunctionalExtensions;
using Domain.PocketMoneyEntities;
using Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Notion.Client;

namespace Infrastructure.DataAccess;

public class CleaningTasksRepository : ICleaningTasksRepository
{
    private NotionClient Client { get; set; }
    private string ExecutedTasksDataset { get; set; }
    private string CleaningTasksDataset { get; set; }
    private string BalanceDataset { get; set; }

    public CleaningTasksRepository(IConfiguration configuration)
    {
        Client = NotionClientFactory.Create(new ClientOptions
        {
            AuthToken = configuration.GetRequiredSection("authToken").Value
        });

        ExecutedTasksDataset = configuration.GetRequiredSection("executedTasksDataset").Value ?? throw new ArgumentNullException(nameof(ExecutedTasksDataset));
        CleaningTasksDataset = configuration.GetRequiredSection("cleaningTasksDataset").Value ?? throw new ArgumentNullException(nameof(CleaningTasksDataset));
        BalanceDataset = configuration.GetRequiredSection("balanceDataset").Value ?? throw new ArgumentNullException(nameof(BalanceDataset));
    }

    public async Task<Result<IReadOnlyList<CleaningTask>>> GetCleaningTasks(Member member)
    {
        var queryParameters = NotionHelper.GetParameters([new MultiSelectFilter("Qui", contains: member.name)]);

        var response = await Client.Databases.QueryAsync(CleaningTasksDataset, queryParameters);

        return response.Results
            .Select(r => GetCleaningTaskFromPage(r as Page))
            .Where(r => r.IsSuccess)
            .Select(r => r.Value)
            .ToList();
    }

    public async Task<Result<IReadOnlyList<TaskExecuted>>> GetTaskRecentlyDone()
    {
        DatabasesQueryParameters queryParameters = NotionHelper.GetParameters([
                new DateFilter("Date", onOrAfter: DateTime.UtcNow.Date.AddDays(-7))
        ]);

        var response = await Client.Databases.QueryAsync(ExecutedTasksDataset, queryParameters);
        var tasks = new List<TaskExecuted>();
        foreach (var result in response.Results)
        {
            var page = result as Page;
            var name = NotionHelper.GetString(page?.Properties["Tâche"]);
            var date = NotionHelper.GetDate(page?.Properties["Date"]);
            var memberName = NotionHelper.GetString(page?.Properties["Qui"]);
            var validated = NotionHelper.GetBoolean(page?.Properties["Validée"]);

            if (!name.IsSuccess || !date.IsSuccess || !validated.IsSuccess || !memberName.IsSuccess)
                continue;

            tasks.Add(new TaskExecuted(name.Value!, new Member(memberName.Value), date.Value!, validated.Value!));
        }
        return tasks;
    }

    public async Task<Result<CleaningTask>> GetTask(string taskName)
    {
        var queryParameters = NotionHelper.GetParameters([new TitleFilter("Nom", equal: taskName)]);

        var response = await Client.Databases.QueryAsync(CleaningTasksDataset, queryParameters);

        return response.Results
            .Select(r => GetCleaningTaskFromPage(r as Page))
            .Select(r => r.Value)
            .Single();
    }

    public async Task<Result> SelectTask(string taskId, Member member, Balance balance, double amount)
    {
        var properties = new Dictionary<string, PropertyValue>
        {
            ["Tâche"] = new RelationPropertyValue
            {
                Relation = [new ObjectId { Id = taskId }]
            },
            ["Qui"] = new SelectPropertyValue
            {
                Select = new SelectOption { Name = member.name }
            },
            ["Balance"] = new RelationPropertyValue
            {
                Relation = [new ObjectId { Id = balance.balanceId }]
            },
            ["Montant"] = new NumberPropertyValue
            {
                Number = amount
            },

        };
        var createPageParameters = new PagesCreateParameters
        {
            Parent = new DatabaseParentInput { DatabaseId = ExecutedTasksDataset },
            Properties = properties
        };

        await Client.Pages.CreateAsync(createPageParameters);
        return Result.Success();
    }

    public async Task<Result<Balance>> FindBalanceForMember(Member member)
    {
        var queryParameters = NotionHelper.GetParameters([new TitleFilter("Nom", equal: member.name)]);
        var response = await Client.Databases.QueryAsync(BalanceDataset, queryParameters);

        var page = response.Results.Single() as Page;
        if (page == null)
            return Result.Failure<Balance>("No balance page found for the member.");
        var id = page.Id;
        var toGive = NotionHelper.GetDouble(page.Properties["A donner"]);
        var waitingForValidation = NotionHelper.GetDouble(page.Properties["En attente pour tâches ménagères"]);

        if (!toGive.IsSuccess || !waitingForValidation.IsSuccess)
            return Result.Failure<Balance>($"Errors: {(toGive.IsSuccess ? "" : toGive.Error)} {(waitingForValidation.IsSuccess ? "" : waitingForValidation.Error)}");

        return new Balance(id, AmountToPoints(toGive), AmountToPoints(waitingForValidation));
    }

    private static int AmountToPoints(Result<double> amount)
    {
        return (int)(amount.Value * 100d);
    }

    private Result<CleaningTask> GetCleaningTaskFromPage(Page? page)
    {
        if (page == null)
            return Result.Failure<CleaningTask>("No cleaning task found for the member.");

        var name = NotionHelper.GetString(page.Properties["Nom"]);
        var description = NotionHelper.GetString(page.Properties["Description"]);
        var points = NotionHelper.GetInt(page.Properties["Points"]);
        var who = NotionHelper.GetStringList(page.Properties["Qui"]);
        var frequency = NotionHelper.GetString(page.Properties["Fréquence"]);

        if (!name.IsSuccess || !description.IsSuccess || !points.IsSuccess || !who.IsSuccess || !frequency.IsSuccess)
            return Result.Failure<CleaningTask>($"Errors: {(name.IsSuccess ? "" : name.Error)} {(description.IsSuccess ? "" : description.Error)} {(points.IsSuccess ? "" : points.Error)} {(who.IsSuccess ? "" : who.Error)}");

        return new CleaningTask(
            page.Id,
            name.Value,
            description.Value,
            points.Value,
            who.Value.Select(w => new Member(w)).ToList(),
            frequency.Value == "quotidien" ? Frequency.Daily : Frequency.Weekly);
    }

}
