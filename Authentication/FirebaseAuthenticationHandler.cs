using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LadingSystem.Authentication;

public sealed class FirebaseAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, IFirebaseTokenVerifier verifier)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Firebase";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization")) return AuthenticateResult.NoResult();
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter))
            return AuthenticateResult.Fail("A Firebase ID token is required.");

        try
        {
            var identity = await verifier.VerifyAsync(header.Parameter, Context.RequestAborted);
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, identity.Uid),
                new("firebase_uid", identity.Uid),
                new("exp", identity.ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
            };
            if (identity.Email is { } email) claims.Add(new(ClaimTypes.Email, email));
            if (identity.DisplayName is { } name) claims.Add(new(ClaimTypes.Name, name));
            return AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
        }
        catch (FirebaseAuthException error) when (error.AuthErrorCode is
            AuthErrorCode.ExpiredIdToken or AuthErrorCode.InvalidIdToken or
            AuthErrorCode.RevokedIdToken or AuthErrorCode.UserNotFound)
        {
            return AuthenticateResult.Fail("The Firebase ID token is invalid or no longer active.");
        }
        catch (FirebaseAdminConfigurationException error)
        {
            // Log only our fixed setup guidance, never SDK exception details or key contents.
            Logger.LogError("{ConfigurationError}", error.Message);
            Context.Items["FirebaseUnavailable"] = true;
            return AuthenticateResult.Fail("Authentication service unavailable.");
        }
        catch (ArgumentException)
        {
            return AuthenticateResult.Fail("The Firebase ID token is invalid.");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Do not expose credentials or raw tokens in responses or logs.
            Logger.LogError("Firebase Admin verification is unavailable ({ErrorType}, {AuthErrorCode}). Check ADC, project permissions, and network access.",
                error.GetType().Name, (error as FirebaseAuthException)?.AuthErrorCode.ToString() ?? "not applicable");
            Context.Items["FirebaseUnavailable"] = true;
            return AuthenticateResult.Fail("Authentication service unavailable.");
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = Context.Items.ContainsKey("FirebaseUnavailable")
            ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Response.WriteAsJsonAsync(new
        {
            message = Response.StatusCode == StatusCodes.Status503ServiceUnavailable
                ? "Sign-in is temporarily unavailable. Contact your SeaRoute administrator."
                : "Your sign-in could not be verified. Please sign in again."
        });
    }
}
