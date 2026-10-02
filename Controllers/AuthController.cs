using System.Globalization;
using System.Security.Claims;
using LadingSystem.Authentication;
using LadingSystem.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LadingSystem.Controllers;

public sealed class AuthController(IAntiforgery antiforgery, FirebaseUserStore users,
    ILogger<AuthController> logger) : Controller
{
    // No UID, email, or profile fields are accepted from the browser.
    public sealed record SessionRequest(bool RememberMe);

    [HttpGet]
    [Authorize(AuthenticationSchemes = FirebaseAuthenticationHandler.SchemeName)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Csrf() => Ok(new
    {
        token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken
    });

    [HttpPost]
    [Authorize(AuthenticationSchemes = FirebaseAuthenticationHandler.SchemeName)]
    public async Task<IActionResult> Session([FromBody] SessionRequest request, CancellationToken cancellationToken)
    {
        var uid = User.FindFirstValue("firebase_uid");
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(email)
            || !long.TryParse(User.FindFirstValue("exp"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var expires)) return Unauthorized();

        Models.User user;
        try
        {
            user = await users.GetOrCreateAsync(new VerifiedFirebaseIdentity(uid, email,
                User.FindFirstValue(ClaimTypes.Name), DateTimeOffset.FromUnixTimeSeconds(expires)), cancellationToken);
        }
        catch (UserIdentityConflictException)
        {
            return Conflict(new { message = "This email is already associated with another account. Contact your administrator." });
        }
        catch (Exception error) when (error is DbUpdateException or NpgsqlException or InvalidOperationException)
        {
            logger.LogError("PostgreSQL account provisioning failed ({ErrorType}).", error.GetType().Name);
            return StatusCode(503, new { message = "Your account could not be loaded. Please try signing in again." });
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new("firebase_uid", user.FirebaseUid),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties
            {
                IsPersistent = request.RememberMe,
                ExpiresUtc = DateTimeOffset.FromUnixTimeSeconds(expires),
                AllowRefresh = false
            });
        return Ok(new { redirectUrl = Url.Action("Dashboard", "Home") });
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home", new { signedOut = true });
    }
}
