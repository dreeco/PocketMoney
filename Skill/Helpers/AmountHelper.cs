using Application;

namespace Skill.Helpers;

public static class AmountHelper
{
    public static string GetAmountToPromptText(int amountInCents)
    {
        if (amountInCents == 0)
            return "Ton solde est à zéro";

        string amount = GetAmountToText(amountInCents);

        return amountInCents < 0 ? $"Tu dois {amount} à papa" : $"Papa te doit {amount}";
    }

    public static string GetWaitingAmountToPromptText(int amountInCents)
    {
        if (amountInCents <= 0)
            return "Tu n'as pas de tâches en attente de validation";
        
        string amount = GetAmountToText(amountInCents);

        return $"Tu as {amount} en attente de validation";
    }

    private static string GetAmountToText(int amountInCents)
    {
        var amountAbs = Math.Abs(amountInCents);

        var euro = amountAbs > 100 ? Math.Floor((double)amountAbs / 100) : 0;
        var cents = amountAbs > 100 ? amountAbs % 100 : amountAbs;

        var eurPlural = euro > 1 ? "s" : string.Empty;
        var centsPlural = cents > 1 ? "s" : string.Empty;

        var amountEur = $"{(euro > 0 ? euro + $" euro{eurPlural}" : string.Empty)}";
        var amountCents = $"{(cents > 0 ? cents + $" centime{centsPlural}" : string.Empty)}";

        var amount = $"{(!string.IsNullOrWhiteSpace(amountEur) ? amountEur : string.Empty)}{(!string.IsNullOrWhiteSpace(amountEur) && !string.IsNullOrWhiteSpace(amountCents) ? " et " : string.Empty)}{(!string.IsNullOrWhiteSpace(amountCents) ? amountCents : string.Empty)}";
        return amount;
    }
}
