using CSharpFunctionalExtensions;
using Domain.BudgetEntities;
using Domain.PocketMoneyEntities;
using Domain.Services;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBot;

public class BudgetNotifier : IBudgetNotifier
{
    private ITelegramBotClient Bot { get; }
    private readonly List<User> _allowedUserIds;

    public BudgetNotifier(ITelegramBotClient boClient)
    {
        Bot = boClient;

        _allowedUserIds = UserHelper.GetAllowedUsers();
    }

    public async Task<Result> NotifyAllBudgetUsersFromNewMessage(UserRequestResponse response, CancellationToken cancellationToken)
    {
        return await NotifyUsersFromNewMessage(_allowedUserIds.Select(u => u.Id), response, cancellationToken);
    }


    public async Task<Result> SendMessageToUniqueUser(long userId, UserRequestResponse response, CancellationToken cancellationToken)
    {
        return await NotifyUsersFromNewMessage([userId], response, cancellationToken);
    }

    private async Task<Result> NotifyUsersFromNewMessage(IEnumerable<long> userIds, UserRequestResponse response, CancellationToken cancellationToken)
    {
        var inlinedButton = response.Buttons != null
            ? new InlineKeyboardMarkup(response.Buttons.Select(button => InlineKeyboardButton.WithUrl(button.Text, button.Url)))
            : null;

        var sendTasks = userIds.Select(userId =>
            Bot.SendMessage(
                chatId: userId,
                text: response.Answer,
                parseMode: response.UseHtml ? ParseMode.Html : ParseMode.Markdown,
                replyMarkup: inlinedButton,
                cancellationToken: cancellationToken
            )
        );

        await Task.WhenAll(sendTasks);

        return Result.Success();
    }
}
