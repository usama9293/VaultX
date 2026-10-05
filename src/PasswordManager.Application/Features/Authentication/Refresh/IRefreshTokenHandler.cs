using PasswordManager.Application.DTOs.Authentication;

namespace PasswordManager.Application.Features.Authentication.Refresh;

public interface IRefreshTokenHandler
{
    Task<RefreshResult> HandleAsync(RefreshCommand command, CancellationToken cancellationToken = default);
}
