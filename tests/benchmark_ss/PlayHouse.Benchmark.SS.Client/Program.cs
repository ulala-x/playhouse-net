using System.CommandLine;
using PlayHouse.Benchmark.SS.Client;
using PlayHouse.Benchmark.SS.Shared.Proto;
using Serilog;

var serverOption = new Option<string>("--server", () => "127.0.0.1:16110");
var connectionsOption = new Option<int>("--connections", () => 100);
var durationOption = new Option<int>("--duration", () => 10);
var messageSizeOption = new Option<int>("--message-size", () => 1024);
var commModeOption = new Option<string>("--comm-mode", () => "request-async");
var maxInFlightOption = new Option<int>("--max-inflight", () => 100);

// Unused options to maintain compatibility with run-single.sh
var modeOption = new Option<string>("--mode", () => "ss-echo");
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
    serverOption, connectionsOption, durationOption, messageSizeOption, commModeOption, maxInFlightOption,
    modeOption, responseSizeOption, httpPortOption, apiHttpPortOption, outputDirOption,
    stageIdOption, targetStageIdOption, targetNidOption, labelOption, callTypeOption
};

rootCommand.SetHandler(async (context) =>
{
    var server = context.ParseResult.GetValueForOption(serverOption)!;
    var connections = context.ParseResult.GetValueForOption(connectionsOption);
    var duration = context.ParseResult.GetValueForOption(durationOption);
    var messageSize = context.ParseResult.GetValueForOption(messageSizeOption);
    var commModeStr = context.ParseResult.GetValueForOption(commModeOption)!;
    var maxInFlight = context.ParseResult.GetValueForOption(maxInFlightOption);

    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    var commMode = commModeStr.ToLower() switch {
        "send" => SSCommMode.Send,
        "request-callback" => SSCommMode.RequestCallback,
        _ => SSCommMode.RequestAsync
    };

    var parts = server.Split(':');
    var runner = new SSEchoBenchmarkRunner(
        parts[0], 
        int.Parse(parts[1]), 
        connections, 
        messageSize, 
        commMode, 
        duration, 
        maxInFlight);
        
    await runner.RunAsync();
});

return await rootCommand.InvokeAsync(args);
