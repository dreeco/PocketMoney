namespace Domain.Entities;

public record CleaningTask(string taskId, string name, string description, int points, IReadOnlyList<Member> able, Frequency frequency);
