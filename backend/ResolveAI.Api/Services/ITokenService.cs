using ResolveAI.Api.Entities;

namespace ResolveAI.Api.Services;

public interface ITokenService
{
    string CreateToken(AppUser user);
}