using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using Microsoft.Extensions.FileProviders;
using Sonverter;
using Sonverter.Converters;

// 注册转换器
ConverterRegistry.Register(config => new SingboxConverter(config));
ConverterRegistry.Register(config => new DaeConverter(config));
ConverterRegistry.Register(config => new ExampleConverter(config));

var cliArgs = new CommandLineArgs(Environment.GetCommandLineArgs().Skip(1).ToArray());
Log.IsDebug = cliArgs.Debug;

if (!string.IsNullOrWhiteSpace(cliArgs.WorkDir))
{
    try
    {
        var targetDir = Path.GetFullPath(cliArgs.WorkDir);
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }
        Directory.SetCurrentDirectory(targetDir);
        Log.Debug($"Working directory switched to: {targetDir}");
    }
    catch (Exception ex)
    {
        Log.Error($"Failed to switch working directory ({cliArgs.WorkDir}): {ex.Message}");
        Environment.ExitCode = 1;
        return;
    }
}

if (cliArgs.Help)
{
    PrintHelp();
    return;
}

if (cliArgs.Version)
{
    VersionInfo.Print();
    return;
}

if (cliArgs.ListConverters)
{
    Console.WriteLine("Available converters:");
    foreach (var info in ConverterRegistry.ListInfo())
    {
        Console.WriteLine($"\n  {info.Name}: {info.Description}");
        Console.WriteLine($"    Supported extensions: {string.Join(", ", info.SupportedExtensions)}");
    }
    return;
}

if (cliArgs.Serve)
{
    await RunServerAsync(cliArgs.Port, cliArgs.Debug, cliArgs.Config);
    return;
}

if (cliArgs.Input is null)
{
    PrintHelp();
    Log.Error("Please specify input file or URL (--input)");
    Environment.ExitCode = 1;
    return;
}

try
{
    var config = DefaultConfig.Load(cliArgs.Config);
    var converter = ConverterRegistry.Get(cliArgs.Converter, config);
    var outputPath = converter.ConvertFile(cliArgs.Input, cliArgs.Output);
    Log.Info($"Conversion completed: {outputPath}");
}
catch (Exception e)
{
    Log.Error($"Conversion failed: {e.Message}");
    if (cliArgs.Debug)
    {
        Log.Debug(e.ToString());
    }
    Environment.ExitCode = 1;
}

return;

static void PrintHelp()
{
    Console.WriteLine("""
        Sonverter - Singbox / Dae Outbound Node Conversion Tool

        Usage:
          --input, -i <file|url>    Input file path or HTTP/HTTPS URL
          --converter, -c <name>    Converter name (default: singbox, available: singbox, dae, example)
          --output, -o <file>       Output file path
          --config, -f <file>       Config file path (default: template.json)
          --workdir, -w, --dir <dir> Working directory (default: current directory)
          --serve                   Start web server
          --port, -p <port>         Web server port (default: 8080)
          --debug                   Enable debug logging
          --version, -v             Display version and build info
          --list-converters         List all available converters
          --help, -h                Display help

        Examples:
          dotnet run -- --input input_example.json
          dotnet run -- --input "https://example.com/nodes?target=sing-box" -o out.json
          dotnet run -- --input nodes.txt --converter dae
          dotnet run -- --serve --port 8080 --workdir /etc/sonverter
        """);
}

static async Task RunServerAsync(int port, bool debug, string? configPath)
{
    Log.IsDebug = debug;
    var currentDir = Directory.GetCurrentDirectory();

    // 如果未显式提供 configPath，但当前工作目录下存在 template.json，则自动加载覆盖
    if (string.IsNullOrWhiteSpace(configPath))
    {
        var localTemplate = Path.Combine(currentDir, "template.json");
        if (File.Exists(localTemplate))
            configPath = localTemplate;
    }

    var config = DefaultConfig.Load(configPath);

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = [],
        ContentRootPath = currentDir,
    });

    // 屏蔽 ASP.NET Core 内部诊断噪音，使用自定义单行日志
    builder.Logging.ClearProviders();
    builder.Logging.AddProvider(new CleanLoggerProvider());

    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    });

    var app = builder.Build();

    // 单行请求访问日志中间件
    app.Use(async (context, next) =>
    {
        var sw = Stopwatch.StartNew();
        Exception? error = null;
        try
        {
            await next();
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            LogRequest(context, sw.ElapsedMilliseconds, error);
        }
    });

    // 前端资源已编译进程序集（嵌入式资源），直接从程序集内提供静态文件
    var embeddedFileProvider = new EmbeddedFileProvider(typeof(Program).Assembly, "Sonverter.wwwroot");
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = embeddedFileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = embeddedFileProvider });

    static async Task SendJsonAsync(HttpContext ctx, JsonNode json, int statusCode = StatusCodes.Status200OK)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsync(JsonHelper.Serialize(json));
    }

    static async Task SendTextAsync(HttpContext ctx, string text, string contentType = "text/plain; charset=utf-8", int statusCode = StatusCodes.Status200OK)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = contentType;
        await ctx.Response.WriteAsync(text);
    }

    app.MapGet("/api/health", async (HttpContext ctx) =>
    {
        await SendJsonAsync(ctx, new JsonObject { ["status"] = "ok" });
    });

    app.MapGet("/api/version", async (HttpContext ctx) =>
    {
        await SendJsonAsync(ctx, new JsonObject
        {
            ["name"] = VersionInfo.ProductName,
            ["build_date"] = VersionInfo.BuildDate,
            ["version"] = VersionInfo.BuildDate,
            ["build_time"] = VersionInfo.BuildDate,
            ["dotnet"] = VersionInfo.DotnetVersion,
            ["runtime"] = VersionInfo.Runtime,
            ["platform"] = VersionInfo.Platform,
            ["process_architecture"] = VersionInfo.ProcessArchitecture,
        });
    });

    app.MapGet("/api/converters", async (HttpContext ctx) =>
    {
        var converters = new JsonArray();
        foreach (var info in ConverterRegistry.ListInfo())
        {
            var extensions = new JsonArray();
            foreach (var extension in info.SupportedExtensions)
                extensions.Add((JsonNode)extension);

            converters.Add((JsonNode)new JsonObject
            {
                ["name"] = info.Name,
                ["description"] = info.Description,
                ["supported_extensions"] = extensions,
            });
        }

        var response = new JsonObject
        {
            ["success"] = true,
            ["build_date"] = VersionInfo.BuildDate,
            ["converters"] = converters,
        };
        await SendJsonAsync(ctx, response);
    });

    async Task HandleSubAsync(HttpContext ctx)
    {
        var url = ctx.Request.Query["url"].ToString();
        var target = ctx.Request.Query["target"].ToString();
        var converter = ctx.Request.Query["converter"].ToString();

        var converterName = !string.IsNullOrEmpty(converter) ? converter : (!string.IsNullOrEmpty(target) ? target : "singbox");
        if (converterName.Equals("sing-box", StringComparison.OrdinalIgnoreCase))
            converterName = "singbox";

        if (string.IsNullOrWhiteSpace(url))
        {
            await SendTextAsync(ctx, "Error: missing 'url' query parameter", statusCode: StatusCodes.Status400BadRequest);
            return;
        }

        try
        {
            var contentStr = BaseConverter.ReadInputText(url);
            JsonNode payload;
            if (converterName.Equals("dae", StringComparison.OrdinalIgnoreCase))
            {
                var arr = new JsonArray();
                foreach (var line in contentStr.Split(["\r\n", "\r", "\n"], StringSplitOptions.None))
                    arr.Add((JsonNode)line);
                payload = arr;
            }
            else
            {
                payload = JsonNode.Parse(contentStr) ?? throw new InvalidOperationException("Failed to parse JSON from URL");
            }

            var conv = ConverterRegistry.Get(converterName, config);
            var result = conv.Convert(payload);

            ctx.Response.Headers["Content-Disposition"] = "inline";
            if (result is string text)
            {
                await SendTextAsync(ctx, text, "text/plain; charset=utf-8");
            }
            else if (result is JsonNode resNode)
            {
                await SendTextAsync(ctx, JsonHelper.Serialize(resNode), "application/json; charset=utf-8");
            }
            else
            {
                await SendTextAsync(ctx, "Unsupported result type", statusCode: StatusCodes.Status500InternalServerError);
            }
        }
        catch (Exception ex)
        {
            await SendTextAsync(ctx, $"Conversion failed: {ex.Message}", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    app.MapGet("/sub", HandleSubAsync);
    app.MapGet("/preview", HandleSubAsync);
    app.MapGet("/api/sub", HandleSubAsync);

    app.MapPost("/api/convert", async (HttpContext ctx) =>
    {
        try
        {
            var node = await JsonNode.ParseAsync(ctx.Request.Body);
            if (node is not JsonObject jsonObject)
            {
                ctx.Items["LogError"] = "Expected JSON object";
                await SendJsonAsync(ctx, new JsonObject { ["error"] = "Expected JSON object" }, StatusCodes.Status400BadRequest);
                return;
            }

            // Request format: { "converter": "singbox|dae", "data": ... }
            // Compatible with legacy format: treat entire body as singbox data if no converter key
            var converterName = "singbox";
            var payload = (JsonNode)jsonObject;
            if (jsonObject["converter"] is JsonValue converterValue
                && converterValue.TryGetValue<string>(out var name)
                && !string.IsNullOrEmpty(name))
            {
                converterName = name;
                if (jsonObject["data"] is JsonNode dataNode)
                    payload = dataNode;
                else if (jsonObject["url"] is JsonValue uVal && uVal.TryGetValue<string>(out var uStr) && !string.IsNullOrWhiteSpace(uStr))
                {
                    var contentStr = BaseConverter.ReadInputText(uStr);
                    if (converterName.Equals("dae", StringComparison.OrdinalIgnoreCase))
                    {
                        var arr = new JsonArray();
                        foreach (var line in contentStr.Split(["\r\n", "\r", "\n"], StringSplitOptions.None))
                            arr.Add((JsonNode)line);
                        payload = arr;
                    }
                    else
                    {
                        payload = JsonNode.Parse(contentStr) ?? throw new InvalidOperationException("Failed to parse JSON from URL");
                    }
                }
                else
                {
                    ctx.Items["LogError"] = "Missing 'data' or 'url' field";
                    await SendJsonAsync(ctx, new JsonObject { ["success"] = false, ["error"] = "Missing 'data' or 'url' field" }, StatusCodes.Status400BadRequest);
                    return;
                }
            }

            if (payload is JsonValue pv && pv.TryGetValue<string>(out var ps) && BaseConverter.IsUrl(ps))
            {
                var contentStr = BaseConverter.ReadInputText(ps);
                if (converterName.Equals("dae", StringComparison.OrdinalIgnoreCase))
                {
                    var arr = new JsonArray();
                    foreach (var line in contentStr.Split(["\r\n", "\r", "\n"], StringSplitOptions.None))
                        arr.Add((JsonNode)line);
                    payload = arr;
                }
                else
                {
                    payload = JsonNode.Parse(contentStr) ?? throw new InvalidOperationException("Failed to parse JSON from URL");
                }
            }

            var converter = ConverterRegistry.Get(converterName, config);
            var result = converter.Convert(payload);

            // Count processed nodes
            int? nodeCount = null;
            if (result is JsonObject resultObj && resultObj["outbounds"] is JsonArray outbounds)
            {
                nodeCount = outbounds.Count;
            }
            else if (payload is JsonObject payloadObj && payloadObj["outbounds"] is JsonArray payloadOutbounds)
            {
                nodeCount = payloadOutbounds.Count;
            }

            ctx.Items["LogSummary"] = nodeCount.HasValue
                ? $"{converterName}: {nodeCount} nodes"
                : converterName;

            var successResponse = new JsonObject { ["success"] = true };
            if (result is string text)
            {
                successResponse["format"] = "text";
                successResponse["data"] = text;
            }
            else if (result is JsonNode resultNode)
            {
                successResponse["format"] = "json";
                successResponse["data"] = resultNode;
            }
            else
            {
                ctx.Items["LogError"] = $"Unsupported result type from converter {converterName}";
                await SendJsonAsync(ctx, new JsonObject
                {
                    ["success"] = false,
                    ["error"] = $"Unsupported result type from converter {converterName}",
                }, StatusCodes.Status500InternalServerError);
                return;
            }

            await SendJsonAsync(ctx, successResponse);
        }
        catch (System.Text.Json.JsonException)
        {
            ctx.Items["LogError"] = "JSON parse error";
            await SendJsonAsync(ctx, new JsonObject { ["success"] = false, ["error"] = "JSON parse error" }, StatusCodes.Status400BadRequest);
        }
        catch (KeyNotFoundException e)
        {
            ctx.Items["LogError"] = e.Message;
            await SendJsonAsync(ctx, new JsonObject { ["success"] = false, ["error"] = e.Message }, StatusCodes.Status400BadRequest);
        }
        catch (Exception e)
        {
            ctx.Items["LogError"] = e.Message;
            await SendJsonAsync(ctx, new JsonObject { ["success"] = false, ["error"] = e.Message }, StatusCodes.Status500InternalServerError);
        }
    });

    app.Lifetime.ApplicationStarted.Register(() =>
    {
        Log.Info($"Sonverter (Build {VersionInfo.BuildDate}) ready, listening on http://0.0.0.0:{port}");
        Log.Info($"Working directory: {currentDir}");
        Log.Info($"Available converters: {string.Join(", ", ConverterRegistry.ListNames())}");
    });

    app.Lifetime.ApplicationStopped.Register(() =>
    {
        Log.Info("Sonverter service stopped");
    });

    await app.RunAsync();
}

static void LogRequest(HttpContext context, long elapsedMs, Exception? ex)
{
    var clientIp = GetClientIp(context);
    var method = context.Request.Method;
    var path = context.Request.Path.Value ?? "/";
    var statusCode = ex != null ? 500 : context.Response.StatusCode;

    if (ex is OperationCanceledException)
    {
        Log.Warn($"{clientIp} {method} {path} - Client closed request ({elapsedMs}ms)");
        return;
    }

    var summary = context.Items.TryGetValue("LogSummary", out var s) && s is string summaryStr && !string.IsNullOrEmpty(summaryStr)
        ? $" [{summaryStr}]"
        : "";

    var errorMsg = context.Items.TryGetValue("LogError", out var err) && err is string errStr && !string.IsNullOrEmpty(errStr)
        ? $": {errStr}"
        : "";

    var statusText = GetStatusText(statusCode);
    var line = $"{clientIp} {method} {path}{summary} - {statusCode} {statusText}{errorMsg} ({elapsedMs}ms)";

    if (statusCode >= 500 || ex != null)
    {
        Log.Error(line);
        if (ex != null && Log.IsDebug)
        {
            Log.Debug(ex.ToString());
        }
    }
    else if (statusCode >= 400)
    {
        Log.Warn(line);
    }
    else
    {
        Log.Info(line);
    }
}

static string GetClientIp(HttpContext context)
{
    if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded) && !string.IsNullOrWhiteSpace(forwarded))
    {
        var firstIp = forwarded.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.IsNullOrEmpty(firstIp))
            return CleanIp(firstIp);
    }
    if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIp) && !string.IsNullOrWhiteSpace(realIp))
    {
        return CleanIp(realIp.ToString().Trim());
    }

    var remoteIp = context.Connection.RemoteIpAddress;
    if (remoteIp is null)
        return "-";

    if (remoteIp.IsIPv4MappedToIPv6)
        return remoteIp.MapToIPv4().ToString();

    var ipStr = remoteIp.ToString();
    if (ipStr == "::1")
        return "127.0.0.1";

    return CleanIp(ipStr);
}

static string CleanIp(string ip)
{
    if (ip.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
        return ip[7..];
    if (ip == "::1")
        return "127.0.0.1";
    return ip;
}

static string GetStatusText(int statusCode) => statusCode switch
{
    200 => "OK",
    201 => "Created",
    204 => "No Content",
    301 => "Moved Permanently",
    302 => "Found",
    304 => "Not Modified",
    400 => "Bad Request",
    401 => "Unauthorized",
    403 => "Forbidden",
    404 => "Not Found",
    405 => "Method Not Allowed",
    500 => "Internal Server Error",
    502 => "Bad Gateway",
    503 => "Service Unavailable",
    _ => statusCode.ToString()
};

/// <summary>命令行参数解析（支持 --key value 与 --key=value）。</summary>
internal sealed class CommandLineArgs
{
    public string? Input { get; private set; }
    public string Converter { get; private set; } = "singbox";
    public string? Output { get; private set; }
    public string? Config { get; private set; }
    public string? WorkDir { get; private set; }
    public bool Serve { get; private set; }
    public int Port { get; private set; } = 8080;
    public bool Debug { get; private set; }
    public bool Version { get; private set; }
    public bool ListConverters { get; private set; }
    public bool Help { get; private set; }

    public CommandLineArgs(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var (name, inlineValue) = Split(arg);

            switch (name)
            {
                case "-h" or "--help":
                    Help = true;
                    break;
                case "--list-converters":
                    ListConverters = true;
                    break;
                case "--serve":
                    Serve = true;
                    break;
                case "--debug":
                    Debug = true;
                    break;
                case "-v" or "--version":
                    Version = true;
                    break;
                case "-w" or "--workdir" or "--dir":
                    WorkDir = inlineValue ?? NextValue(args, ref i, name);
                    break;
                case "-i" or "--input":
                    Input = inlineValue ?? NextValue(args, ref i, name);
                    break;
                case "-c" or "--converter":
                    Converter = inlineValue ?? NextValue(args, ref i, name) ?? Converter;
                    break;
                case "-o" or "--output":
                    Output = inlineValue ?? NextValue(args, ref i, name);
                    break;
                case "-f" or "--config":
                    Config = inlineValue ?? NextValue(args, ref i, name);
                    break;
                case "-p" or "--port":
                    var value = inlineValue ?? NextValue(args, ref i, name);
                    if (int.TryParse(value, out var port))
                        Port = port;
                    break;
            }
        }
    }

    private static (string Name, string? Value) Split(string arg)
    {
        var eq = arg.IndexOf('=');
        return eq >= 0
            ? (arg[..eq], arg[(eq + 1)..])
            : (arg, null);
    }

    private static string? NextValue(string[] args, ref int i, string name)
    {
        if (i + 1 >= args.Length)
        {
            Log.Error($"Missing value for argument {name}");
            return null;
        }
        return args[++i];
    }
}
