using LadingSystem.Authentication;
using LadingSystem.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LadingSystem.Data;

public sealed class UserIdentityConflictException : Exception;

public sealed class FirebaseUserStore(ApplicationDbContext db)
{
    public async Task<User> GetOrCreateAsync(VerifiedFirebaseIdentity identity, CancellationToken cancellationToken)
    {
        var existing = await db.Users.SingleOrDefaultAsync(u => u.FirebaseUid == identity.Uid, cancellationToken);
        if (existing is not null) return existing;

        var user = new User
        {
            FirebaseUid = identity.Uid, Email = identity.Email!,
            FullName = string.IsNullOrWhiteSpace(identity.DisplayName) ? identity.Email! : identity.DisplayName.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return user;
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(user).State = EntityState.Detached;
            // A concurrent first login may already have inserted this same verified UID.
            existing = await db.Users.SingleOrDefaultAsync(u => u.FirebaseUid == identity.Uid, cancellationToken);
            if (existing is not null) return existing;
            // Never attach a new Firebase UID to an existing user merely by matching email.
            throw new UserIdentityConflictException();
        }
    }
}
