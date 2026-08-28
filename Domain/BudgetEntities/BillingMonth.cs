namespace Domain.BudgetEntities;

public record AccountSituation();

public record BillingMonth(string Id, string Name, AccountSituation situation);
