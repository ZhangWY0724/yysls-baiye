using Microsoft.EntityFrameworkCore;
using Yanyun.Api.Infrastructure.Persistence;

namespace Yanyun.Api.Infrastructure.Authentication;

public sealed class UserAccountStateReader(YanyunDbContext dbContext) : IUserAccountStateReader
{
    public Task<UserAccountState?> FindAsync(Guid userId, CancellationToken cancellationToken)
        => dbContext.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserAccountState(user.Status == "active"))
            .SingleOrDefaultAsync(cancellationToken);
}
