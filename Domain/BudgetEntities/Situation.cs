using System.Text.Json.Serialization;

namespace Domain.BudgetEntities;

public class Situation
{
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
}
