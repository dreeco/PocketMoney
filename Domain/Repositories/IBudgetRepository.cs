using CSharpFunctionalExtensions;
using Domain.BudgetEntities;

namespace Domain.Repositories;

public interface IBudgetRepository
{
    Task<Result<ExpensePage>> CreateExpense(Expense expense);
}
