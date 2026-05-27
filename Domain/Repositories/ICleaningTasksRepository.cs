using CSharpFunctionalExtensions;
using Domain.Entities;

namespace Domain.Repositories;

public interface ICleaningTasksRepository
{
    Task<Result<IReadOnlyList<CleaningTask>>> GetCleaningTasks(Member member);
    Task<Result<IReadOnlyList<TaskExecuted>>> GetTaskRecentlyDone();
    Task<Result> SelectTask(string taskId, Member member, Balance balance, double amount);
    Task<Result<CleaningTask>> GetTask(string taskName);
    Task<Result<Balance>> FindBalanceForMember(Member member);
}
