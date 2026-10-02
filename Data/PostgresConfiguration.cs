using Npgsql;

namespace LadingSystem.Data;

public static class PostgresConfiguration
{
    public static string GetConnectionString(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        // Preserve the existing ignored local .env database settings for development.
        if (environment.IsDevelopment())
        {
            var path = Path.Combine(environment.ContentRootPath, ".env");
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var local = new ConfigurationBuilder().AddIniStream(stream).Build();
                if (!string.IsNullOrWhiteSpace(local["host"])) return new NpgsqlConnectionStringBuilder
                {
                    Host = local["host"], Port = int.Parse(local["port"] ?? "5432"),
                    Database = local["database"], Username = local["user"], Password = local["password"],
                    SslMode = Enum.Parse<SslMode>(local["sslmode"] ?? "Require", ignoreCase: true)
                }.ConnectionString;
            }
        }
        throw new InvalidOperationException(
            "Set ConnectionStrings__DefaultConnection to the Supabase PostgreSQL connection string.");
    }
}
