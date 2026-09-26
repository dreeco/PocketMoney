using CSharpFunctionalExtensions;
using Domain.PocketMoneyEntities;

namespace Domain.Repositories;

public interface IPocketMoneyRepository
{
    Task<Result<IEnumerable<PocketMoneyCalendarItem>>> SynchronizeCalendar(CancellationToken cancellationToken);
}
