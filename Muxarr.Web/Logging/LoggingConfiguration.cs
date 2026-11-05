using System.Reflection;
using Arrkit.Logging;
using Microsoft.Data.Sqlite;
using Muxarr.Data;
using Serilog;
using Serilog.Events;

namespace Muxarr.Web.Logging;

public static class LoggingConfiguration
{
    public static WebApplicationBuilder ConfigureLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();

        var environment = builder.Environment.EnvironmentName;
        var configuration = builder.Configuration;
        var projectName = GetProjectName();

        var logConfig = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.WithProperty("Environment", environment)
            .Enrich.WithProperty("Project", projectName)
            .WriteTo.Console();

        Log.Logger = logConfig.CreateLogger();
        builder.Logging.AddSerilog(Log.Logger, true);

        // The Logs page (Arrkit's LogView, as in the sister apps): lines since start in memory, and the
        // last Logs__KeepLines lines (default 2000) in <config>/logs/remuxarr.jsonl for the second tab.
        var memoryLog = new MemoryLog();
        memoryLog.KeepFile(Path.Combine(ConfigFolder(configuration), "logs", "remuxarr.jsonl"),
            configuration.GetValue(MemoryLog.KeepLinesKey, MemoryLog.Capacity));
        builder.Logging.AddProvider(new MemoryLoggerProvider(memoryLog));
        // The same levels as the console.
        builder.Logging.AddFilter<MemoryLoggerProvider>(null, LogLevel.Information);
        builder.Logging.AddFilter<MemoryLoggerProvider>("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter<MemoryLoggerProvider>("System", LogLevel.Warning);
        builder.Services.AddSingleton(memoryLog);

        Log.Logger.Information("Initialized logger for {Project}", projectName);

        return builder;
    }

    /// <summary>
    ///     Runs the application with robust error handling, ensuring any startup exceptions are logged.
    /// </summary>
    public static async Task RunWithLoggingAsync(this WebApplicationBuilder builder,
        Func<WebApplicationBuilder, Task> configure)
    {
        try
        {
            await configure(builder);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>The folder the database is in (/config in the container), where logs/ goes too.</summary>
    private static string ConfigFolder(IConfiguration configuration)
    {
        var connectionString = ContainerAppData.ResolveConnectionString(
            configuration.GetConnectionString("DefaultConnection") ?? "Data Source=/config/muxarr.db");
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        return Path.GetDirectoryName(Path.GetFullPath(dataSource)) ?? "/config";
    }

    public static string GetProjectName()
    {
        return Assembly.GetEntryAssembly()?.GetName().Name ?? "Unknown";
    }
}
