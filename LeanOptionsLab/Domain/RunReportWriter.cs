using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LeanOptionsLab.Domain;

public sealed class RecordedLifecycleEvent
{
    public DateTimeOffset? OccurredAtUtc { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed class RunReport
{
    public string SchemaVersion { get; init; } = "1.0";
    public string RunId { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public string CodeVersion { get; init; } = "unknown";
    public ExperimentConfiguration Experiment { get; init; } = new();
    public DataReadinessDecision DataReadiness { get; init; } = new(false, Array.Empty<string>());
    public RankingDecision RankingAssessment { get; init; } = RankingDecision.NotRankable("No ranking assessment was supplied.");
    public ComparisonStatus FinalStatus { get; init; }
    public IReadOnlyList<string> FinalReasons { get; init; } = Array.Empty<string>();
    public IReadOnlyList<RecordedLifecycleEvent> OrderEvents { get; init; } = Array.Empty<RecordedLifecycleEvent>();
    public IReadOnlyList<RecordedLifecycleEvent> AssignmentEvents { get; init; } = Array.Empty<RecordedLifecycleEvent>();
    public IReadOnlyList<RecordedLifecycleEvent> ExerciseEvents { get; init; } = Array.Empty<RecordedLifecycleEvent>();
}

public sealed record RunArtifactPaths(string Directory, string JsonPath, string MarkdownPath);

public static class RunReportFactory
{
    public static RunReport Create(
        string runId,
        string codeVersion,
        ExperimentConfiguration configuration,
        DataReadinessEvidence dataEvidence,
        IReadOnlyList<StrategyEvaluation> evaluations,
        IReadOnlyList<RecordedLifecycleEvent>? orderEvents = null,
        IReadOnlyList<RecordedLifecycleEvent>? assignmentEvents = null,
        IReadOnlyList<RecordedLifecycleEvent>? exerciseEvents = null)
    {
        var dataReadiness = DataReadinessGate.Evaluate(dataEvidence);
        var rankingAssessment = ComparisonRankingService.Rank(configuration, evaluations);
        var finalStatus = dataReadiness.IsReady
            ? rankingAssessment.Status
            : ComparisonStatus.InvalidData;
        var finalReasons = dataReadiness.IsReady
            ? rankingAssessment.Reasons
            : dataReadiness.Reasons;

        return new RunReport
        {
            RunId = runId,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            CodeVersion = codeVersion,
            Experiment = configuration,
            DataReadiness = dataReadiness,
            RankingAssessment = rankingAssessment,
            FinalStatus = finalStatus,
            FinalReasons = finalReasons,
            OrderEvents = orderEvents ?? Array.Empty<RecordedLifecycleEvent>(),
            AssignmentEvents = assignmentEvents ?? Array.Empty<RecordedLifecycleEvent>(),
            ExerciseEvents = exerciseEvents ?? Array.Empty<RecordedLifecycleEvent>()
        };
    }
}

public static class RunReportWriter
{
    public static RunArtifactPaths Write(string outputRoot, RunReport report)
    {
        ValidateRunId(report.RunId);
        var runDirectory = Path.Combine(outputRoot, report.RunId);
        Directory.CreateDirectory(runDirectory);

        var jsonPath = Path.Combine(runDirectory, "comparison-report.json");
        var markdownPath = Path.Combine(runDirectory, "comparison-report.ar.md");

        File.WriteAllText(jsonPath, ExperimentConfigurationJson.Serialize(report), new UTF8Encoding(false));
        File.WriteAllText(markdownPath, CreateArabicMarkdown(report), new UTF8Encoding(false));

        return new(runDirectory, jsonPath, markdownPath);
    }

    public static string CreateArabicMarkdown(RunReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# تقرير مختبر الأوبشن المحلي — {Escape(report.RunId)}");
        builder.AppendLine();
        builder.AppendLine($"- وقت إنشاء التقرير (UTC): `{report.GeneratedAtUtc:O}`");
        builder.AppendLine($"- نسخة الكود: `{Escape(report.CodeVersion)}`");
        builder.AppendLine($"- الحالة النهائية: **{ArabicStatus(report.FinalStatus)}** (`{StatusToken(report.FinalStatus)}`)");
        builder.AppendLine();
        builder.AppendLine("## إعداد التجربة");
        builder.AppendLine();
        builder.AppendLine($"- الأصل: `{Escape(report.Experiment.Underlying)}`");
        builder.AppendLine($"- الدقة: `{Escape(report.Experiment.Resolution)}`");
        builder.AppendLine($"- المدة: `{report.Experiment.StartDate:yyyy-MM-dd}` إلى `{report.Experiment.EndDate:yyyy-MM-dd}`");
        builder.AppendLine($"- التدريب: `{report.Experiment.Windows.Training.StartDate:yyyy-MM-dd}` إلى `{report.Experiment.Windows.Training.EndDate:yyyy-MM-dd}`");
        builder.AppendLine($"- التحقق: `{report.Experiment.Windows.Validation.StartDate:yyyy-MM-dd}` إلى `{report.Experiment.Windows.Validation.EndDate:yyyy-MM-dd}`");
        builder.AppendLine($"- خارج العينة: `{report.Experiment.Windows.OutOfSample.StartDate:yyyy-MM-dd}` إلى `{report.Experiment.Windows.OutOfSample.EndDate:yyyy-MM-dd}`");
        builder.AppendLine($"- التداول الحي: `{report.Experiment.EnableLiveTrading}`؛ التداول الورقي: `{report.Experiment.EnablePaperTrading}`");
        builder.AppendLine();
        builder.AppendLine("## تحقق البيانات");
        builder.AppendLine();
        builder.AppendLine($"- جاهزية البيانات: **{(report.DataReadiness.IsReady ? "مكتملة" : "غير مكتملة")}**");
        AppendReasons(builder, report.DataReadiness.Reasons, "لا توجد ملاحظات على تحقق البيانات.");
        builder.AppendLine();
        builder.AppendLine("## قرار الترتيب");
        builder.AppendLine();
        builder.AppendLine($"- تقييم الترتيب: **{ArabicStatus(report.RankingAssessment.Status)}** (`{StatusToken(report.RankingAssessment.Status)}`)");
        AppendReasons(builder, report.RankingAssessment.Reasons, "لا توجد أسباب حجب مسجلة.");

        if (report.RankingAssessment.RankedStrategies.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("| الترتيب | الاستراتيجية | العائد المعدل بالمخاطر خارج العينة | أقصى هبوط خارج العينة |");
            builder.AppendLine("| ---: | --- | ---: | ---: |");

            for (var index = 0; index < report.RankingAssessment.RankedStrategies.Count; index++)
            {
                var item = report.RankingAssessment.RankedStrategies[index];
                builder.AppendLine($"| {index + 1} | {Escape(item.TemplateName)} | {item.OutOfSampleRiskAdjustedReturn:0.####} | {item.OutOfSampleMaxDrawdown:0.####} |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## أحداث دورة العقد في LEAN");
        builder.AppendLine();
        AppendEvents(builder, "أحداث الأوامر", report.OrderEvents);
        AppendEvents(builder, "أحداث Assignment", report.AssignmentEvents);
        AppendEvents(builder, "أحداث Exercise", report.ExerciseEvents);
        builder.AppendLine();
        builder.AppendLine("## ملاحظة حدود الاستخدام");
        builder.AppendLine();
        builder.AppendLine("هذا المختبر أداة بحث وتدقيق محلية فقط. لا يشغّل وسيطاً أو Paper Trading أو أوامر حقيقية، ولا يقدم توصية مالية أو تنبؤاً مضموناً.");

        return builder.ToString();
    }

    private static void AppendReasons(StringBuilder builder, IReadOnlyList<string> reasons, string emptyMessage)
    {
        if (reasons.Count == 0)
        {
            builder.AppendLine($"- {emptyMessage}");
            return;
        }

        foreach (var reason in reasons)
        {
            builder.AppendLine($"- {Escape(reason)}");
        }
    }

    private static void AppendEvents(
        StringBuilder builder,
        string title,
        IReadOnlyList<RecordedLifecycleEvent> events)
    {
        builder.AppendLine($"### {title}");
        builder.AppendLine();

        if (events.Count == 0)
        {
            builder.AppendLine("- لا توجد أحداث مسجلة.");
            builder.AppendLine();
            return;
        }

        foreach (var item in events.OrderBy(item => item.OccurredAtUtc ?? DateTimeOffset.MaxValue))
        {
            var timestamp = item.OccurredAtUtc?.ToString("O") ?? "غير متاح في السجل";
            builder.AppendLine($"- `{timestamp}` — `{Escape(item.Kind)}` — {Escape(item.Detail)}");
        }

        builder.AppendLine();
    }

    private static string StatusToken(ComparisonStatus status) => ComparisonStatusTokens.ToToken(status);

    private static string ArabicStatus(ComparisonStatus status) => status switch
    {
        ComparisonStatus.Ranked => "قابل للترتيب",
        ComparisonStatus.NotRankable => "غير مؤهل للترتيب",
        ComparisonStatus.InvalidData => "بيانات غير صالحة أو غير مكتملة",
        _ => "غير مؤهل للترتيب"
    };

    private static string Escape(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static void ValidateRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId)
            || runId.Contains("..", StringComparison.Ordinal)
            || runId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || runId.Contains(Path.DirectorySeparatorChar)
            || runId.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Run ID must be a simple directory name.", nameof(runId));
        }
    }
}
