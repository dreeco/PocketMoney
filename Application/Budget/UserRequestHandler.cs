using CSharpFunctionalExtensions;
using Domain.BudgetEntities;
using Domain.PocketMoneyEntities;
using Domain.Repositories;
using Domain.Services;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Application.Budget;

public record UserMessage(string Text, User User);
public interface IUserRequestHandler
{
    Task<Result> ParseMessage(UserMessage message, CancellationToken cancellationToken);
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

    public async Task<Result> ParseMessage(UserMessage userMessage, CancellationToken cancellationToken)
    {
        //if (userMessage.Text.Equals("Situation", StringComparison.InvariantCultureIgnoreCase))
        //{
        //    var responseSummary = await HandleSituationSummary2(userMessage.Text, cancellationToken);
        //    if (responseSummary.IsFailure)
        //        return Result.Failure(responseSummary.Error);

        //    var result = await _budgetNotifier.SendMessageToUniqueUser(userMessage.User.Id, responseSummary.Value, cancellationToken);
        //    if (result.IsFailure)
        //        _logger.LogError(result.Error);

        //    return Result.Success();
        //}

        var actionResult = await _genAiBudgetService.ParseRouteFromMessage(userMessage.Text, cancellationToken);

        var userId = userMessage.User.Id;

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
                var responseSummary = await HandleSituationSummary(userMessage.Text, cancellationToken);
                if (responseSummary.IsFailure)
                    return Result.Failure(responseSummary.Error);

                var result = await _budgetNotifier.SendMessageToUniqueUser(userId, responseSummary.Value, cancellationToken);
                if (result.IsFailure)
                    _logger.LogError(result.Error);

                return Result.Success();

            case "SynchroniserDépensesRécurrentes":
                var createdDebits = await HandleSyncRecurrentDebits(cancellationToken);

                return await NotifyAll(_logger, userId, createdDebits, cancellationToken);

            default:
                await _budgetNotifier.SendMessageToUniqueUser(userId, new UserRequestResponse($"⚠️ Je n'ai pas compris la demande."), cancellationToken);
                return Result.Success();
        }
    }

    private async Task<Result<UserRequestResponse>> HandleNewExpense(ILogger logger, UserMessage userMessage, CancellationToken cancellationToken)
    {
        var recurringDebits = await _repository.FetchAllRecurringDebits(cancellationToken);
        if (recurringDebits.IsFailure)
            return Result.Failure<UserRequestResponse>(recurringDebits.Error);

        var parsedExpense = await _genAiBudgetService.ParseExpenseAsync(userMessage.Text, recurringDebits.Value, cancellationToken);

        if (!parsedExpense.IsSuccess)
            return new UserRequestResponse("⚠️ Je n'ai pas pu identifier le montant ou la dépense. Exemple : *'Courses carrefour 35€'*");

        var expense = parsedExpense.Value;

        expense.CBHolder = userMessage.User.Name;

        logger
            .LogInformation("Creating Notion expense: Amount={Amount}, Description={Description}, Category={Category}, RecurringDebitId={RecurringDebitId}, RecurringDebitName={RecurringDebitName}, IsTransfer={IsTransfer}",
            expense.Amount, expense.Description, expense.Category, expense.RecurringDebitId, expense.RecurringDebitName, expense.IsTransfer);


        //// Override transfer when false and recurring debit associated is made by transfer
        //expense.IsTransfer = expense.IsTransfer == false ? budgetLeftResult.Value.IsTransfer : expense.IsTransfer;

        var result = await _repository.CreateExpenses([expense], cancellationToken);
        if (!result.IsSuccess || result.Value.FirstOrDefault() == null)
            return Result.Failure<UserRequestResponse>("Impossible to create expense: " + result.Error);

        var budgetLeftResult = await _repository.GetBudgetInformation(expense.RecurringDebitId, cancellationToken);
        if (!budgetLeftResult.IsSuccess)
            return Result.Failure<UserRequestResponse>("Impossible to fetch budget");

        expense.PageUrl = result.Value.First().url;

        var mean = expense.IsTransfer ? "virement" : "CB";

        var text = $@"
💵 Dépense ""{expense.Description}"" par {mean} enregistrée !
• **Montant :** {expense.Amount:C}
• **Catégorie :** {expense.Category}
• **Dépense récurrente: ** {expense.RecurringDebitName}

{budgetLeftResult.Value.CurrentMonthInfo}
";

        var button = new Button("🔗 Voir la dépense", expense.PageUrl);

        return new UserRequestResponse(text, [button]);
    }


    private async Task<Result<UserRequestResponse>> HandleNewIncome(ILogger logger, UserMessage userMessage, CancellationToken cancellationToken)
    {
        var recurringCredits = await _repository.FetchAllRecurringCredits(cancellationToken);
        if (recurringCredits.IsFailure)
            return Result.Failure<UserRequestResponse>(recurringCredits.Error);

        var parsedIncome = await _genAiBudgetService.ParseIncomeAsync(userMessage.Text, recurringCredits.Value, cancellationToken);

        if (parsedIncome.IsFailure)
            return new UserRequestResponse("⚠️ Je n'ai pas pu identifier le montant ou le revenu. Exemple : *'CAF 437€'*");

        var income = parsedIncome.Value;
        income.CBHolder = userMessage.User.Name;
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

        return new UserRequestResponse(text, [button]);
    }

    //private async Task<Result<UserRequestResponse>> HandleSituationSummary(string userMessage, CancellationToken cancellationToken)
    //{
    //    // 1. Lancer les deux opérations en parallèle
    //    var recurringDebitsTask = _repository.FetchAllRecurringDebits(cancellationToken);
    //    var billingMonthsTask = _repository.FetchAllBillingMonths(cancellationToken);

    //    // 2. Attendre que les deux tâches soient terminées
    //    await Task.WhenAll(recurringDebitsTask, billingMonthsTask);

    //    // 3. Récupérer les résultats
    //    var recurringDebitsResult = await recurringDebitsTask;
    //    var billingMonthsResult = await billingMonthsTask;

    //    // 4. Valider les échecs
    //    if (recurringDebitsResult.IsFailure || billingMonthsResult.IsFailure)
    //        return Result.Failure<UserRequestResponse>(Result.Combine([recurringDebitsResult, billingMonthsResult]).Error);

    //    var situation = await _genAiBudgetService.EvaluateSituation(userMessage, recurringDebitsResult.Value, billingMonthsResult.Value, cancellationToken);
    //    if (situation.IsFailure)
    //        return Result.Failure<UserRequestResponse>(situation.Error);

    //    return new UserRequestResponse(situation.Value.Summary);
    //}


    private async Task<Result<UserRequestResponse>> HandleSituationSummary(string userMessage, CancellationToken cancellationToken)
    {
        var currentBillingMonthTask = _repository.GetCurrentBillingMonth(cancellationToken);
        var awaitingExpensesTask = _repository.GetDebits(awaiting: true, cancellationToken);
        var recurrentDebitsTask = _repository.GetCurrentMonthRecurrentDebits(cancellationToken);

        await Task.WhenAll([currentBillingMonthTask, awaitingExpensesTask, recurrentDebitsTask]);


        var currentBillingMonthResult = await currentBillingMonthTask;
        if (currentBillingMonthResult.IsFailure)
            return Result.Failure<UserRequestResponse>(currentBillingMonthResult.Error);

        var awaitingExpensesResult = await awaitingExpensesTask;
        if (awaitingExpensesResult.IsFailure)
            return Result.Failure<UserRequestResponse>(awaitingExpensesResult.Error);

        var recurrentDebitsResult = await recurrentDebitsTask;
        if (recurrentDebitsResult.IsFailure)
            return Result.Failure<UserRequestResponse>(recurrentDebitsResult.Error);


        var situation = currentBillingMonthResult.Value.Situation;

        var budgetsDépassés = recurrentDebitsResult.Value.Where(r => r.CurrentState.Contains("Dépassé", StringComparison.InvariantCultureIgnoreCase));
        var sommeBudgetDépassés = budgetsDépassés.Sum(b =>
        {
            var match = Regex.Match(b.CurrentState, @"\((\d+(?:[.,]\d+)?)\s*€\)");
            if (!match.Success)
                return 0;
            return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        });


        var TotalDepenseMois = situation.Spent + recurrentDebitsResult.Value.Where(r => r.CurrentState.Contains("En attente", StringComparison.InvariantCultureIgnoreCase)).Sum(d => d.Amount);

        //        string messageTelegram = $@"
        //📊 *Résumé Budget {currentBillingMonthResult.Value.Name}*

        //🏦 *État des comptes*
        //• Solde visible CIC : *{situation.BankVisible:F2} €*
        //• Encours Cartes : *{situation.BankTotalCards:F2} €*
        //  ├ 👱‍ Adrien : {situation.BankAdrienCard:F2} €
        //  └ 👩 Justine : {situation.BankJustineCard:F2} €

        //⏳ *Dépenses en attente & à venir*
        //• Dépenses pas visibles CIC : *{awaitingExpensesResult.Value.Sum(d => d.Amount):F2} €*
        //  ├ 👱‍ Adrien : {awaitingExpensesResult.Value.Where(d => d.CBHolder == "Adrien").Sum(d => d.Amount):F2} € {awaitingExpensesResult.Value.Count(d => d.CBHolder == "Adrien")} transactions
        //**>•{string.Join(@"
        //>•", awaitingExpensesResult.Value.Where(d => d.CBHolder == "Adrien").Select(d => $"{d.Description} - {d.Amount:F2} €"))}
        //  ├ 👩 Justine : {awaitingExpensesResult.Value.Where(d => d.CBHolder == "Justine").Sum(d => d.Amount):F2} € {awaitingExpensesResult.Value.Count(d => d.CBHolder == "Justine")} transactions
        //  └ Autres : {awaitingExpensesResult.Value.Where(d => d.CBHolder == string.Empty).Sum(d => d.Amount):F2} € {awaitingExpensesResult.Value.Count(d => d.CBHolder == string.Empty)} transactions

        //🎯 *État des Budgets Principaux*
        //• ⚠️ *Budgets dépassés :* total = {sommeBudgetDépassés:F2} € ({string.Join(", ", budgetsDépassés.Select(b => b.Name))})
        //├ {string.Join(@"
        //├ ", recurrentDebitsResult.Value.Where(r => r.CurrentState.Contains("Dépassé", StringComparison.InvariantCultureIgnoreCase) && r.Progressive).OrderBy(r => r.CurrentState).Select(r => r.Icon + " " + r.Name + " : " + r.CurrentState))}

        //• 🟢 *Autres budgets :*
        //├ {string.Join(@"
        //├ ", recurrentDebitsResult.Value.Where(r => !r.CurrentState.Contains("Dépassé", StringComparison.InvariantCultureIgnoreCase) && r.Progressive).OrderByDescending(r => r.CurrentState).Select(r => r.Icon + " " + r.Name + " : " + r.CurrentState))}

        //🔮 *Projection Fin de Mois*
        //• 💸 Dépensé global : {TotalDepenseMois:F2} € / {recurrentDebitsResult.Value.Sum(r => r.Amount):F2} € prévus
        //• 🏁 Estimation fin de mois : {situation.ExpectedEndOfMonth:F2} €
        //";

        string messageTelegram = $@"
📊 <b>Résumé Budget {currentBillingMonthResult.Value.Name}</b>

🏦 <b>État des comptes</b>
• Solde visible CIC : <b>{situation.BankVisible:F2} €</b>
• Encours Cartes : <b>{situation.BankTotalCards:F2} €</b>
  ├ 👱‍♂️ Adrien : {situation.BankAdrienCard:F2} €
  └ 👩 Justine : {situation.BankJustineCard:F2} €

⏳ <b>Dépenses en attente &amp; à venir</b>
• Dépenses pas visibles CIC : <b>{awaitingExpensesResult.Value.Sum(d => d.Amount):F2} €</b>
  ├ 👱‍♂️ Adrien : {awaitingExpensesResult.Value.Where(d => d.CBHolder == "Adrien").Sum(d => d.Amount):F2} € ({awaitingExpensesResult.Value.Count(d => d.CBHolder == "Adrien")} transactions)
<blockquote expandable>{string.Join("\n", awaitingExpensesResult.Value.Where(d => d.CBHolder == "Adrien").Select(d => $"• {d.Description} - {d.Amount:F2} €"))}</blockquote>
  ├ 👩 Justine : {awaitingExpensesResult.Value.Where(d => d.CBHolder == "Justine").Sum(d => d.Amount):F2} € ({awaitingExpensesResult.Value.Count(d => d.CBHolder == "Justine")} transactions)
<blockquote expandable>{string.Join("\n", awaitingExpensesResult.Value.Where(d => d.CBHolder == "Justine").Select(d => $"• {d.Description} - {d.Amount:F2} €"))}</blockquote>
  └ Autres : {awaitingExpensesResult.Value.Where(d => d.CBHolder == string.Empty).Sum(d => d.Amount):F2} € ({awaitingExpensesResult.Value.Count(d => d.CBHolder == string.Empty)} transactions)

🎯 <b>État des Budgets Principaux</b>
• ⚠️ <b>Budgets dépassés :</b> total = {sommeBudgetDépassés:F2} € ({string.Join(", ", budgetsDépassés.Select(b => b.Name))})
<blockquote expandable>{string.Join("\n├ ", recurrentDebitsResult.Value.Where(r => r.CurrentState.Contains("Dépassé", StringComparison.InvariantCultureIgnoreCase) && r.Progressive).OrderBy(r => r.CurrentState).Select(r => r.Icon + " " + r.Name + " : " + r.CurrentState))}</blockquote>

• 🟢 <b>Autres budgets :</b>
<blockquote expandable>{string.Join("\n├ ", recurrentDebitsResult.Value.Where(r => !r.CurrentState.Contains("Dépassé", StringComparison.InvariantCultureIgnoreCase) && r.Progressive).OrderByDescending(r => r.CurrentState).Select(r => r.Icon + " " + r.Name + " : " + r.CurrentState))}</blockquote>

🔮 <b>Projection Fin de Mois</b>
• 💸 Dépensé global : {TotalDepenseMois:F2} € / {recurrentDebitsResult.Value.Sum(r => r.Amount):F2} € prévus
• 🏁 Estimation fin de mois : <b>{situation.ExpectedEndOfMonth:F2} €</b>
";

        return new UserRequestResponse(messageTelegram, UseHtml: true);
    }

    private async Task<Result<UserRequestResponse>> HandleSyncRecurrentDebits(CancellationToken cancellationToken)
    {
        var unsyncedRecurrentDebitsResult = await _repository.GetRecurrentDebitsWithNoExpenseForCurrentMonth(cancellationToken);
        if (unsyncedRecurrentDebitsResult.IsFailure)
            return Result.Failure<UserRequestResponse>(unsyncedRecurrentDebitsResult.Error);
        
        var unsyncedRecurrentDebits = unsyncedRecurrentDebitsResult.Value;
        if (unsyncedRecurrentDebits.Count() == 0)
            return Result.Success(new UserRequestResponse("Aucun débit récurrent à synchroniser"));

        var expenses = unsyncedRecurrentDebits.Select(rd => new Expense() { 
            Amount = rd.Amount, 
            Category = rd.Category, 
            Description = rd.Name, 
            IsTransfer = rd.IsTransfer, 
            IsValidExpense = true, 
            RecurringDebitId = rd.Id, 
            RecurringDebitName = rd.Name 
        });
        var response = await _repository.CreateExpenses(expenses, cancellationToken);
        if (response.IsFailure)
            return Result.Failure<UserRequestResponse>(response.Error);

        var text = $@"{unsyncedRecurrentDebits.Count()} débits récurrents non enregistrés trouvés : 
•   {String.Join("\n•   ", unsyncedRecurrentDebits.Select(urd => $"{urd.Name} ({urd.Amount}€) par {(urd.IsTransfer ? "🏦virement" : "💳CB")}"))}
";
        return new UserRequestResponse(text, response.Value.Select(e => new Button(e.name, e.url)));
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
