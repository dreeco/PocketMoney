using Domain.PocketMoneyEntities;

namespace TelegramBot;

public static class UserHelper
{
    public static List<User> GetAllowedUsers()
    { 
        var envIds = Environment.GetEnvironmentVariable("UserIds") ?? throw new Exception("Could not find allowed user ids");
        return envIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(User.New)
                                .ToList();
    }
}
