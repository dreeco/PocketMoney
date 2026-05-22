using CSharpFunctionalExtensions;
using Domain.Entities;
using Domain.Repositories;
using System.Security.Cryptography;

namespace Application;

public class TaskSelector
{
    ICleaningTasksRepository CleaningTasksRepository { get; set; }
    public TaskSelector(ICleaningTasksRepository cleaningTasksRepository)
    {
        CleaningTasksRepository = cleaningTasksRepository;
    }

    public async Task<Result<SelectedTaskResponse>> GetTaskFor(Member member)
    {
        // Recently completed tasks
        var tasksAlreadyDoneResult = await CleaningTasksRepository.GetTaskRecentlyDone();
        if (!tasksAlreadyDoneResult.TryGetValue(out var tasksAlreadyDone))
            return Result.Failure<SelectedTaskResponse>(tasksAlreadyDoneResult.Error);

        // All tasks
        var allTasksResult = await CleaningTasksRepository.GetCleaningTasks(member);
        if (!allTasksResult.TryGetValue(out var allTasks))
            return Result.Failure<SelectedTaskResponse>(allTasksResult.Error);

        // Filter recently completed tasks
        var leftToDo = allTasksResult.Value
            .Where(t => !tasksAlreadyDone.Any(d => d.taskId == t.taskId && d.date > GetAfterDate(t.frequency)))
            .ToList();

        // Get a random one
        var nextToDo = leftToDo.ElementAt(RandomNumberGenerator.GetInt32(0, leftToDo.Count));

        var balanceResult = await CleaningTasksRepository.FindBalanceForMember(member);
        if (!balanceResult.TryGetValue(out var balance))
            return Result.Failure<SelectedTaskResponse>(balanceResult.Error);

        // Select it in database
        await CleaningTasksRepository.SelectTask(nextToDo.taskId, member, balance, nextToDo.points / 100d);

        return new SelectedTaskResponse(nextToDo.name, nextToDo.description, nextToDo.points);
    }


    public async Task<Result<MemberBalanceResponse>> GetMemberBalance(Member member)
    {
        var balanceResult = await CleaningTasksRepository.FindBalanceForMember(member);
        if (!balanceResult.TryGetValue(out var balance))
            return Result.Failure<MemberBalanceResponse>(balanceResult.Error);
        return new MemberBalanceResponse(member.name, balance.amount, balance.pendingAmount);
    }

    public static DateTime GetAfterDate(Frequency frequency)
    {
        if (frequency == Frequency.Daily)
            return DateTime.UtcNow.Date;

        DateTime today = DateTime.UtcNow.Date;
        int diff = (int)today.DayOfWeek - (int)DayOfWeek.Monday;
        if (diff < 0) diff += 7;
        return today.AddDays(-1 * diff);
    }
}
