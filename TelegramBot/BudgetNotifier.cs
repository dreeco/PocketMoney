using CSharpFunctionalExtensions;
using Domain.BudgetEntities;
using Domain.Services;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBot;

public class BudgetNotifier : IBudgetNotifier
{
    private ITelegramBotClient Bot { get; }
    private readonly List<long> _allowedUserIds;

    public BudgetNotifier(ITelegramBotClient boClient)
    {
        Bot = boClient;

        var envIds = Environment.GetEnvironmentVariable("ALLOWED_USER_IDS") ?? throw new Exception("Could not find allowed user ids");
        _allowedUserIds = envIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(long.Parse)
                                .ToList();
    }

    public async Task<Result> NotifyAllBudgetUsersFromNewMessage(UserRequestResponse response, CancellationToken cancellationToken)
    {
        return await NotifyUsersFromNewMessage(_allowedUserIds, response, cancellationToken);
    }


    public async Task<Result> SendMessageToUniqueUser(long userId, UserRequestResponse response, CancellationToken cancellationToken)
    {
        return await NotifyUsersFromNewMessage([userId], response, cancellationToken);
    }

    private async Task<Result> NotifyUsersFromNewMessage(IEnumerable<long> userIds, UserRequestResponse response, CancellationToken cancellationToken)
    {
        var inlinedButton = response.Button != null
            ? new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl(response.Button.Text, response.Button.Url))
            : null;

        var sendTasks = userIds.Select(userId =>
            Bot.SendMessage(
                chatId: userId,
                text: response.Answer,
                parseMode: ParseMode.Markdown,
                replyMarkup: inlinedButton,
                cancellationToken: cancellationToken
            )
        );

        await Task.WhenAll(sendTasks);

        return Result.Success();
    }
}
