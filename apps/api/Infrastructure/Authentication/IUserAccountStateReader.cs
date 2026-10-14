namespace Yanyun.Api.Infrastructure.Authentication;

public sealed record UserAccountState(bool IsActive, bool HasVerifiedEmail);

public interface IUserAccountStateReader
{
    Task<UserAccountState?> FindAsync(Guid userId, CancellationToken cancellationToken);
}
