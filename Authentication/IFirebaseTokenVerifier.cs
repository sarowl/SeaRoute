namespace LadingSystem.Authentication;

public sealed record VerifiedFirebaseIdentity(
    string Uid, string? Email, string? DisplayName, DateTimeOffset ExpiresAt);

public interface IFirebaseTokenVerifier
{
    Task<VerifiedFirebaseIdentity> VerifyAsync(string idToken, CancellationToken cancellationToken);
}
