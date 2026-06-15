namespace FamilyHQ.Services.Auth;

public class IdTokenValidationException : Exception
{
    public IdTokenValidationException(string reason) : base(reason) { }
}
