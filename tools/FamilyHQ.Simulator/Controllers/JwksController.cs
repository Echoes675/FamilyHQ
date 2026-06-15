using FamilyHQ.Simulator.Auth;
using Microsoft.AspNetCore.Mvc;

namespace FamilyHQ.Simulator.Controllers;

[ApiController]
public class JwksController : ControllerBase
{
    [HttpGet("/.well-known/jwks.json")]
    public IActionResult GetJwks()
    {
        var rsaParams = SimulatorRsaKey.Instance.ExportParameters(false);
        return Ok(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = "RS256",
                    n = Base64UrlEncode(rsaParams.Modulus!),
                    e = Base64UrlEncode(rsaParams.Exponent!)
                }
            }
        });
    }

    private static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
