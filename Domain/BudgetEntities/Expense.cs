using System.Text.Json.Serialization;

namespace Domain.BudgetEntities;

public class Expense
{
    [JsonPropertyName("amount")]
    public double Amount { get; set; }

    [JsonPropertyName("recurring_debit_id")]
    public string RecurringDebitId { get; set; } = string.Empty;

    [JsonPropertyName("recurring_debit_name")]
    public string RecurringDebitName { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("is_valid_expense")]
    public bool IsValidExpense { get; set; }
}
