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
    Console.WriteLine("可用的转换器:");
    foreach (var info in ConverterRegistry.ListInfo())
    {
        Console.WriteLine($"\n  {info.Name}: {info.Description}");
        Console.WriteLine($"    支持的文件格式：{string.Join(", ", info.SupportedExtensions)}");
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
    Console.Error.WriteLine("\n错误：请指定输入文件 (--input)");
    Environment.ExitCode = 1;
    return;
}

try
{
    var config = DefaultConfig.Load(cliArgs.Config);
    var converter = ConverterRegistry.Get(cliArgs.Converter, config);
    var outputPath = converter.ConvertFile(cliArgs.Input, cliArgs.Output);
    Console.WriteLine($"✅ 转换文件：{outputPath}");
}
catch (Exception e)
{
    Console.Error.WriteLine($"错误：{e}");
    Environment.ExitCode = 1;
}

return;

static void PrintHelp()
{
    Console.WriteLine("""
        出口节点转换工具

        用法:
          --input, -i <文件>        输入文件
          --converter, -c <名称>    转换器名称 (默认: singbox, 可用: singbox, dae, example)
          --output, -o <文件>       输出文件路径
          --config, -f <文件>       配置文件 (默认: template.json)
          --serve                   启动 Web 服务
          --port, -p <端口>         Web 服务端口 (默认: 8080)
          --debug                   调试模式
          --version, -v             显示版本与编译信息
          --list-converters         列出所有可用的转换器
          --help, -h                显示帮助

        示例:
          dotnet run -- --input input_example.json
          dotnet run -- --input nodes.txt --converter dae
          dotnet run -- --serve --port 8080
        """);
}

static async Task RunServerAsync(int port, bool debug, string? configPath)
{
    var config = DefaultConfig.Load(configPath);

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = [],
        ContentRootPath = AppContext.BaseDirectory,
    });
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    });

    var app = builder.Build();

    // 前端资源已编译进程序集（嵌入式资源），直接从程序集内提供静态文件
    var embeddedFileProvider = new EmbeddedFileProvider(typeof(Program).Assembly, "Sonverter.wwwroot");
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = embeddedFileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = embeddedFileProvider });

    app.MapGet("/api/health", () =>
        Results.Text(JsonHelper.Serialize(new JsonObject { ["status"] = "ok" }), "application/json"));

    app.MapGet("/api/version", () =>
        Results.Text(JsonHelper.Serialize(new JsonObject
        {
            ["name"] = VersionInfo.ProductName,
            ["version"] = VersionInfo.Version,
            ["build_time"] = VersionInfo.BuildTimestamp,
            ["dotnet"] = VersionInfo.DotnetVersion,
            ["runtime"] = VersionInfo.Runtime,
            ["platform"] = VersionInfo.Platform,
            ["process_architecture"] = VersionInfo.ProcessArchitecture,
        }), "application/json"));

    app.MapGet("/api/converters", () =>
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
            ["converters"] = converters,
        };
        return Results.Text(JsonHelper.Serialize(response), "application/json");
    });

    app.MapPost("/api/convert", async (HttpContext context) =>
    {
        try
        {
            var node = await JsonNode.ParseAsync(context.Request.Body);
            if (node is not JsonObject jsonObject)
                return Results.Text(
                    JsonHelper.Serialize(new JsonObject { ["error"] = "请提供 JSON 数据" }),
                    "application/json",
                    statusCode: StatusCodes.Status400BadRequest);

            // 请求格式：{ "converter": "singbox|dae", "data": ... }
            // 兼容旧格式：没有 converter 字段时整个 body 视为 singbox 数据
            var converterName = "singbox";
            var payload = (JsonNode)jsonObject;
            if (jsonObject["converter"] is JsonValue converterValue
                && converterValue.TryGetValue<string>(out var name)
                && !string.IsNullOrEmpty(name))
            {
                converterName = name;
                if (jsonObject["data"] is JsonNode dataNode)
                    payload = dataNode;
                else
                    return Results.Text(
                        JsonHelper.Serialize(new JsonObject { ["success"] = false, ["error"] = "缺少 data 字段" }),
                        "application/json",
                        statusCode: StatusCodes.Status400BadRequest);
            }

            var converter = ConverterRegistry.Get(converterName, config);
            var result = converter.Convert(payload);

            var successResponse = new JsonObject { ["success"] = true };
            if (result is string text)
            {
                // 文本类转换器（如 dae）：直接返回配置文本
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
                return Results.Text(
                    JsonHelper.Serialize(new JsonObject
                    {
                        ["success"] = false,
                        ["error"] = $"转换器 {converterName} 返回了不支持的结果类型",
                    }),
                    "application/json",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Text(JsonHelper.Serialize(successResponse), "application/json");
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.Text(
                JsonHelper.Serialize(new JsonObject { ["success"] = false, ["error"] = "JSON 解析失败" }),
                "application/json",
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (KeyNotFoundException e)
        {
            return Results.Text(
                JsonHelper.Serialize(new JsonObject { ["success"] = false, ["error"] = e.Message }),
                "application/json",
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (Exception e)
        {
            return Results.Text(
                JsonHelper.Serialize(new JsonObject { ["success"] = false, ["error"] = e.Message }),
                "application/json",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    });

    Console.WriteLine("🚀 启动 Web 服务器...");
    Console.WriteLine($"📦 Version: {VersionInfo.Version} | Build time: {VersionInfo.BuildTimestamp}");
    Console.WriteLine($"🧩 .NET: {VersionInfo.DotnetVersion} | Platform: {VersionInfo.Platform}");
    Console.WriteLine($"📡 访问地址：http://localhost:{port}");
    Console.WriteLine($"📁 资源目录：{AppContext.BaseDirectory}");
    Console.WriteLine($"🔧 可用转换器：{string.Join(", ", ConverterRegistry.ListNames())}");
    Console.WriteLine("按 Ctrl+C 停止服务");

    await app.RunAsync();
}

/// <summary>命令行参数解析（支持 --key value 与 --key=value）。</summary>
internal sealed class CommandLineArgs
{
    public string? Input { get; private set; }
    public string Converter { get; private set; } = "singbox";
    public string? Output { get; private set; }
    public string? Config { get; private set; }
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
            Console.Error.WriteLine($"错误：参数 {name} 缺少值");
            return null;
        }
        return args[++i];
    }
}
