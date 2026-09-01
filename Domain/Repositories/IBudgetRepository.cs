using CSharpFunctionalExtensions;
using Domain.BudgetEntities;

namespace Domain.Repositories;

public interface IBudgetRepository
{
    Task<Result<ExpensePage>> CreateExpense(Expense expense);
    Task<Result<ExpensePage>> CreateIncome(Expense expense);
    Task<Result<BillingMonth>> GetCurrentBillingMonth();
    Task<Result<BudgetInformation>> GetBudgetInformation(string recurringDebitId);
    Task<Result<string>> FetchAllRecurringDebits();
    Task<Result<string>> FetchAllRecurringCredits();
    Task<Result<string>> FetchAllBillingMonths();
}
