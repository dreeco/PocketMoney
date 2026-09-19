namespace Domain.BudgetEntities;
public record UserRequestResponse(string Answer, IEnumerable<Button>? Buttons = null);
