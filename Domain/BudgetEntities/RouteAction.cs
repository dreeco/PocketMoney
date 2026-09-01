using System.Text.Json.Serialization;

namespace Domain.BudgetEntities;

public class RouteAction
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;
}
