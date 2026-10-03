using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Application.Budget;
using CSharpFunctionalExtensions;
using Domain.PocketMoneyEntities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TelegramBot.Models;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TelegramBot;

public class TelegramFunction
{
    private readonly IServiceProvider _serviceProvider;
    private readonly List<User> _allowedUserIds;

    public TelegramFunction() : this(new Startup().ConfigureServices()) { }

    public TelegramFunction(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        _allowedUserIds = UserHelper.GetAllowedUsers();
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        // Subtract a safety buffer (e.g., 500ms) to allow cleanup before AWS forcibly terminates the container
        var timeoutBuffer = TimeSpan.FromMilliseconds(500);
        var cancellationTimeout = context.RemainingTime > timeoutBuffer
            ? context.RemainingTime.Subtract(timeoutBuffer)
            : TimeSpan.FromMilliseconds(100);

        using var cts = new CancellationTokenSource(cancellationTimeout);
        CancellationToken cancellationToken = cts.Token;

        //var externalLogger = _loggerFactory.CreateLogger<TelegramFunction>();
        var externalLogger = _serviceProvider.GetRequiredService<ILogger<TelegramFunction>>();
        externalLogger.LogInformation($"Received raw body: {request.Body}");

        if (string.IsNullOrWhiteSpace(request.Body))
            return new APIGatewayHttpApiV2ProxyResponse { StatusCode = (int)HttpStatusCode.BadRequest, Body = "Empty body" };

        TelegramUpdate? update;
        try
        {
            update = JsonSerializer.Deserialize<TelegramUpdate>(request.Body);
        }
        catch (Exception ex)
        {
            externalLogger.LogError($"Error parsing JSON: {ex.Message}");
            return new APIGatewayHttpApiV2ProxyResponse { StatusCode = (int)HttpStatusCode.BadRequest, Body = "Invalid JSON" };
        }

        var message = update?.Message;
        if (message == null || string.IsNullOrWhiteSpace(message.Text))
            return new APIGatewayHttpApiV2ProxyResponse { StatusCode = (int)HttpStatusCode.NoContent, Body = "No text" };

        // 1. Sécurité : Vérifier que l'expéditeur est autorisé
        var user = _allowedUserIds.FirstOrDefault(u => u.Id == message.From.Id);
        if (user is null)
        {
            externalLogger.LogWarning($"Unauthorized sender: {message.From.Id} ({message.From.Username})");
            return new APIGatewayHttpApiV2ProxyResponse { StatusCode = (int)HttpStatusCode.Forbidden, Body = "Unauthorized" };
        }

        // 2. Traitement métier via tes services injectés
        externalLogger.LogInformation($"Processing message: '{message.Text}' from {message.From.Id}");

        var userRequestHandler = _serviceProvider.GetRequiredService<IUserRequestHandler>();
        var result = await userRequestHandler.ParseMessage(new UserMessage(message.Text, user), cancellationToken);
        if (result.IsFailure)
        {
            context.Logger.LogError("Failed on " + result.Error);
            throw new Exception(result.Error);
        }

        return NoContent();
    }

    private static APIGatewayHttpApiV2ProxyResponse NoContent()
    {
        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = (int)HttpStatusCode.NoContent,
            Body = string.Empty,
            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
        };
    }
}
