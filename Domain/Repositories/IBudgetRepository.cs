using CSharpFunctionalExtensions;
using Domain.BudgetEntities;

namespace Domain.Repositories;

public interface IBudgetRepository
{
    Task<Result<ExpensePage>> CreateExpense(Expense expense, CancellationToken cancellationToken);
    Task<Result<ExpensePage>> CreateIncome(Expense expense, CancellationToken cancellationToken);
    Task<Result<BillingMonth>> GetCurrentBillingMonth(CancellationToken cancellationToken);
    Task<Result<BudgetInformation>> GetBudgetInformation(string recurringDebitId, CancellationToken cancellationToken);
    Task<Result<string>> FetchAllRecurringDebits(CancellationToken cancellationToken);
    Task<Result<string>> FetchAllRecurringCredits(CancellationToken cancellationToken);
    Task<Result<string>> FetchAllBillingMonths(CancellationToken cancellationToken);
}
