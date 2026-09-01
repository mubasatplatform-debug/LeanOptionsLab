using System.Text.Json;
using LeanOptionsLab.Domain;

namespace LeanOptionsLab.Tooling;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                WriteUsage();
                return 64;
            }

            var options = ParseOptions(args.Skip(1).ToArray());
            return args[0] switch
            {
                "validate" => Validate(options),
                "write-report" => WriteReport(options),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            return 1;
        }
    }

    private static int Validate(IReadOnlyDictionary<string, string> options)
    {
        var configuration = ExperimentConfigurationJson.Load(Required(options, "--config"));
        var result = ExperimentConfigurationValidator.Validate(configuration);

        if (result.IsValid)
        {
            Console.WriteLine("Configuration layout is valid.");
            return 0;
        }

        foreach (var issue in result.Issues)
        {
            Console.Error.WriteLine($"{issue.Code}: {issue.Message}");
        }

        return 2;
    }

    private static int WriteReport(IReadOnlyDictionary<string, string> options)
    {
        var configuration = ExperimentConfigurationJson.Load(Required(options, "--config"));
        var dataEvidence = LoadOptional<DataReadinessEvidence>(options, "--data-evidence")
            ?? new DataReadinessEvidence
            {
                FailureMessages = new()
                {
                    "No audited data-readiness evidence was supplied for this run."
                }
            };
        var evaluations = LoadOptional<List<StrategyEvaluation>>(options, "--evaluations")
            ?? new List<StrategyEvaluation>();
        var extractedEvents = Optional(options, "--lean-log") is { } leanLogPath
            ? LeanLogEventExtractor.Extract(leanLogPath)
            : null;
        var orderEvents = extractedEvents?.OrderEvents
            ?? LoadOptional<List<RecordedLifecycleEvent>>(options, "--order-events");
        var assignmentEvents = extractedEvents?.AssignmentEvents
            ?? LoadOptional<List<RecordedLifecycleEvent>>(options, "--assignment-events");
        var exerciseEvents = extractedEvents?.ExerciseEvents
            ?? LoadOptional<List<RecordedLifecycleEvent>>(options, "--exercise-events");

        var report = RunReportFactory.Create(
            Required(options, "--run-id"),
            Optional(options, "--code-version") ?? "unknown",
            configuration,
            dataEvidence,
            evaluations,
            orderEvents,
            assignmentEvents,
            exerciseEvents);
        var paths = RunReportWriter.Write(Required(options, "--output-root"), report);

        Console.WriteLine($"status={ComparisonStatusTokens.ToToken(report.FinalStatus)}");
        Console.WriteLine($"json={paths.JsonPath}");
        Console.WriteLine($"markdown={paths.MarkdownPath}");
        return 0;
    }

    private static T? LoadOptional<T>(
        IReadOnlyDictionary<string, string> options,
        string name)
    {
        var path = Optional(options, name);
        if (path is null)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), ExperimentConfigurationJson.Options)
            ?? throw new InvalidDataException($"'{path}' is empty or invalid.");
    }

    private static IReadOnlyDictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal)
                || index + 1 >= args.Length
                || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Options must be supplied as --name value pairs.");
            }

            options.Add(args[index], args[index + 1]);
        }

        return options;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string name) =>
        Optional(options, name)
        ?? throw new ArgumentException($"Missing required option {name}.");

    private static string? Optional(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) ? value : null;

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        WriteUsage();
        return 64;
    }

    private static void WriteUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  validate --config <experiment.json>");
        Console.WriteLine("  write-report --config <experiment.json> --output-root <results> --run-id <id> [--code-version <version>] [--data-evidence <json>] [--evaluations <json>] [--lean-log <log>] [--order-events <json>] [--assignment-events <json>] [--exercise-events <json>]");
    }
}
