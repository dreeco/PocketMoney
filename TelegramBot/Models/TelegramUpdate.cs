using System.Text.Json.Serialization;

namespace TelegramBot.Models;

public class TelegramUpdate
{
    [JsonPropertyName("message")]
    public TelegramMessage? Message { get; set; }
}

