using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace LogPilot.DemoClient;

/// <summary>
/// LogPilot 对接演示：交互式输入「内容 + 条数」持续生成日志。
/// </summary>
internal static class Program
{
    private const int MaxBatch = 500;

    private static async Task<int> Main(string[] args)
    {
        var options = CliOptions.Parse(args);
        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (string.IsNullOrWhiteSpace(options.Token))
        {
            Console.Error.WriteLine("缺少 Token。请在 LogPilot「接入中心」生成后写入 local.settings.json。");
            PrintHelp();
            return 1;
        }

        Console.WriteLine("=== LogPilot 交互式日志生成器 ===");
        Console.WriteLine($"URL     : {options.BaseUrl}");
        Console.WriteLine($"Service : {options.ServiceName}");
        Console.WriteLine($"Mode    : {options.Mode}");
        Console.WriteLine($"查看    : {options.BaseUrl.TrimEnd('/')}/logs?auto=1");
        Console.WriteLine();
        PrintInteractiveHelp();

        using var http = CreateHttpClient(options.BaseUrl);
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", options.Token);

        using var otlpFactory = options.Mode == "otlp" ? CreateOtlpLoggerFactory(options) : null;
        var otlpLogger = otlpFactory?.CreateLogger("LogPilot.DemoClient");

        var level = "Info";
        var mode = options.Mode;

        while (true)
        {
            Console.WriteLine();
            Console.Write("内容> ");
            var line = Console.ReadLine();
            if (line is null)
            {
                break;
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (IsQuit(line))
            {
                Console.WriteLine("已退出。");
                break;
            }

            if (line.Equals("help", StringComparison.OrdinalIgnoreCase)
                || line is "?" or "？")
            {
                PrintInteractiveHelp();
                continue;
            }

            if (TryHandleLevelCommand(line, ref level))
            {
                Console.WriteLine($"默认级别已设为: {level}");
                continue;
            }

            if (TryHandleModeCommand(line, ref mode))
            {
                if (mode == "otlp" && otlpLogger is null)
                {
                    Console.WriteLine("当前进程以 json 启动，如需 otlp 请用 --mode otlp 重新运行。");
                    mode = "json";
                }
                else
                {
                    Console.WriteLine($"发送模式已设为: {mode}");
                }

                continue;
            }

            if (IsChineseSampleCommand(line))
            {
                try
                {
                    await SendChineseSamplesAsync(http, options).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"发送失败: {ex.Message}");
                    Console.ResetColor();
                }

                continue;
            }

            // 支持一行写法：  10 用户登录成功
            string message;
            int count;
            if (TryParseCountAndMessage(line, out count, out message))
            {
                // already parsed
            }
            else
            {
                message = line;
                Console.Write("条数> ");
                var countLine = Console.ReadLine()?.Trim();
                if (countLine is null || IsQuit(countLine))
                {
                    Console.WriteLine("已退出。");
                    break;
                }

                if (!int.TryParse(countLine, out count) || count <= 0)
                {
                    Console.WriteLine("条数必须是正整数，例如 5");
                    continue;
                }
            }

            if (count > MaxBatch)
            {
                Console.WriteLine($"单次最多 {MaxBatch} 条，已按 {MaxBatch} 发送。");
                count = MaxBatch;
            }

            try
            {
                if (mode == "otlp")
                {
                    await SendBatchOtlpAsync(otlpLogger!, options.ServiceName, message, level, count)
                        .ConfigureAwait(false);
                }
                else
                {
                    await SendBatchJsonAsync(http, options, message, level, count).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"发送失败: {ex.Message}");
                Console.ResetColor();
            }
        }

        return 0;
    }

    private static async Task SendBatchJsonAsync(
        HttpClient http,
        CliOptions options,
        string message,
        string level,
        int count)
    {
        var now = DateTimeOffset.UtcNow;
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var logs = new List<JsonLogItem>(count);
        for (var i = 1; i <= count; i++)
        {
            logs.Add(new JsonLogItem
            {
                Timestamp = now.AddMilliseconds(i),
                ServiceName = options.ServiceName,
                Environment = "dev",
                Level = level,
                Message = $"[demo] ({i}/{count}) {message}",
                TraceId = traceId,
                Attributes = new Dictionary<string, object?>
                {
                    ["module"] = "DemoClient",
                    ["mode"] = "json",
                    ["seq"] = i,
                    ["batchSize"] = count
                }
            });
        }

        var url = $"{options.BaseUrl.TrimEnd('/')}/api/v1/ingest/logs";
        using var response = await http.PostAsJsonAsync(
            url,
            new JsonIngestRequest { Logs = logs },
            JsonOptions).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine(PrettyJson(body));
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ 已发送 {count} 条 | level={level} | TraceId={traceId}");
        Console.ResetColor();
    }

    /// <summary>内置中文样例：覆盖中文正文、中文子串、中文异常消息/堆栈等检索场景。</summary>
    private static readonly (string Level, string Message, string? ExType, string? ExMessage, string? ExStack)[] ChineseSamples =
    [
        ("Info", "用户登录成功，用户ID=10086，来源=微信小程序", null, null, null),
        ("Info", "订单支付完成，订单号=SO202610170001，金额=299.00 元", null, null, null),
        ("Warn", "缓存命中率偏低，当前命中率=42%，建议检查热点 Key", null, null, null),
        ("Warn", "接口响应较慢，耗时 1820ms，超过阈值 1000ms", null, null, null),
        ("Error", "数据库连接超时，已重试 3 次仍然失败", null, null, null),
        ("Error", "调用第三方支付网关失败：连接被拒绝", null, null, null),
        ("Info", "定时任务「每日对账」开始执行", null, null, null),
        ("Info", "定时任务「每日对账」执行完成，共处理 1280 条记录", null, null, null),
        ("Error", "处理订单时发生未捕获异常",
            "System.NullReferenceException",
            "未将对象引用设置到对象的实例",
            "   at LogPilot.Demo.OrderService.结算订单(Order order)\n   at LogPilot.Demo.Program.Main(String[] args)"),
        ("Error", "库存扣减失败",
            "System.InvalidOperationException",
            "库存不足：商品编号 SKU-8899，当前库存 0",
            "   at LogPilot.Demo.StockService.扣减库存(String sku, Int32 quantity)"),
    ];

    private static async Task SendChineseSamplesAsync(HttpClient http, CliOptions options)
    {
        var now = DateTimeOffset.UtcNow;
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var logs = new List<JsonLogItem>(ChineseSamples.Length);

        for (var i = 0; i < ChineseSamples.Length; i++)
        {
            var (sampleLevel, message, exType, exMessage, exStack) = ChineseSamples[i];
            logs.Add(new JsonLogItem
            {
                Timestamp = now.AddMilliseconds(i),
                ServiceName = options.ServiceName,
                Environment = "dev",
                Level = sampleLevel,
                Message = $"[中文样例] {message}",
                TraceId = traceId,
                ExceptionType = exType,
                ExceptionMessage = exMessage,
                ExceptionStackTrace = exStack,
                Attributes = new Dictionary<string, object?>
                {
                    ["module"] = "中文样例",
                    ["biz"] = "订单",
                    ["seq"] = i + 1
                }
            });
        }

        var url = $"{options.BaseUrl.TrimEnd('/')}/api/v1/ingest/logs";
        using var response = await http.PostAsJsonAsync(
            url,
            new JsonIngestRequest { Logs = logs },
            JsonOptions).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine(PrettyJson(body));
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ 已发送 {logs.Count} 条中文样例 | TraceId={traceId}");
        Console.WriteLine("  试试这些关键词: 超时 / 连接 / 订单 / 库存不足 / 每日对账");
        Console.ResetColor();
    }

    private static bool IsChineseSampleCommand(string line)
        => line.Equals("zh", StringComparison.OrdinalIgnoreCase)
           || line is "中文" or "样例" or "中文样例";

    private static async Task SendBatchOtlpAsync(
        ILogger logger,
        string serviceName,
        string message,
        string level,
        int count)
    {
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["trace_id"] = traceId,
            ["mode"] = "otlp",
            ["service"] = serviceName
        }))
        {
            for (var i = 1; i <= count; i++)
            {
                var text = $"[demo] ({i}/{count}) {message}";
                switch (level.ToUpperInvariant())
                {
                    case "TRACE":
                        logger.LogTrace("{Message}", text);
                        break;
                    case "DEBUG":
                        logger.LogDebug("{Message}", text);
                        break;
                    case "WARN":
                    case "WARNING":
                        logger.LogWarning("{Message}", text);
                        break;
                    case "ERROR":
                    case "FATAL":
                        logger.LogError("{Message}", text);
                        break;
                    default:
                        logger.LogInformation("{Message}", text);
                        break;
                }
            }
        }

        // 等 OTLP 导出刷出
        await Task.Delay(800).ConfigureAwait(false);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ 已通过 OTLP 发送 {count} 条 | level={level} | TraceId={traceId}");
        Console.ResetColor();
    }

    private static ILoggerFactory CreateOtlpLoggerFactory(CliOptions options)
        => LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
                logging.SetResourceBuilder(
                    ResourceBuilder.CreateDefault()
                        .AddService(options.ServiceName)
                        .AddAttributes(new Dictionary<string, object>
                        {
                            ["deployment.environment"] = "dev"
                        }));

                logging.AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri($"{options.BaseUrl.TrimEnd('/')}/v1/logs");
                    o.Protocol = OtlpExportProtocol.HttpProtobuf;
                    o.Headers = $"Authorization=Bearer {options.Token}";
                    o.HttpClientFactory = () => CreateHttpClient(options.BaseUrl);
                });
            });
        });

    private static bool TryParseCountAndMessage(string line, out int count, out string message)
    {
        count = 0;
        message = string.Empty;
        var parts = line.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out count) || count <= 0)
        {
            return false;
        }

        message = parts[1].Trim();
        return message.Length > 0;
    }

    private static bool TryHandleLevelCommand(string line, ref string level)
    {
        if (!line.StartsWith("level ", StringComparison.OrdinalIgnoreCase)
            && !line.StartsWith("级别 ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = line.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1).FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        level = NormalizeLevel(value);
        return true;
    }

    private static bool TryHandleModeCommand(string line, ref string mode)
    {
        if (!line.StartsWith("mode ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = line.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1).FirstOrDefault()?.Trim().ToLowerInvariant();
        if (value is not ("json" or "otlp"))
        {
            Console.WriteLine("mode 只能是 json 或 otlp");
            return true;
        }

        mode = value;
        return true;
    }

    private static string NormalizeLevel(string value)
        => value.ToUpperInvariant() switch
        {
            "TRACE" => "Trace",
            "DEBUG" => "Debug",
            "INFO" or "INFORMATION" => "Info",
            "WARN" or "WARNING" => "Warn",
            "ERROR" => "Error",
            "FATAL" or "CRITICAL" => "Fatal",
            _ => value
        };

    private static bool IsQuit(string line)
        => line.Equals("quit", StringComparison.OrdinalIgnoreCase)
           || line.Equals("exit", StringComparison.OrdinalIgnoreCase)
           || line.Equals("q", StringComparison.OrdinalIgnoreCase)
           || line is "退出";

    private static void PrintInteractiveHelp()
    {
        Console.WriteLine("""
            怎么用:
              1) 直接输入内容，再输入条数
              2) 一行写完:  10 用户登录成功

            命令:
              zh | 中文           发送 10 条内置中文样例日志（含中文异常堆栈）
              level Info|Warn|Error|Debug|Trace|Fatal
              mode json|otlp
              help / quit
            """);
    }

    private static HttpClient CreateHttpClient(string baseUrl)
    {
        var handler = new HttpClientHandler();
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            LogPilot.DemoClient — 交互式向 LogPilot 写入测试日志

            用法:
              dotnet run --project examples/dotnet-console-ingest -- [选项]

            选项:
              --url <url>       默认读 local.settings.json（如 https://localhost:7243）
              --token <token>   接入 Token
              --mode <mode>     json（默认）| otlp
              --service <name>  服务名（默认 demo-console）
              --help            显示帮助
            """);
    }

    private static string PrettyJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(empty body)";
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return text;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed class CliOptions
{
    public string BaseUrl { get; init; } = "https://localhost:7243";
    public string Token { get; init; } = string.Empty;
    public string Mode { get; init; } = "json";
    public string ServiceName { get; init; } = "demo-console";
    public bool ShowHelp { get; init; }

    public static CliOptions Parse(string[] args)
    {
        var local = LoadLocalSettings();
        var url = FirstNonEmpty(
            Environment.GetEnvironmentVariable("LOGPILOT_URL"),
            local.Url,
            "https://localhost:7243")!;
        var token = FirstNonEmpty(
            Environment.GetEnvironmentVariable("TOKEN"),
            local.Token,
            string.Empty)!;
        var mode = "json";
        var service = "demo-console";
        var help = false;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : string.Empty;

            switch (a)
            {
                case "--help":
                case "-h":
                case "/?":
                    help = true;
                    break;
                case "--url":
                    url = Next();
                    break;
                case "--token":
                    token = Next();
                    break;
                case "--mode":
                    mode = Next().ToLowerInvariant();
                    break;
                case "--service":
                    service = Next();
                    break;
            }
        }

        if (mode is not ("json" or "otlp"))
        {
            mode = "json";
        }

        return new CliOptions
        {
            BaseUrl = string.IsNullOrWhiteSpace(url) ? "https://localhost:7243" : url.Trim(),
            Token = token.Trim(),
            Mode = mode,
            ServiceName = string.IsNullOrWhiteSpace(service) ? "demo-console" : service.Trim(),
            ShowHelp = help
        };
    }

    private static (string? Url, string? Token) LoadLocalSettings()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "local.settings.json");
            if (!File.Exists(path))
            {
                path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "local.settings.json"));
            }

            if (!File.Exists(path))
            {
                return (null, null);
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var url = root.TryGetProperty("LOGPILOT_URL", out var u) ? u.GetString() : null;
            var token = root.TryGetProperty("TOKEN", out var t) ? t.GetString() : null;
            return (url, token);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                return v;
            }
        }

        return null;
    }
}

internal sealed class JsonIngestRequest
{
    public required List<JsonLogItem> Logs { get; init; }
}

internal sealed class JsonLogItem
{
    public DateTimeOffset Timestamp { get; init; }
    public required string ServiceName { get; init; }
    public string? Environment { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
    public string? TraceId { get; init; }
    public string? ExceptionType { get; init; }
    public string? ExceptionMessage { get; init; }
    public string? ExceptionStackTrace { get; init; }
    public Dictionary<string, object?>? Attributes { get; init; }
}
