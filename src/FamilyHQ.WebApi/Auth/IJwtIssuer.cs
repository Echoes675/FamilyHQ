namespace FamilyHQ.WebApi.Auth;

/// <summary>
/// Mints application JWTs for authenticated users.
/// </summary>
public interface IJwtIssuer
{
    /// <summary>
    /// Issues a JWT for the given user.
    /// </summary>
    /// <param name="userId">The Google subject identifier stored as <c>UserToken.UserId</c>.</param>
    /// <param name="email">Optional email address. When provided it is added as the <c>name</c> claim.</param>
    /// <returns>The signed JWT and its expiry instant.</returns>
    (string Token, DateTimeOffset ExpiresAt) Issue(string userId, string? email = null);
}
