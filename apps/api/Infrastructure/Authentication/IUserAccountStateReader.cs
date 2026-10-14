namespace Yanyun.Api.Infrastructure.Authentication;

public sealed record UserAccountState(bool IsActive);

public interface IUserAccountStateReader
{
    Task<UserAccountState?> FindAsync(Guid userId, CancellationToken cancellationToken);
}
