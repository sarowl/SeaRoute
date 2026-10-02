using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using LadingSystem.Authentication;
using LadingSystem.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LadingSystem.Tests;

// Real MVC authentication, antiforgery, cookies, and EF persistence. Only the
// external token verifier is substituted; production uses Firebase Admin SDK.
public sealed class AuthenticationTests
{
    [Fact]
    public async Task SessionUsesVerifiedUidIgnoresSubmittedIdentityAndReusesTheUser()
    {
        using var app = new TestApp();
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new("Bearer", "valid-token");
        await AddCsrf(client);
        var response = await client.PostAsJsonAsync("/Auth/Session", new
        {
            rememberMe = true, firebaseUid = "someone-else", uid = "someone-else",
            email = "attacker@example.com", fullName = "Forged name"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("SeaRoute.Session="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = Assert.Single(await db.Users.ToListAsync());
            Assert.Equal("verified-uid", user.FirebaseUid);
            Assert.Equal("verified@example.com", user.Email);
            Assert.Equal("Verified name", user.FullName);
        }

        // Repeat login must not create a duplicate row.
        await AddCsrf(client);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/Auth/Session", new { rememberMe = false })).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        var dashboard = await client.GetStringAsync("/Home/Dashboard");
        Assert.Contains("Verified name", dashboard);
        Assert.Contains("verified@example.com", dashboard);
        Assert.DoesNotContain("firestore", dashboard, StringComparison.OrdinalIgnoreCase);
        using var finalScope = app.Services.CreateScope();
        Assert.Equal(1, await finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.CountAsync());

        // Cookie-based logout needs the cookie identity's own antiforgery token.
        var csrf = WebUtility.HtmlDecode(Regex.Match(dashboard,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(csrf);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var logout = await client.PostAsync("/Auth/Logout", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = csrf }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Home/Dashboard")).StatusCode);
    }

    [Fact]
    public async Task InvalidOrMissingTokensAndCsrfCannotProvisionUsers()
    {
        using var app = new TestApp();
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/Auth/Csrf")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "forged-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/Auth/Csrf")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/Auth/Session", new { uid = "verified-uid" })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "valid-token");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/Auth/Session", new { rememberMe = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Auth/Logout", null)).StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.ToListAsync());
    }

    [Fact]
    public async Task AdminCredentialFailureDoesNotCreateASessionOrUser()
    {
        using var app = new TestApp();
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new("Bearer", "admin-unavailable");
        var response = await client.GetAsync("/Auth/Csrf");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sign-in is temporarily unavailable. Contact your SeaRoute administrator.",
            body.GetProperty("message").GetString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var scope = app.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.ToListAsync());
    }

    private static async Task AddCsrf(HttpClient client)
    {
        var result = await client.GetFromJsonAsync<JsonElement>("/Auth/Csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", result.GetProperty("token").GetString());
    }

    private sealed class TestVerifier : IFirebaseTokenVerifier
    {
        public Task<VerifiedFirebaseIdentity> VerifyAsync(string token, CancellationToken cancellationToken)
        {
            if (token == "admin-unavailable") throw new FirebaseAdminConfigurationException(
                new InvalidOperationException("Private credential details must not appear in the response."));
            if (token != "valid-token") throw new ArgumentException("Invalid token.");
            return Task.FromResult(new VerifiedFirebaseIdentity("verified-uid", "verified@example.com",
                "Verified name", DateTimeOffset.UtcNow.AddHours(1)));
        }
    }

    private sealed class TestApp : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(AppPaths.Root);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFirebaseTokenVerifier>();
                services.AddSingleton<IFirebaseTokenVerifier, TestVerifier>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                connection.Open();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
                using var provider = services.BuildServiceProvider();
                using var scope = provider.CreateScope();
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) connection.Dispose();
        }
    }
}
