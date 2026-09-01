using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Application.Budget;
using CSharpFunctionalExtensions;
using Domain.BudgetEntities;
using Domain.Repositories;
using Domain.Services;
using Infrastructure.AI;
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
    private readonly List<long> _allowedUserIds;
    private readonly ILoggerFactory _loggerFactory;

    public TelegramFunction() : this(new Startup().ConfigureServices()) { }

    public TelegramFunction(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        // Récupérer les IDs autorisés depuis les variables d'environnement (ex: "123456,789012")
        var envIds = Environment.GetEnvironmentVariable("ALLOWED_USER_IDS") ?? "";
        _allowedUserIds = envIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(long.Parse)
                                .ToList();

        // Create an ILoggerFactory instance configured for AWS Lambda
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddLambdaLogger(new LambdaLoggerOptions
            {
                IncludeCategory = true,
                IncludeLogLevel = true
            });
        });
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

        var externalLogger = _loggerFactory.CreateLogger<TelegramFunction>();

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
        if (!_allowedUserIds.Contains(message.From.Id))
        {
            externalLogger.LogWarning($"Unauthorized sender: {message.From.Id} ({message.From.Username})");
            return new APIGatewayHttpApiV2ProxyResponse { StatusCode = (int)HttpStatusCode.Forbidden, Body = "Unauthorized" };
        }

        // 2. Traitement métier via tes services injectés
        externalLogger.LogInformation($"Processing message: '{message.Text}' from {message.From.Id}");

        var parser = _serviceProvider.GetRequiredService<GenAiBudgetService>();

        var budgetRepository = _serviceProvider.GetRequiredService<IBudgetRepository>();
        var budgetNotifier = _serviceProvider.GetRequiredService<IBudgetNotifier>();

        var userRequestHandler = new UserRequestHandler(budgetRepository, parser, budgetNotifier);
        var result = await userRequestHandler.ParseMessage(externalLogger, message.Text, message.From.Id, cancellationToken);
        if (result.IsFailure)
            throw new Exception(result.Error);

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
