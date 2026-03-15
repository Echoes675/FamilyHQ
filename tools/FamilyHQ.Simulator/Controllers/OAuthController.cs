using FamilyHQ.Simulator.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Simulator.Controllers;

[ApiController]
public class OAuthController : ControllerBase
{
    private readonly SimContext _db;

    public OAuthController(SimContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Renders an HTML consent screen listing all SimulatedUsers.
    /// Mimics the Google OAuth2 authorization endpoint.
    /// </summary>
    [HttpGet("/oauth2/auth")]
    public async Task<IActionResult> AuthPrompt([FromQuery] string redirect_uri, [FromQuery] string client_id)
    {
        var users = await _db.Users.OrderBy(u => u.Username).ToListAsync();
        var options = string.Join("", users.Select(u => $"<option value=\"{u.Id}\">{u.Username}</option>"));
        var html = $"""
            <!DOCTYPE html>
            <html>
            <head><title>FamilyHQ Simulator — Sign In</title></head>
            <body style="font-family:sans-serif;max-width:400px;margin:80px auto;padding:20px">
                <h2>FamilyHQ Simulator</h2>
                <p>Choose an account to continue</p>
                <form method="post" action="/oauth2/auth/consent">
                    <input type="hidden" name="redirect_uri" value="{redirect_uri}" />
                    <div style="margin-bottom:16px">
                        <label for="selectedUserId">Account:</label><br/>
                        <select id="selectedUserId" name="selectedUserId" style="width:100%;padding:8px;margin-top:4px">{options}</select>
                    </div>
                    <button type="submit" style="padding:8px 24px">Continue</button>
                </form>
            </body>
            </html>
            """;
        return Content(html, "text/html");
    }

    /// <summary>
    /// Handles the consent form submission and redirects back to the WebApi callback with an auth code.
    /// </summary>
    [HttpPost("/oauth2/auth/consent")]
    public async Task<IActionResult> Consent([FromForm] string selectedUserId, [FromForm] string redirect_uri)
    {
        var user = await _db.Users.FindAsync(selectedUserId);
        if (user == null)
            return BadRequest("Unknown user.");

        return Redirect($"{redirect_uri}?code=dummy_code_for_{selectedUserId}");
    }

    /// <summary>
    /// Token exchange endpoint. Access token embeds userId so downstream controllers
    /// can extract it without a DB lookup.
    /// Token format: simulated_{userId}_{nonce}
    /// Refresh token format: simulated_refresh_{userId}
    /// </summary>
    [HttpPost("/token")]
    public async Task<IActionResult> Token()
    {
        var form = await Request.ReadFormAsync();
        var grantType = form["grant_type"].ToString();
        var code = form["code"].ToString();
        var refreshToken = form["refresh_token"].ToString();

        const string refreshTokenPrefix = "simulated_refresh_user_";
        string userId;
        if (grantType == "refresh_token" && refreshToken.StartsWith(refreshTokenPrefix))
        {
            userId = refreshToken[refreshTokenPrefix.Length..];
        }
        else
        {
            userId = "default_simulator_user";
            if (code.StartsWith("dummy_code_for_"))
                userId = code["dummy_code_for_".Length..];
        }

        var accessToken = $"simulated_{userId}_{Guid.NewGuid():N}";

        return Ok(new
        {
            access_token = accessToken,
            refresh_token = $"{refreshTokenPrefix}{userId}",
            expires_in = 3600,
            token_type = "Bearer",
            user_id = userId
        });
    }
}
