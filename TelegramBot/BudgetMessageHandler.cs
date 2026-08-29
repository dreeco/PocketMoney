using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBot
{
    public record Button(string Text, string Url);
    
    public interface IBudgetNotifier {
        Task<Result> SendMessage(long from, string text, Button button);
    }

    public class BudgetNotifier: IBudgetNotifier
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

        public async Task<Result> SendMessage(long from, string text, Button button)
        {
            var message = await Bot.SendMessage(
                new ChatId(_allowedUserIds.Where(i => i != from).FirstOrDefault()),
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton(button.Text, button.Url)),
                text: text
            );

            if (message == null)
                return Result.Failure("Impossible to send message to second recipient");

            return Result.Success();
        }
    }
}
