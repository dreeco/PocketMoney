namespace Domain.Entities;

public record TaskExecuted(string taskId, Member member, DateTimeOffset date, bool validated);
