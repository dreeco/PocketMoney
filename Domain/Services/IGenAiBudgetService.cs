using CSharpFunctionalExtensions;
using Domain.BudgetEntities;

namespace Domain.Services;

public interface IGenAiBudgetService
{
    Task<Result<RouteAction>> ParseRouteFromMessage(string rawUserInput);
    Task<Result<Expense>> ParseIncomeAsync(string rawUserInput, string recurringCredits);
    Task<Result<Expense>> ParseExpenseAsync(string rawUserInput, string recurringDebits);
    Task<Result<Situation>> EvaluateSituation(string rawUserInput, string recurringDebits, string billingMonths);
}
