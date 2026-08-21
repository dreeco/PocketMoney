using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using static Amazon.Lambda.APIGatewayEvents.APIGatewayHttpApiV2ProxyRequest.AuthorizerDescription;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TelegramBot;

public class TelegramFunction
{
    private readonly IServiceProvider _serviceProvider;
    private readonly List<long> _allowedUserIds;

    public TelegramFunction() : this(new Startup().ConfigureServices()) { }

    public TelegramFunction(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        // Récupérer les IDs autorisés depuis les variables d'environnement (ex: "123456,789012")
        var envIds = Environment.GetEnvironmentVariable("ALLOWED_USER_IDS") ?? "";
        _allowedUserIds = envIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(long.Parse)
                                .ToList();
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        context.Logger.LogInformation($"Received raw body: {request.Body}");

        if (string.IsNullOrWhiteSpace(request.Body))
            return new APIGatewayHttpApiV2ProxyResponse { StatusCode = (int)HttpStatusCode.BadRequest, Body = "Empty body" };

        TelegramUpdate? update;
        try
        {
            update = JsonSerializer.Deserialize<TelegramUpdate>(request.Body);
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error parsing JSON: {ex.Message}");
            return new APIGatewayHttpApiV2ProxyResponse { StatusCode = (int)HttpStatusCode.BadRequest, Body = "Invalid JSON" };
        }

        var message = update?.Message;
        if (message == null || string.IsNullOrWhiteSpace(message.Text))
            return new APIGatewayHttpApiV2ProxyResponse { StatusCode = (int)HttpStatusCode.NoContent, Body = "No text" };

        // 1. Sécurité : Vérifier que l'expéditeur est autorisé
        if (!_allowedUserIds.Contains(message.From.Id))
        {
            context.Logger.LogWarning($"Unauthorized sender: {message.From.Id} ({message.From.Username})");
            return new APIGatewayHttpApiV2ProxyResponse { StatusCode = (int)HttpStatusCode.Forbidden, Body = "Unauthorized" };
        }

        // 2. Traitement métier via tes services injectés
        context.Logger.LogInformation($"Processing message: '{message.Text}' from {message.From.Id}");

        // Ex: var expenseService = _serviceProvider.GetRequiredService<IExpenseService>();
        // await expenseService.ProcessAsync(message.Text);

        var amount = 42;
        var category = "Alimentation";
        var recurringDebit = "Courses";


        var replyPayload = new
        {
            method = "sendMessage",
            chat_id = message.From.Id,
            text = $"✅ Dépense enregistrée !\n• **Montant :** {amount:C}\n• **Catégorie :** {category}\n• **Dépense récurrente :** {recurringDebit}",
            parse_mode = "Markdown",
            reply_markup = new
            {
                inline_keyboard = new[]
                {
                    new[]
                    {
                        new
                        {
                            text = "🔗 Voir la dépense",
                            url = "https://app.notion.com/p/3b7bbbc3b4e980e7ac35dac965ca00d2?v=3b7bbbc3b4e980118a28000c2f2d0f3f&p=3bebbbc3b4e980e0b805db7c8e535880&pm=s"
                        }
                    }
                }
            }
        };

        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = 200,
            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } },
            Body = JsonSerializer.Serialize(replyPayload)
        };
    }
}

// Modèles C# légers pour désérialiser le message Telegram
public class TelegramUpdate
{
    [JsonPropertyName("message")]
    public TelegramMessage? Message { get; set; }
}

public class TelegramMessage
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("from")]
    public TelegramUser From { get; set; } = new();
}

public class TelegramUser
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }
}