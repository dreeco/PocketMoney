using CSharpFunctionalExtensions;
using Domain.BudgetEntities;

namespace Domain.Services;

public interface IGenAiBudgetService
{
    Task<Result<RouteAction>> ParseRouteFromMessage(string rawUserInput, CancellationToken cancellationToken);
    Task<Result<Expense>> ParseIncomeAsync(string rawUserInput, string recurringCredits, CancellationToken cancellationToken);
    Task<Result<Expense>> ParseExpenseAsync(string rawUserInput, string recurringDebits, CancellationToken cancellationToken);
    Task<Result<Situation>> EvaluateSituation(string rawUserInput, string recurringDebits, string billingMonths, CancellationToken cancellationToken);
}
