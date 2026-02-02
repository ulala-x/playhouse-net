using System.CommandLine;
using PlayHouse.Benchmark.SS.Client;
using PlayHouse.Benchmark.SS.Shared.Proto;
using Serilog;

var serverOption = new Option<string>("--server", () => "127.0.0.1:16110");
var ccuOption = new Option<int>("--ccu", () => 100);
var durationOption = new Option<int>("--duration", () => 10);
var messageSizeOption = new Option<int>("--message-size", () => 1024);
var modeOption = new Option<string>("--mode", () => "request-async");
var inflightOption = new Option<int>("--inflight", () => 100);
var warmupOption = new Option<int>("--warmup", () => 3);

// Unused options to maintain compatibility with run-single.sh
var testModeOption = new Option<string>("--test-mode", () => "ss-echo");
var responseSizeOption = new Option<string>("--response-size", () => "");
var httpPortOption = new Option<int>("--http-port", () => 5080);
var apiHttpPortOption = new Option<int>("--api-http-port", () => 5081);
var outputDirOption = new Option<string>("--output-dir", () => "");
var stageIdOption = new Option<long>("--stage-id", () => 1000);
var targetStageIdOption = new Option<long>("--target-stage-id", () => 2000);
var targetNidOption = new Option<string>("--target-nid", () => "play-2");
var labelOption = new Option<string>("--label", () => "");
var callTypeOption = new Option<string>("--call-type", () => "stage-to-api");

var rootCommand = new RootCommand("PlayHouse S2S Benchmark")
{
    serverOption, ccuOption, durationOption, messageSizeOption, modeOption, inflightOption, warmupOption,
    testModeOption, responseSizeOption, httpPortOption, apiHttpPortOption, outputDirOption,
    stageIdOption, targetStageIdOption, targetNidOption, labelOption, callTypeOption
};

rootCommand.SetHandler(async (context) =>
{
    var server = context.ParseResult.GetValueForOption(serverOption)!;
    var ccu = context.ParseResult.GetValueForOption(ccuOption);
    var duration = context.ParseResult.GetValueForOption(durationOption);
    var messageSize = context.ParseResult.GetValueForOption(messageSizeOption);
    var modeStr = context.ParseResult.GetValueForOption(modeOption)!;
    var inflight = context.ParseResult.GetValueForOption(inflightOption);
    var warmup = context.ParseResult.GetValueForOption(warmupOption);

    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    var commMode = modeStr.ToLower() switch {
        "send" => SSCommMode.Send,
        "request-callback" => SSCommMode.RequestCallback,
        _ => SSCommMode.RequestAsync
    };

    var parts = server.Split(':');
    var runner = new SSEchoBenchmarkRunner(
        parts[0],
        int.Parse(parts[1]),
        ccu,
        messageSize,
        commMode,
        duration,
        inflight,
        warmup);

    await runner.RunAsync();
});

return await rootCommand.InvokeAsync(args);
