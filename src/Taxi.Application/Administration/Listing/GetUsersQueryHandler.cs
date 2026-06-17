using Taxi.Application.Administration;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Listing;

/// <summary>
/// Gère <see cref="GetUsersQuery"/> : délègue la récupération des comptes à <see cref="IUserDirectory"/>
/// et retourne les résumés d'utilisateurs.
/// </summary>
internal sealed class GetUsersQueryHandler(IUserDirectory users)
    : IQueryHandler<GetUsersQuery, IReadOnlyList<UserSummary>>
{
    public async Task<Result<IReadOnlyList<UserSummary>>> Handle(GetUsersQuery query, CancellationToken cancellationToken)
    {
        var list = await users.ListAsync(cancellationToken);
        return Result.Success(list);
    }
}
