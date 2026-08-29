using System.Text.Json.Serialization;

namespace TelegramBot.Models;

public class TelegramMessage
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("from")]
    public TelegramUser From { get; set; } = new();
}
