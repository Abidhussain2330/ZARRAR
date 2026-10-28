using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog;

namespace BURAQ.Extensions.Startup;

internal static class DatabaseExtension
{
    extension(WebApplicationBuilder builder)
    {
        internal void ConfigureDatabase()
        {
            if (!builder.Configuration.GetSection("ConnectionStrings").GetSection("Database").Exists())
                ExitWithFatalMessage(
                    StaticLocalizer[nameof(Resources.Program.Database_NoConnectionString)]);

            builder.Services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseNpgsql(builder.Configuration.GetConnectionString("Database"),
                        o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));

                    // The schema is owned by the checked-in migrations. Small differences between
                    // the runtime model and the migration snapshot (e.g. relationship configuration
                    // details) must not stop the server from starting.
                    options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

                    if (!builder.Environment.IsDevelopment())
                        return;

                    options.EnableSensitiveDataLogging();
                    options.EnableDetailedErrors();
                }
            );

            try
            {
                builder.Configuration.AddEntityConfiguration(options =>
                {
                    options.UseNpgsql(builder.Configuration.GetConnectionString("Database"));
                });
            }
            catch (Exception e)
            {
                if (builder.Configuration.GetSection("ConnectionStrings").GetSection("Database").Exists())
                    Log.Logger.Error(StaticLocalizer[
                        nameof(Resources.Program.Database_CurrentConnectionString),
                        RedactConnectionString(builder.Configuration.GetConnectionString("Database"))]);
                ExitWithFatalMessage(
                    StaticLocalizer[nameof(Resources.Program.Database_ConnectionFailed), e.Message]);
            }
        }
    }

    /// <summary>
    /// Strip credential material from a connection string before it reaches a log sink.
    /// </summary>
    /// <remarks>
    /// A failed database connection at startup previously logged the connection string verbatim,
    /// which for Npgsql includes <c>Password=</c> (finding F-005). Host, database and username are
    /// retained because they are what actually makes the error diagnosable.
    /// </remarks>
    internal static string RedactConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return "null";

        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < parts.Length; i++)
        {
            var separator = parts[i].IndexOf('=');

            if (separator < 0)
                continue;

            var key = parts[i][..separator].Trim();

            if (key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("pwd", StringComparison.OrdinalIgnoreCase))
                parts[i] = $"{key}=******";
        }

        return string.Join(';', parts);
    }
}
