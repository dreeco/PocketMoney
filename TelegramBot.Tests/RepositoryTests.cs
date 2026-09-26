using Domain.PocketMoneyEntities;
using Domain.Repositories;
using Xunit;

namespace TelegramBot.Tests;

public class RepositoryTests
{
    [Fact]
    public void Test() 
    {
        var lastEntry = DateTime.Today.AddDays(-42);
        var r = Infrastructure.DataAccess.PocketMoneyRepository.GetEachMemberDateMissing(lastEntry, [new Balance("Elio", "BalanceDeElio", 1, 1), new Balance("Alix", "BalanceD'Alix", 1, 1), new Balance("Lucie", "BalanceDeLucie", 1, 1)]);

        Assert.Equal(42 * 3, r.Count());
        Assert.Equal(42, r.Count(re => re.BalanceId == "BalanceDeElio"));
        Assert.Equal(42, r.Count(re => re.BalanceId == "BalanceD'Alix"));
        Assert.Equal(42, r.Count(re => re.BalanceId == "BalanceDeLucie"));

        Assert.Equal(42, r.Count(re => re.Kid == "Elio"));
        Assert.Equal(42, r.Count(re => re.Kid == "Alix"));
        Assert.Equal(42, r.Count(re => re.Kid == "Lucie"));

        Assert.Equal(lastEntry.AddDays(1), r.Min(re => re.Date));
        Assert.Equal(DateTime.Today, r.Max(re => re.Date));
    }
}
