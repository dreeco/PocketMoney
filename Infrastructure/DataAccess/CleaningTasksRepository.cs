using CSharpFunctionalExtensions;
using Domain.Entities;
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
        var queryParameters = GetParameters([new MultiSelectFilter("Qui", contains: member.name)]);

        var response = await Client.Databases.QueryAsync(CleaningTasksDataset, queryParameters);

        return response.Results
            .Select(r => GetCleaningTaskFromPage(r as Page))
            .Where(r => r.IsSuccess)
            .Select(r => r.Value)
            .ToList();
    }

    public async Task<Result<IReadOnlyList<TaskExecuted>>> GetTaskRecentlyDone()
    {
        DatabasesQueryParameters queryParameters = GetParameters([
                new DateFilter("Date", onOrAfter: DateTime.UtcNow.Date.AddDays(-7))
        ]);

        var response = await Client.Databases.QueryAsync(ExecutedTasksDataset, queryParameters);
        var tasks = new List<TaskExecuted>();
        foreach (var result in response.Results)
        {
            var page = result as Page;
            var name = GetString(page?.Properties["Tâche"]);
            var date = GetDate(page?.Properties["Date"]);
            var memberName = GetString(page?.Properties["Qui"]);
            var validated = GetBoolean(page?.Properties["Validée"]);

            if (!name.IsSuccess || !date.IsSuccess || !validated.IsSuccess || !memberName.IsSuccess)
                continue;

            tasks.Add(new TaskExecuted(name.Value!, new Member(memberName.Value), date.Value!, validated.Value!));
        }
        return tasks;
    }
    public async Task<Result<CleaningTask>> GetTask(string taskName)
    {
        var queryParameters = GetParameters([new TitleFilter("Nom", equal: taskName)]);

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
        var queryParameters = GetParameters([new TitleFilter("Nom", equal: member.name)]);
        var response = await Client.Databases.QueryAsync(BalanceDataset, queryParameters);

        var page = response.Results.Single() as Page;
        if (page == null)
            return Result.Failure<Balance>("No balance page found for the member.");
        var id = page.Id;
        var toGive = GetDouble(page.Properties["A donner"]);
        var waitingForValidation = GetDouble(page.Properties["En attente pour tâches ménagères"]);

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

        var name = GetString(page.Properties["Nom"]);
        var description = GetString(page.Properties["Description"]);
        var points = GetInt(page.Properties["Points"]);
        var who = GetStringList(page.Properties["Qui"]);
        var frequency = GetString(page.Properties["Fréquence"]);

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

    private static DatabasesQueryParameters GetParameters(List<Filter> filters)
    {
        var queryParameters = new DatabasesQueryParameters
        {
            PageSize = int.MaxValue,
            Filter = new CompoundFilter
            {
                And = filters,
            }
        };
        return queryParameters;
    }

    Result<IReadOnlyList<string>> GetStringList(PropertyValue? p)
    {
        if (p is null)
            return Result.Failure<IReadOnlyList<string>>("Property is null.");

        switch (p)
        {
            case MultiSelectPropertyValue multiSelectPropertyValue:
                return multiSelectPropertyValue.MultiSelect.Select(v => v.Name).ToList();
            default:
                return Result.Failure<IReadOnlyList<string>>("Property value is not mapped to string list.");
        }
    }

    Result<string> GetString(PropertyValue? p)
    {
        if (p is null)
            return Result.Failure<string>("Property is null.");

        switch (p)
        {
            case RelationPropertyValue relationPropertyValue:
                var relationId = relationPropertyValue.Relation.FirstOrDefault()?.Id;
                if (relationId is null)
                    return Result.Failure<string>("Property relation is null.");
                return relationId;
            case RichTextPropertyValue richTextPropertyValue:
                var text = richTextPropertyValue.RichText.FirstOrDefault()?.PlainText;
                if (text is null)
                    return Result.Failure<string>("Property value is null.");
                return text;
            case TitlePropertyValue titlePropertyValue:
                var title = titlePropertyValue.Title.FirstOrDefault()?.PlainText;
                if (title is null)
                    return Result.Failure<string>("Property value is null.");
                return title;
            case SelectPropertyValue selectPropertyValue:
                var select = selectPropertyValue.Select.Name;
                if (select is null)
                    return Result.Failure<string>("Property value is null.");
                return select;
            default:
                return Result.Failure<string>("Property value is not mapped to string.");
        }
    }

    Result<bool> GetBoolean(PropertyValue? p)
    {
        if (p is null)
            return Result.Failure<bool>("Property is null.");

        switch (p)
        {
            case CheckboxPropertyValue value:
                return value.Checkbox;
            default:
                return Result.Failure<bool>("Property value is not mapped to bool.");
        }
    }

    Result<int> GetInt(PropertyValue? p)
    {
        return GetDouble(p).Map(d => (int)d);
    }

    Result<double> GetDouble(PropertyValue? p)
    {
        if (p is null)
            return Result.Failure<double>("Property is null.");

        switch (p)
        {
            case FormulaPropertyValue formula:
                if (formula.Formula.Number is null)
                    return Result.Failure<double>("Property value is null.");
                return formula.Formula.Number.Value;
            case NumberPropertyValue number:
                if (number.Number is null)
                    return Result.Failure<double>("Property value is null.");
                return number.Number.Value;
            default:
                return Result.Failure<double>("Property value is not mapped to int.");
        }
    }

    Result<DateTimeOffset> GetDate(PropertyValue? p)
    {
        if (p is null)
            return Result.Failure<DateTimeOffset>("Property is null.");

        switch (p)
        {
            case CreatedTimePropertyValue createTime:
                var createdTimeString = createTime.CreatedTime;
                if (createdTimeString is null)
                    return Result.Failure<DateTimeOffset>("Property value is null.");

                return DateTimeOffset.Parse(createdTimeString);

            case DatePropertyValue date:
                var parsedDate = date.Date.Start;
                if (parsedDate is null)
                    return Result.Failure<DateTimeOffset>("Property value is null.");

                return parsedDate.Value;
            default:
                return Result.Failure<DateTimeOffset>("Property value is not mapped to date.");
        }
    }
}
