namespace Domain.PocketMoneyEntities;

public record TaskExecuted(string taskId, Member member, DateTimeOffset date, bool validated);
