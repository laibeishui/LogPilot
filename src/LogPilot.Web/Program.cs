using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using LogPilot.Application;
using LogPilot.Application.Options;
using LogPilot.Infrastructure;
using LogPilot.Infrastructure.Security;
using LogPilot.Infrastructure.Sqlite;
using LogPilot.Web.Filters;
using LogPilot.Web.Middleware;
using LogPilot.Web.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<ApiExceptionFilter>();
    options.Filters.Add<ValidateAntiforgeryApiFilter>();
})
.AddJsonOptions(options =>
{
    // 允许以字符串形式绑定枚举（如 "DingTalk"、"Ratio"），同时保留数值（allowIntegerValues）。
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddScoped<ApiExceptionFilter>();
builder.Services.AddScoped<ValidateAntiforgeryApiFilter>();
builder.Services.AddScoped<AuthCookieService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "logpilot.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/login";
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // 人员/项目管理等仅系统管理员可用（UserStateMiddleware 已在请求早期刷新该标志）。
    options.AddPolicy(LogPilotPolicies.SystemAdmin, policy => policy.RequireAssertion(
        context => context.User.IsSystemAdmin()));
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
});

// 日志接入限流：仅作用于接入端点，按接入 Token / 来源 IP 分区。
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        IngestRateLimitPolicy.CreatePartition(
            context,
            context.RequestServices.GetRequiredService<IOptions<IngestionLimits>>().Value));
});

builder.Services.AddLogPilotApplication(builder.Configuration);
builder.Services.AddLogPilotInfrastructure(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var migrator = scope.ServiceProvider.GetRequiredService<SqliteMigrator>();
    await migrator.MigrateAsync();
}

var channelProtector = app.Services.GetRequiredService<ChannelConfigProtector>();
switch (channelProtector.KeyStatus)
{
    case ChannelKeyStatus.NotConfigured:
        app.Logger.LogWarning(
            "Alerting:ConfigKey 未配置，通知渠道配置正在使用开发默认密钥加密。生产环境请务必配置有效密钥。");
        break;
    case ChannelKeyStatus.Invalid:
        app.Logger.LogError(
            "Alerting:ConfigKey 格式无效（{Reason}），已回退到开发默认密钥，通知渠道配置将以公开密钥加密。请修正该配置后重启。",
            channelProtector.KeyErrorMessage);
        break;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// 必须最外层：保证 /api 请求（含中间件阶段抛出）始终得到统一 JSON 错误体
app.UseMiddleware<ApiExceptionMiddleware>();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseMiddleware<SetupGateMiddleware>();
app.UseAuthentication();
app.UseMiddleware<UserStateMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

/// <summary>
/// 供集成测试 WebApplicationFactory 识别入口程序集。
/// </summary>
public partial class Program;
