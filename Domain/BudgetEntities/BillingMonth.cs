namespace Domain.BudgetEntities;

public record AccountSituation(double LeftFromLastMonth, double BankVisible, double BankTotalCards, double BankAdrienCard, double BankJustineCard, double ExpectedEndOfMonth, double Spent);

public record BillingMonth(string Id, string Name, AccountSituation Situation);
