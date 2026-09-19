namespace Domain.BudgetEntities;

public record RecurringDebitPage(string Id, string Name, double Amount, string Category, bool Progressive, string CurrentState, bool IsTransfer);
