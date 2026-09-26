namespace Domain.PocketMoneyEntities;

public record Balance(string name, string balanceId, int amount, int pendingAmount);
