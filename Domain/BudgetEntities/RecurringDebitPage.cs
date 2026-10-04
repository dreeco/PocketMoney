namespace Domain.BudgetEntities;

public record RecurringDebitPage(string Id, string Icon, string Name, double Amount, string Category, bool Progressive, string CurrentState, bool IsTransfer, DateTimeOffset expectedPaymentDate);
