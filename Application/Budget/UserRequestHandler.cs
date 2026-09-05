using CSharpFunctionalExtensions;
using Domain.BudgetEntities;
using Domain.Repositories;
using Domain.Services;
using Microsoft.Extensions.Logging;

namespace Application.Budget;

public interface IUserRequestHandler
{
    Task<Result> ParseMessage(string userMessage, long userId, CancellationToken cancellationToken);
}

public class UserRequestHandler : IUserRequestHandler
{
    private readonly ILogger<UserRequestHandler> _logger;
    private readonly IBudgetRepository _repository;
    private readonly IGenAiBudgetService _genAiBudgetService;
    private readonly IBudgetNotifier _budgetNotifier;

    public UserRequestHandler(ILogger<UserRequestHandler> logger, IBudgetRepository repository, IGenAiBudgetService genAiBudgetService, IBudgetNotifier budgetNotifier)
    {
        _logger = logger;
        _repository = repository;
        _genAiBudgetService = genAiBudgetService;
        _budgetNotifier = budgetNotifier;
    }

    public async Task<Result<UserRequestResponse>> HandleNewExpense(ILogger logger, string userMessage, CancellationToken cancellationToken)
    {
        var recurringDebits = await _repository.FetchAllRecurringDebits(cancellationToken);
        if (recurringDebits.IsFailure)
            return Result.Failure<UserRequestResponse>(recurringDebits.Error);

        var parsedExpense = await _genAiBudgetService.ParseExpenseAsync(userMessage, recurringDebits.Value, cancellationToken);

        if (!parsedExpense.IsSuccess)
            return new UserRequestResponse("⚠️ Je n'ai pas pu identifier le montant ou la dépense. Exemple : *'Courses carrefour 35€'*");

        var expense = parsedExpense.Value;

        logger
            .LogInformation("Creating Notion expense: Amount={Amount}, Description={Description}, Category={Category}, RecurringDebitId={RecurringDebitId}, RecurringDebitName={RecurringDebitName}, IsTransfer={IsTransfer}",
            expense.Amount, expense.Description, expense.Category, expense.RecurringDebitId, expense.RecurringDebitName, expense.IsTransfer);


        //// Override transfer when false and recurring debit associated is made by transfer
        //expense.IsTransfer = expense.IsTransfer == false ? budgetLeftResult.Value.IsTransfer : expense.IsTransfer;

        var result = await _repository.CreateExpense(expense, cancellationToken);
        if (!result.IsSuccess)
            return Result.Failure<UserRequestResponse>("Impossible to create expense: " + result.Error);

        var budgetLeftResult = await _repository.GetBudgetInformation(expense.RecurringDebitId, cancellationToken);
        if (!budgetLeftResult.IsSuccess)
            return Result.Failure<UserRequestResponse>("Impossible to fetch budget");

        expense.PageUrl = result.Value.url;

        var mean = expense.IsTransfer ? "virement" : "CB";

        var text = $@"
💵 Dépense ""{expense.Description}"" par {mean} enregistrée !
• **Montant :** {expense.Amount:C}
• **Catégorie :** {expense.Category}
• **Dépense récurrente: ** {expense.RecurringDebitName}

{budgetLeftResult.Value.CurrentMonthInfo}
";

        var button = new Button("🔗 Voir la dépense", expense.PageUrl);

        return new UserRequestResponse(text, button);
    }


    public async Task<Result<UserRequestResponse>> HandleNewIncome(ILogger logger, string userMessage, CancellationToken cancellationToken)
    {
        var recurringCredits = await _repository.FetchAllRecurringCredits(cancellationToken);
        if (recurringCredits.IsFailure)
            return Result.Failure<UserRequestResponse>(recurringCredits.Error);

        var parsedIncome = await _genAiBudgetService.ParseIncomeAsync(userMessage, recurringCredits.Value, cancellationToken);

        if (parsedIncome.IsFailure)
            return new UserRequestResponse("⚠️ Je n'ai pas pu identifier le montant ou le revenu. Exemple : *'CAF 437€'*");

        var income = parsedIncome.Value;
        var incomeResult = await _repository.CreateIncome(income, cancellationToken);
        if (incomeResult.IsFailure)
            return Result.Failure<UserRequestResponse>(incomeResult.Error);

        income.PageUrl = incomeResult.Value.url;

        var mean = income.IsTransfer ? "virement" : "CB";

        var text = $@"
🤑 Revenu ""{income.Description}"" par {mean} enregistré !
• **Montant :** {income.Amount:C}
• **Catégorie :** {income.Category}
• **Revenu récurrent: ** {income.RecurringDebitName}
";

        var button = new Button("🔗 Voir le revenu", income.PageUrl);

        return new UserRequestResponse(text, button);
    }

    public async Task<Result<UserRequestResponse>> HandleSituationSummary(string userMessage, CancellationToken cancellationToken)
    {
        // 1. Lancer les deux opérations en parallèle
        var recurringDebitsTask = _repository.FetchAllRecurringDebits(cancellationToken);
        var billingMonthsTask = _repository.FetchAllBillingMonths(cancellationToken);

        // 2. Attendre que les deux tâches soient terminées
        await Task.WhenAll(recurringDebitsTask, billingMonthsTask);

        // 3. Récupérer les résultats
        var recurringDebitsResult = await recurringDebitsTask;
        var billingMonthsResult = await billingMonthsTask;

        // 4. Valider les échecs
        if (recurringDebitsResult.IsFailure || billingMonthsResult.IsFailure)
            return Result.Failure<UserRequestResponse>(Result.Combine([recurringDebitsResult, billingMonthsResult]).Error);

        var situation = await _genAiBudgetService.EvaluateSituation(userMessage, recurringDebitsResult.Value, billingMonthsResult.Value, cancellationToken);
        if (situation.IsFailure)
            return Result.Failure<UserRequestResponse>(situation.Error);

        return new UserRequestResponse(situation.Value.Summary);
    }

    public async Task<Result> ParseMessage(string userMessage, long userId, CancellationToken cancellationToken)
    {
        var actionResult = await _genAiBudgetService.ParseRouteFromMessage(userMessage, cancellationToken);
        if (actionResult.IsFailure)
            return Result.Failure(actionResult.Error);

        switch (actionResult.Value.Action)
        {
            case "SaisieDépense":
                var userRequestResponse = await HandleNewExpense(_logger, userMessage, cancellationToken);
                if (userRequestResponse.IsFailure)
                    return Result.Failure(userRequestResponse.Error);

                return await NotifyAll(_logger, userId, userRequestResponse, cancellationToken);
            
            case "SaisieRevenu":
                var userRequestResponseIncome = await HandleNewIncome(_logger, userMessage, cancellationToken);
                if (userRequestResponseIncome.IsFailure)
                    return Result.Failure(userRequestResponseIncome.Error);
                
                return await NotifyAll(_logger, userId, userRequestResponseIncome, cancellationToken);
            
            case "RésuméSituation":
                var responseSummary = await HandleSituationSummary(userMessage, cancellationToken);
                if (responseSummary.IsFailure)
                    return Result.Failure(responseSummary.Error);

                var result = await _budgetNotifier.SendMessageToUniqueUser(userId, responseSummary.Value, cancellationToken);
                if (result.IsFailure)
                    _logger.LogError(result.Error);

                return Result.Success();

            default:
                await _budgetNotifier.SendMessageToUniqueUser(userId, new UserRequestResponse($"⚠️ Je n'ai pas compris la demande."), cancellationToken);
                return Result.Success();
        }
    }

    private async Task<Result> NotifyAll(ILogger logger, long userId, Result<UserRequestResponse> userRequestResponseIncome, CancellationToken cancellationToken)
    {
        var messageResultIncome = await _budgetNotifier.NotifyAllBudgetUsersFromNewMessage(userRequestResponseIncome.Value, cancellationToken);

        if (messageResultIncome.IsFailure)
        {
            logger.LogWarning(messageResultIncome.Error);
            await _budgetNotifier.SendMessageToUniqueUser(userId, new UserRequestResponse("⚠️ La transaction a été enregistrée sur Notion mais la notification n'a pas pu être envoyée"), cancellationToken);
        }

        return Result.Success();
    }
}
