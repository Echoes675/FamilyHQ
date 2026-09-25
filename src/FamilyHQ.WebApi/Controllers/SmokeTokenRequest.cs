namespace FamilyHQ.WebApi.Controllers;

/// <summary>
/// Body of both preprod smoke token endpoints (FHQ-139). <see cref="UserId"/> is the Google subject
/// identifier stored as <c>UserToken.UserId</c>, and the <c>PreprodSmokeAccess</c> policy allows only
/// the configured smoke account's value through.
/// </summary>
public record SmokeTokenRequest(string UserId);
