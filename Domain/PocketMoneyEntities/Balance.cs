namespace Domain.PocketMoneyEntities;

public record Balance(string balanceId, int amount, int pendingAmount);
