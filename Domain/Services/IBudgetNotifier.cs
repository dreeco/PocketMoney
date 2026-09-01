using CSharpFunctionalExtensions;
using Domain.BudgetEntities;

namespace Domain.Services;

public interface IBudgetNotifier
{
    Task<Result> NotifyAllBudgetUsersFromNewMessage(UserRequestResponse response, CancellationToken cancellationToken);
    Task<Result> SendMessageToUniqueUser(long userId, UserRequestResponse response, CancellationToken cancellationToken);
}
