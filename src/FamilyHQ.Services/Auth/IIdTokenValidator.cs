namespace FamilyHQ.Services.Auth;

public interface IIdTokenValidator
{
    Task<IdTokenClaims> ValidateAsync(string idToken, CancellationToken ct = default);
}
