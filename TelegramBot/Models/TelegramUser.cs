using System.Text.Json.Serialization;

namespace TelegramBot.Models;

public class TelegramUser
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }
}
