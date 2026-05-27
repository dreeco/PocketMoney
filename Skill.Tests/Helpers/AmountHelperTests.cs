using Skill.Helpers;
using Xunit;

namespace Skill.Tests.Helpers;

public class AmountHelperTests
{
    [Theory]
    [InlineData(0, "Ton solde est à zéro")]
    [InlineData(1, "Papa te doit 1 centime")]
    [InlineData(2, "Papa te doit 2 centimes")]
    [InlineData(102, "Papa te doit 1 euro et 2 centimes")]
    [InlineData(202, "Papa te doit 2 euros et 2 centimes")]
    [InlineData(101, "Papa te doit 1 euro et 1 centime")]
    [InlineData(201, "Papa te doit 2 euros et 1 centime")]
    [InlineData(-1, "Tu dois 1 centime à papa")]
    [InlineData(-2, "Tu dois 2 centimes à papa")]
    [InlineData(-102, "Tu dois 1 euro et 2 centimes à papa")]
    [InlineData(-202, "Tu dois 2 euros et 2 centimes à papa")]
    [InlineData(-101, "Tu dois 1 euro et 1 centime à papa")]
    [InlineData(-201, "Tu dois 2 euros et 1 centime à papa")]
    public void AmountTextShouldMatchExpected(int amountInCents, string expectedText) 
    { 
        var text = AmountHelper.GetAmountToPromptText(amountInCents);
        Assert.Equal(expectedText, text);
    }
    [Theory]
    [InlineData(0, "Tu n'as pas de tâches en attente de validation")]
    [InlineData(-1, "Tu n'as pas de tâches en attente de validation")]
    [InlineData(2, "Tu as 2 centimes en attente de validation")]
    [InlineData(102, "Tu as 1 euro et 2 centimes en attente de validation")]
    [InlineData(202, "Tu as 2 euros et 2 centimes en attente de validation")]
    [InlineData(101, "Tu as 1 euro et 1 centime en attente de validation")]
    [InlineData(201, "Tu as 2 euros et 1 centime en attente de validation")]
    public void WaitingAmountTextShouldMatchExpected(int amountInCents, string expectedText)
    {
        var text = AmountHelper.GetWaitingAmountToPromptText(amountInCents);
        Assert.Equal(expectedText, text);
    }

}
