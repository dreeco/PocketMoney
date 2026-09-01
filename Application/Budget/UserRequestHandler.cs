using CSharpFunctionalExtensions;
using Domain.BudgetEntities;
using Domain.Repositories;
using Domain.Services;
using Microsoft.Extensions.Logging;

namespace Application.Budget;

public interface IUserRequestHandler
{
    Task<Result<UserRequestResponse>> HandleNewExpense(ILogger logger, string userMessage, CancellationToken cancellationToken);
    Task<Result<UserRequestResponse>> HandleNewIncome(ILogger logger, string userMessage, CancellationToken cancellationToken);
}

public class UserRequestHandler : IUserRequestHandler
{
    private readonly IBudgetRepository _repository;
    private readonly IGenAiBudgetService _genAiBudgetService;

    public UserRequestHandler(IBudgetRepository repository, IGenAiBudgetService genAiBudgetService)
    {
        _repository = repository;
        _genAiBudgetService = genAiBudgetService;
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

        var budgetLeftResult = await _repository.GetBudgetInformation(expense.RecurringDebitId, cancellationToken);
        if (!budgetLeftResult.IsSuccess)
            throw new Exception("Impossible to fetch budget");

        // Override transfer when false and recurring debit associated is made by transfer
        expense.IsTransfer = expense.IsTransfer == false ? budgetLeftResult.Value.IsTransfer : expense.IsTransfer;

        var result = await _repository.CreateExpense(expense, cancellationToken);
        if (!result.IsSuccess)
            return Result.Failure<UserRequestResponse>("Impossible to create expense: " + result.Error);

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
}
