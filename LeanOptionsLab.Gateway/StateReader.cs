using System.Net;
using System.Text.Json;
using LeanOptionsLab.Domain;

namespace LeanOptionsLab.Gateway;

public enum GatewayCommandKind
{
    Serve,
    HealthProbe,
    Invalid
}

public readonly record struct GatewayCommand(GatewayCommandKind Kind);

public static class GatewayCommandLine
{
    public static GatewayCommand Parse(IReadOnlyList<string> arguments) => arguments.Count switch
    {
        0 => new(GatewayCommandKind.Serve),
        1 when string.Equals(arguments[0], "--health-probe", StringComparison.Ordinal) =>
            new(GatewayCommandKind.HealthProbe),
        _ => new(GatewayCommandKind.Invalid)
    };
}

public static class GatewayHealthProbe
{
    public static readonly Uri Endpoint = new("http://127.0.0.1:8080/healthz", UriKind.Absolute);

    public static async Task<int> RunAsync(
        HttpClient? client = null,
        CancellationToken cancellationToken = default)
    {
        var ownsClient = client is null;
        client ??= new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        try
        {
            using var response = await client.GetAsync(Endpoint, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (OperationCanceledException)
        {
            return 1;
        }
        finally
        {
            if (ownsClient)
            {
                client.Dispose();
            }
        }
    }
}

public sealed record GatewayStatus(
    string RunId,
    DateTimeOffset GeneratedAtUtc,
    string CodeVersion,
    string FinalStatus,
    string RankingStatus,
    bool DataReady,
    bool ExperimentReady,
    bool LiveTrading,
    bool PaperTrading,
    int OrderEventCount,
    int AssignmentEventCount,
    int ExerciseEventCount,
    int WriteEndpointCount,
    int TriggerEndpointCount);

public sealed class GatewayStateReader
{
    private const long MaximumReportBytes = 1024 * 1024;
    private readonly string _resultsRoot;

    public GatewayStateReader(string resultsRoot)
    {
        _resultsRoot = Path.GetFullPath(
            string.IsNullOrWhiteSpace(resultsRoot)
                ? throw new ArgumentException("A results root is required.", nameof(resultsRoot))
                : resultsRoot);
    }

    public bool TryReadLatest(out GatewayStatus? status)
    {
        status = null;

        try
        {
            var reportPath = FindLatestReportPath();
            if (reportPath is null)
            {
                return false;
            }

            var reportFile = new FileInfo(reportPath);
            if (reportFile.Length is <= 0 or > MaximumReportBytes)
            {
                return false;
            }

            using var stream = new FileStream(
                reportPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            var report = JsonSerializer.Deserialize<RunReport>(stream, ExperimentConfigurationJson.Options);
            if (!IsValid(report, reportFile.Directory?.Name))
            {
                return false;
            }

            status = new GatewayStatus(
                report!.RunId,
                report.GeneratedAtUtc,
                report.CodeVersion,
                ComparisonStatusTokens.ToToken(report.FinalStatus),
                ComparisonStatusTokens.ToToken(report.RankingAssessment.Status),
                report.DataReadiness.IsReady,
                report.FinalStatus == ComparisonStatus.Ranked
                    && report.DataReadiness.IsReady
                    && report.RankingAssessment.Status == ComparisonStatus.Ranked,
                LiveTrading: false,
                PaperTrading: false,
                report.OrderEvents.Count,
                report.AssignmentEvents.Count,
                report.ExerciseEvents.Count,
                WriteEndpointCount: 0,
                TriggerEndpointCount: 0);
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException)
        {
            return false;
        }
    }

    private string? FindLatestReportPath()
    {
        var root = new DirectoryInfo(_resultsRoot);
        if (!root.Exists || root.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return null;
        }

        var rootPrefix = root.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return root.EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
            .Where(directory => !directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .Select(directory => new FileInfo(Path.Combine(directory.FullName, "comparison-report.json")))
            .Where(file => file.Exists && !file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .Where(file => file.FullName.StartsWith(rootPrefix, pathComparison))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.FullName, StringComparer.Ordinal)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    private static bool IsValid(RunReport? report, string? containingDirectory)
    {
        if (report is null
            || report.SchemaVersion != "1.0"
            || report.GeneratedAtUtc == default
            || string.IsNullOrWhiteSpace(report.RunId)
            || !string.Equals(report.RunId, containingDirectory, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(report.CodeVersion)
            || report.Experiment is null
            || report.DataReadiness is null
            || report.RankingAssessment is null
            || report.OrderEvents is null
            || report.AssignmentEvents is null
            || report.ExerciseEvents is null
            || report.Experiment.EnableLiveTrading
            || report.Experiment.EnablePaperTrading)
        {
            return false;
        }

        var configurationValidation = ExperimentConfigurationValidator.Validate(report.Experiment);
        if (!configurationValidation.IsValid)
        {
            return false;
        }

        var expectedFinalStatus = report.DataReadiness.IsReady
            ? report.RankingAssessment.Status
            : ComparisonStatus.InvalidData;
        return report.FinalStatus == expectedFinalStatus;
    }
}
