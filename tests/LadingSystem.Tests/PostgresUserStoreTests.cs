using LadingSystem.Authentication;
using LadingSystem.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LadingSystem.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SEAROUTE_LIVE_POSTGRES_TESTS") != "1")
            Skip = "Opt-in PostgreSQL test; creates and cleans up temporary Users rows.";
    }
}

public sealed class PostgresUserStoreTests
{
    [PostgresFact]
    public async Task ConcurrentFirstLoginsReuseUidAndEmailMatchesCannotRelinkAnAccount()
    {
        var root = AppPaths.Root;
        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var connection = PostgresConfiguration.GetConnectionString(config, new LocalEnvironment(root));
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options;
        var uid = "searoute-store-test-" + Guid.NewGuid().ToString("N");
        var otherUid = "searoute-store-test-" + Guid.NewGuid().ToString("N");
        var identity = new VerifiedFirebaseIdentity(uid, uid + "@example.com", "Store test", DateTimeOffset.UtcNow.AddHours(1));
        try
        {
            async Task<int> Login()
            {
                await using var db = new ApplicationDbContext(options);
                return (await new FirebaseUserStore(db).GetOrCreateAsync(identity, CancellationToken.None)).Id;
            }
            var ids = await Task.WhenAll(Login(), Login());
            Assert.Equal(ids[0], ids[1]);
            await using var db = new ApplicationDbContext(options);
            var user = Assert.Single(await db.Users.Where(u => u.FirebaseUid == uid).ToListAsync());
            Assert.Equal(identity.Email, user.Email);
            Assert.Equal("Store test", user.FullName);
            await Assert.ThrowsAsync<UserIdentityConflictException>(() =>
                new FirebaseUserStore(db).GetOrCreateAsync(identity with { Uid = otherUid }, CancellationToken.None));
            Assert.False(await db.Users.AnyAsync(u => u.FirebaseUid == otherUid));
            Assert.Equal(uid, (await db.Users.SingleAsync(u => u.Id == ids[0])).FirebaseUid);
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            await cleanup.Users.Where(u => u.FirebaseUid == uid || u.FirebaseUid == otherUid).ExecuteDeleteAsync();
        }
    }

    private sealed class LocalEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "LadingSystem";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
