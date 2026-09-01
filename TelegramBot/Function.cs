using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using CSharpFunctionalExtensions;
using Domain.Repositories;
using Infrastructure.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
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

        var parser = _serviceProvider.GetRequiredService<GenAiBudgetService>();
        var budgetRepository = _serviceProvider.GetRequiredService<IBudgetRepository>();
        var budgetNotifier = _serviceProvider.GetRequiredService<IBudgetNotifier>();

        var action = "SaisieDépense";
        var actionResult = await parser.ParseRouteFromMessage(message.Text);
        if (actionResult.IsSuccess)
            action = actionResult.Value.Action;

        switch (action)
        {
            case "SaisieDépense":
                return await HandleNewExpense(context, message, parser, budgetRepository, budgetNotifier);
            case "SaisieRevenu":
                return await HandleNewIncome(context, message, parser, budgetRepository, budgetNotifier);
            case "RésuméSituation":
                return await HandleSituationSummary(message, parser, budgetRepository, budgetNotifier);
            default:
                break;
        }

        return SendJsonResponse(new
        {
            method = "sendMessage",
            chat_id = message.From.Id,
            text = $"⚠️ Je n'ai pas compris la demande.",
            parse_mode = "Markdown"
        });
    }

    private static async Task<APIGatewayHttpApiV2ProxyResponse> HandleSituationSummary(TelegramMessage message, GenAiBudgetService parser, IBudgetRepository budgetRepository, IBudgetNotifier budgetNotifier)
    {
        var recurringDebits2 = await budgetRepository.FetchAllRecurringDebits();
        if (recurringDebits2.IsFailure)
            throw new Exception(recurringDebits2.Error);

        var billingMonths2 = await budgetRepository.FetchAllBillingMonths();
        if (billingMonths2.IsFailure)
            throw new Exception(billingMonths2.Error);

        var situation = await parser.EvaluateSituation(message.Text, recurringDebits2.Value, billingMonths2.Value);
        if (situation.IsFailure)
            throw new Exception(situation.Error);

        var message2 = await budgetNotifier.BuildSituationSummaryAnswer(message.From.Id, situation.Value);
        return SendJsonResponse(message2);
    }

    private static async Task<APIGatewayHttpApiV2ProxyResponse> HandleNewIncome(ILambdaContext context, TelegramMessage message, GenAiBudgetService parser, IBudgetRepository budgetRepository, IBudgetNotifier budgetNotifier)
    {
        var recurringCredits = await budgetRepository.FetchAllRecurringCredits();
        if (recurringCredits.IsFailure)
            throw new Exception(recurringCredits.Error);

        var parsedIncome = await parser.ParseIncomeAsync(message.Text, recurringCredits.Value);

        if (!parsedIncome.IsSuccess)
        {
            var failPayload = new
            {
                method = "sendMessage",
                chat_id = message.From.Id,
                text = "⚠️ Je n'ai pas pu identifier le montant ou le revenu. Exemple : *'CAF 437€'*",
                parse_mode = "Markdown"
            };
            return SendJsonResponse(failPayload);
        }

        var income = parsedIncome.Value;
        var incomeResult = await budgetRepository.CreateIncome(income);
        if (incomeResult.IsFailure)
            throw new Exception(incomeResult.Error);

        income.PageUrl = incomeResult.Value.url;

        var messageIncomeResult = await budgetNotifier.NotifyBudgetUsersFromNewIncome(message.From.Id, income);
        if (!messageIncomeResult.Result.IsSuccess)
            context.Logger.LogWarning(messageIncomeResult.Result.Error);

        return SendJsonResponse(messageIncomeResult.Reply);
    }

    private static async Task<APIGatewayHttpApiV2ProxyResponse> HandleNewExpense(ILambdaContext context, TelegramMessage message, GenAiBudgetService parser, IBudgetRepository budgetRepository, IBudgetNotifier budgetNotifier)
    {
        var recurringDebits = await budgetRepository.FetchAllRecurringDebits();
        if (recurringDebits.IsFailure)
            throw new Exception(recurringDebits.Error);

        var parsedExpense = await parser.ParseExpenseAsync(message.Text, recurringDebits.Value);

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

        var expense = parsedExpense.Value;

        context.Logger.LogInformation(
        "Creating Notion expense: Amount={Amount}, Description={Description}, Category={Category}, RecurringDebitId={RecurringDebitId}, RecurringDebitName={RecurringDebitName}, IsTransfer={IsTransfer}",
        expense.Amount,
        expense.Description,
        expense.Category,
        expense.RecurringDebitId,
        expense.RecurringDebitName,
        expense.IsTransfer
    );

        var budgetLeftResult = await budgetRepository.GetBudgetInformation(expense.RecurringDebitId);
        if (!budgetLeftResult.IsSuccess)
            throw new Exception("Impossible to fetch budget");

        // Override transfer when false and recurring debit associated is made by transfer
        expense.IsTransfer = expense.IsTransfer == false ? budgetLeftResult.Value.IsTransfer : expense.IsTransfer;

        var result = await budgetRepository.CreateExpense(expense);
        if (!result.IsSuccess)
            throw new Exception("Impossible to create expense: " + result.Error);

        expense.PageUrl = result.Value.url;

        var messageResult = await budgetNotifier.NotifyBudgetUsersFromNewExpense(message.From.Id, expense, budgetLeftResult.Value);
        if (!messageResult.Result.IsSuccess)
            context.Logger.LogWarning(messageResult.Result.Error);

        return SendJsonResponse(messageResult.Reply);
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
