namespace Skill;

internal class CurrentSession
{
    private Dictionary<string, object?> Session { get; set; }

    public CurrentSession(Dictionary<string, object?> session) {
        Session = session;
    }

    public string FirstName { 
        get 
        {
            return Session["FirstName"]?.ToString() ?? string.Empty;
        } 
        set 
        {
            Session["FirstName"] = char.ToUpper(value[0]) + value?.Substring(1).ToLower();
        }
    }
}
