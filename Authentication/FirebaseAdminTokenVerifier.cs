using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace LadingSystem.Authentication;

public sealed class FirebaseAdminTokenVerifier(IOptions<Models.FirebaseOptions> options)
    : IFirebaseTokenVerifier, IDisposable
{
    private readonly Lazy<FirebaseApp> app = new(() => CreateApp(options.Value.ProjectId));

    private static FirebaseApp CreateApp(string projectId)
    {
        GoogleCredential credential;
        try
        {
            credential = GoogleCredential.GetApplicationDefault();
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or ArgumentException)
        {
            throw new FirebaseAdminConfigurationException(error);
        }
        return FirebaseApp.Create(new AppOptions
        {
            ProjectId = projectId, Credential = credential
        }, "searoute-backend");
    }

    public async Task<VerifiedFirebaseIdentity> VerifyAsync(
        string idToken, CancellationToken cancellationToken)
    {
        // The Admin SDK checks signature, issuer, audience, expiry, and revoked/disabled accounts.
        var token = await FirebaseAuth.GetAuth(app.Value)
            .VerifyIdTokenAsync(idToken, true, cancellationToken);
        string? Claim(string key) => token.Claims.TryGetValue(key, out var value)
            ? value as string : null;
        return new(token.Uid, Claim("email"), Claim("name"),
            DateTimeOffset.FromUnixTimeSeconds(token.ExpirationTimeSeconds));
    }

    public void Dispose()
    {
        if (app.IsValueCreated) app.Value.Delete();
    }
}
