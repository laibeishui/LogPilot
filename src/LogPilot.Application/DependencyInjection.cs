using LogPilot.Application.Health;
using LogPilot.Application.Identity;
using LogPilot.Application.Ingestion;
using LogPilot.Application.Logs;
using LogPilot.Application.Options;
using LogPilot.Application.Projects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LogPilot.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddLogPilotApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<IngestionLimits>(configuration.GetSection(IngestionLimits.SectionName));
        services.Configure<QueryLimits>(configuration.GetSection(QueryLimits.SectionName));

        services.AddScoped<ISystemHealthService, SystemHealthService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IProjectAccessService, ProjectAccessService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<ILogQueryService, LogQueryService>();

        services.AddScoped<IIngestApiKeyAuthenticator, IngestApiKeyAuthenticator>();
        services.AddScoped<ILogIngestionService, LogIngestionService>();
        services.AddScoped<IJsonLogIngestService, JsonLogIngestService>();

        return services;
    }
}
