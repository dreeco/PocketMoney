namespace Domain.PocketMoneyEntities;

public record User(string Name, long Id)
{
    public static User New(string key) 
    {
        var split = key.Split(':');
        return new User(split[0], long.Parse(split[1]));
    }
}
