using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FamilyHQ.Core.Interfaces;
using Microsoft.AspNetCore.Http;

namespace FamilyHQ.WebApi.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? UserId =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? BackgroundUserContext.Current;
}
