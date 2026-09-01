using CSharpFunctionalExtensions;
using Domain.BudgetEntities;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBot;

public record Button(string Text, string Url);

public interface IBudgetNotifier
{
    Task<(Result Result, object Reply)> NotifyBudgetUsersFromNewExpense(long from, Expense expense, BudgetInformation budgetLeftResult);
    Task<(Result Result, object Reply)> NotifyBudgetUsersFromNewIncome(long from, Expense expense);
    Task<object> BuildSituationSummaryAnswer(long from, Situation situation);
}

public class BudgetNotifier : IBudgetNotifier
{
    private ITelegramBotClient Bot { get; }
    private readonly List<long> _allowedUserIds;

    public BudgetNotifier(ITelegramBotClient boClient)
    {
        Bot = boClient;

        var envIds = Environment.GetEnvironmentVariable("ALLOWED_USER_IDS") ?? "8818144478,8662514156";
        _allowedUserIds = envIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(long.Parse)
                                .ToList();
    }

    public async Task<(Result Result, object Reply)> NotifyBudgetUsersFromNewExpense(long from, Expense expense, BudgetInformation budgetLeftResult)
    {
        var otherUser = _allowedUserIds.FirstOrDefault(i => i != from);

        var mean = expense.IsTransfer ? "virement" : "CB";

        var text = $@"
💵 Dépense ""{expense.Description}"" par {mean} enregistrée !
• **Montant :** {expense.Amount:C}
• **Catégorie :** {expense.Category}
• **Dépense récurrente: ** {expense.RecurringDebitName}

{budgetLeftResult.CurrentMonthInfo}
";

        var button = new Button("🔗 Voir la dépense", expense.PageUrl);

        var message = await Bot.SendMessage(
            new ChatId(otherUser),
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton(button.Text, button.Url)),
            text: text
        );

        var reply = new
        {
            method = "sendMessage",
            chat_id = from,
            text = text,
            parse_mode = "Markdown",
            reply_markup = new
            {
                inline_keyboard = new[] { new
                {
                    text = button.Text,
                    url = button.Url
                } }
            }
        };

        if (message == null)
            return (Result.Failure("Impossible to send message to second recipient"), reply);

        return (Result.Success(), reply);
    }


    public async Task<(Result Result, object Reply)> NotifyBudgetUsersFromNewIncome(long from, Expense expense)
    {
        var otherUser = _allowedUserIds.FirstOrDefault(i => i != from);

        var mean = expense.IsTransfer ? "virement" : "CB";

        var text = $@"
🤑 Revenu ""{expense.Description}"" par {mean} enregistré !
• **Montant :** {expense.Amount:C}
• **Catégorie :** {expense.Category}
• **Revenu récurrent: ** {expense.RecurringDebitName}
";

        var button = new Button("🔗 Voir le revenu", expense.PageUrl);

        var message = await Bot.SendMessage(
            new ChatId(otherUser),
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton(button.Text, button.Url)),
            text: text
        );

        var reply = new
        {
            method = "sendMessage",
            chat_id = from,
            text = text,
            parse_mode = "Markdown",
            reply_markup = new
            {
                inline_keyboard = new[] { new
                {

                    text = button.Text,
                    url = button.Url
                } }
            }
        };

        if (message == null)
            return (Result.Failure("Impossible to send message to second recipient"), reply);

        return (Result.Success(), reply);
    }


    public async Task<object> BuildSituationSummaryAnswer(long from, Situation situation)
    {
        var reply = new
        {
            method = "sendMessage",
            chat_id = from,
            text = situation.Summary,
            parse_mode = "Markdown",
        };

        return reply;
    }
}
