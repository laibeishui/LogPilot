using LogPilot.Application.Ingestion;
using LogPilot.Core.Abstractions;
using LogPilot.Infrastructure.Authentication;
using LogPilot.Infrastructure.Notifications;
using LogPilot.Infrastructure.Observability;
using LogPilot.Infrastructure.Options;
using LogPilot.Infrastructure.Otlp;
using LogPilot.Infrastructure.Security;
using LogPilot.Infrastructure.Sqlite;
using LogPilot.Infrastructure.Sqlite.Repositories;
using LogPilot.Infrastructure.VictoriaLogs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LogPilot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLogPilotInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.SectionName));

        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<SqliteMigrator>();

        var apiKeyCrypto = new SecureApiKeyGenerator();
        services.AddSingleton<IApiKeyGenerator>(apiKeyCrypto);
        services.AddSingleton<IApiKeyHasher>(apiKeyCrypto);
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();

        services.AddScoped<IUserRepository, SqliteUserRepository>();
        services.AddScoped<IProjectRepository, SqliteProjectRepository>();
        services.AddScoped<IApplicationRepository, SqliteApplicationRepository>();
        services.AddScoped<IApiKeyRepository, SqliteApiKeyRepository>();
        services.AddScoped<IAlertChannelRepository, SqliteAlertChannelRepository>();
        services.AddScoped<IAlertRuleRepository, SqliteAlertRuleRepository>();
        services.AddScoped<IAlertEventRepository, SqliteAlertEventRepository>();

        // 渠道配置加密
        var alertingKey = configuration["Alerting:ConfigKey"];
        services.AddSingleton(new ChannelConfigProtector(alertingKey));

        // 通知渠道
        services.AddSingleton<INotifier, DingTalkNotifier>();
        services.AddSingleton<INotifier, WeComNotifier>();
        services.AddSingleton<INotifier, EmailNotifier>();
        services.AddSingleton<INotifier, WebhookNotifier>();

        services.AddSingleton<IHealthProbe, VictoriaLogsHealthProbe>();
        services.AddSingleton<ILogWriteMetrics, LogWriteMetrics>();
        services.AddSingleton<ILogWriter, VictoriaLogsWriter>();
        services.AddSingleton<ILogSearcher, VictoriaLogsSearcher>();
        services.AddScoped<IOtlpLogIngestService, OtlpLogIngestService>();

        var victoriaBaseUrl = configuration.GetSection(StorageOptions.SectionName)
            .Get<StorageOptions>()?.VictoriaLogs.BaseUrl
            ?? "http://localhost:9428";

        var timeoutSeconds = configuration.GetSection(StorageOptions.SectionName)
            .Get<StorageOptions>()?.VictoriaLogs.TimeoutSeconds
            ?? 10;

        services.AddHttpClient("VictoriaLogs", client =>
        {
            client.BaseAddress = new Uri(victoriaBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });

        return services;
    }
}
