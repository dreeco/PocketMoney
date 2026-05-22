namespace Application;

/// <summary>
/// Get current balance for a member
/// </summary>
/// <param name="memberName">Name of the member concerned</param>
/// <param name="amount">Amount in cents</param>
/// <param name="pendingAmount">Pending amount in cents</param>
public record MemberBalanceResponse(string memberName, int amount, int pendingAmount);
