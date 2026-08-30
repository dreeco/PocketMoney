using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using CSharpFunctionalExtensions;
using Domain.Repositories;
using Infrastructure.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBot.Models;

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

        // Vos catégories prédéfinies pour Notion
        string[] availableCategories =
        [
            "Alimentation",
            "Animaux",
            "Assurances",
            "Cadeaux",
            "Dons",
            "Frais bancaires",
            "Frais maison",
            "Habits",
            "Impôts",
            "Internet",
            "Santé",
            "Sorties",
            "Travail",
            "Vacances",
            "Voiture"
        ];


        var parser = _serviceProvider.GetRequiredService<GeminiExpenseParser>();
        var parsedExpense = await parser.ParseExpenseAsync(message.Text, availableCategories);

        if (!parsedExpense.IsSuccess)
        {
            var failPayload = new
            {
                method = "sendMessage",
                chat_id = message.From.Id,
                text = "⚠️ Je n'ai pas pu identifier le montant ou la dépense. Exemple : *'Courses carrefour 35€'*",
                parse_mode = "Markdown"
            };
            return SendJsonResponse(failPayload);
        }

        var text = "";
        var pages = new List<object>();
        var budgetRepository = _serviceProvider.GetRequiredService<IBudgetRepository>();

        var budgetNotifier = _serviceProvider.GetRequiredService<IBudgetNotifier>();
        foreach (var expense in parsedExpense.Value)
        {

            context.Logger.LogInformation(
            "Creating Notion expense: Amount={Amount}, Description={Description}, Category={Category}, RecurringDebitId={RecurringDebitId}, RecurringDebitName={RecurringDebitName}",
            expense.Amount,
            expense.Description,
            expense.Category,
            expense.RecurringDebitId,
            expense.RecurringDebitName
        );

            var result = await budgetRepository.CreateExpense(expense);
            if (!result.IsSuccess)
                throw new Exception("Impossible to create expense: " + result.Error);

            var budgetLeftResult = await budgetRepository.GetBudgetInformation(expense.RecurringDebitId);
            if (!budgetLeftResult.IsSuccess)
                continue;

            text += $@"
💵 Dépense ""{expense.Description}"" enregistrée !
• **Montant :** {expense.Amount:C}
• **Catégorie :** {expense.Category}
• **Dépense récurrente: ** {expense.RecurringDebitName}

{budgetLeftResult.Value.CurrentMonthInfo}
";
            pages.Add(new
            {
                text = "🔗 Voir la dépense",
                url = result.Value.url
            });



            var messageResult = await budgetNotifier.SendMessage(message.From.Id, text, new Button("🔗 Voir la dépense", result.Value.url));
            if (!messageResult.IsSuccess)
                context.Logger.LogWarning(messageResult.Error);
        }
        var replyPayload = new
        {
            method = "sendMessage",
            chat_id = message.From.Id,
            text = text,
            parse_mode = "Markdown",
            reply_markup = new
            {
                inline_keyboard = new[] { pages }
            }
        };

        return SendJsonResponse(replyPayload);
    }

    private static APIGatewayHttpApiV2ProxyResponse SendJsonResponse(object replyPayload)
    {
        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = 200,
            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } },
            Body = JsonSerializer.Serialize(replyPayload)
        };
    }
}
