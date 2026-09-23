using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.Engines;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class CertificationSuite
{
    private readonly List<VideoTestCase> _testCases;
    private readonly ISpeechToTextEngine _engine;
    private readonly string _modelName;
    private readonly string _language;
    private readonly string? _tempPath;

    public CertificationSuite(
        IEnumerable<VideoTestCase> testCases,
        ISpeechToTextEngine engine,
        string modelName,
        string language,
        string? tempPath = null)
    {
        _testCases = testCases.ToList();
        _engine = engine;
        _modelName = modelName;
        _language = language;
        _tempPath = tempPath;
    }

    public async Task<SuiteReport> RunAsync(CancellationToken ct = default)
    {
        var results = new List<VideoCertReport>();
        var startTime = DateTime.Now;

        foreach (var testCase in _testCases)
        {
            if (ct.IsCancellationRequested) break;

            Console.WriteLine($"Testing: {testCase.VideoPath} ({testCase.VideoType})");

            try
            {
                using var harness = new CertificationHarness(
                    testCase.VideoPath,
                    testCase.VideoType,
                    _engine,
                    _modelName,
                    _language,
                    testCase.AudioTrackIndex,
                    _tempPath);

                var result = await harness.RunCertificationAsync(ct);
                results.Add(result);

                Console.WriteLine($"  Result: {(result.AllPassed ? "PASS" : result.AnyFailed ? "FAIL" : "WARNING")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error: {ex.Message}");
                results.Add(new VideoCertReport
                {
                    VideoPath = testCase.VideoPath,
                    Type = testCase.VideoType,
                    TimestampResult = new CertificationResult("Timestamp", CertificationStatus.Failed, ex.Message, new Dictionary<string, object>())
                });
            }
        }

        return new SuiteReport(results, DateTime.Now - startTime);
    }

    public static SuiteReport RunQuickSmokeTest(
        string videoPath,
        ISpeechToTextEngine engine,
        string modelName,
        string language,
        string? tempPath = null)
    {
        var testCases = new List<VideoTestCase>
        {
            new(videoPath, VideoType.SingleSpeaker)
        };

        var suite = new CertificationSuite(testCases, engine, modelName, language, tempPath);
        return suite.RunAsync().GetAwaiter().GetResult();
    }
}

public record VideoTestCase(
    string VideoPath,
    VideoType VideoType,
    int AudioTrackIndex = 0);

public sealed class SuiteReport
{
    public IReadOnlyList<VideoCertReport> Results { get; }
    public TimeSpan TotalDuration { get; }
    public DateTime CompletedAt { get; }

    public int TotalCount => Results.Count;
    public int PassedCount => Results.Count(r => r.AllPassed);
    public int FailedCount => Results.Count(r => r.AnyFailed);
    public int WarningCount => Results.Count(r => !r.AllPassed && !r.AnyFailed);

    public bool AllPassed => FailedCount == 0 && PassedCount > 0;

    public SuiteReport(IReadOnlyList<VideoCertReport> results, TimeSpan totalDuration)
    {
        Results = results;
        TotalDuration = totalDuration;
        CompletedAt = DateTime.Now;
    }

    public string GetSummary()
    {
        var lines = new List<string>
        {
            "",
            "========================================",
            "     CERTIFICATION SUITE REPORT",
            "========================================",
            "",
            $"Completed: {CompletedAt:yyyy-MM-dd HH:mm:ss}",
            $"Duration:  {TotalDuration:mm\\:ss\\.fff}",
            "",
            "----------------------------------------",
            "SUMMARY",
            "----------------------------------------",
            $"Total:     {TotalCount}",
            $"Passed:    {PassedCount} ✅",
            $"Failed:    {FailedCount} ❌",
            $"Warnings:  {WarningCount} ⚠️",
            "",
            "----------------------------------------",
            "DETAILED RESULTS",
            "----------------------------------------"
        };

        foreach (var result in Results)
        {
            lines.Add("");
            lines.Add(result.GetSummary());
        }

        lines.Add("");
        lines.Add("========================================");
        lines.Add($"OVERALL: {(AllPassed ? "CERTIFIED ✅" : "NOT CERTIFIED ❌")}");
        lines.Add("========================================");
        lines.Add("");

        return string.Join(Environment.NewLine, lines);
    }

    public void SaveToFile(string path)
    {
        var content = GetSummary();
        File.WriteAllText(path, content);
        Console.WriteLine($"Report saved to: {path}");
    }

    public void SaveJsonToFile(string path)
    {
        var report = new CertificationJsonReport(this);
        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };
        var json = System.Text.Json.JsonSerializer.Serialize(report, options);
        File.WriteAllText(path, json);
        Console.WriteLine($"JSON report saved to: {path}");
    }
}

public sealed class CertificationJsonReport
{
    public DateTime CompletedAt { get; set; }
    public string TotalDuration { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public int WarningCount { get; set; }
    public bool AllPassed { get; set; }
    public List<VideoCertJsonReport> Results { get; set; } = new();

    public CertificationJsonReport(SuiteReport report)
    {
        CompletedAt = report.CompletedAt;
        TotalDuration = report.TotalDuration.ToString(@"hh\:mm\:ss\.fff");
        TotalCount = report.TotalCount;
        PassedCount = report.PassedCount;
        FailedCount = report.FailedCount;
        WarningCount = report.WarningCount;
        AllPassed = report.AllPassed;

        foreach (var result in report.Results)
        {
            Results.Add(new VideoCertJsonReport(result));
        }
    }
}

public sealed class VideoCertJsonReport
{
    public string VideoPath { get; set; } = string.Empty;
    public string VideoName { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public string VideoType { get; set; } = string.Empty;
    public bool AllPassed { get; set; }
    public bool AnyFailed { get; set; }
    public int? LegacySubtitleCount { get; set; }
    public int? ChunkedSubtitleCount { get; set; }
    public double? LegacyRuntimeSeconds { get; set; }
    public double? ChunkedRuntimeSeconds { get; set; }
    public TimestampMetrics? TimestampMetrics { get; set; }
    public QualityMetrics? QualityMetrics { get; set; }
    public MemoryMetrics? MemoryMetrics { get; set; }
    public CleanupMetrics? CleanupMetrics { get; set; }

    public VideoCertJsonReport(VideoCertReport report)
    {
        VideoPath = report.VideoPath;
        VideoName = report.VideoName;
        DurationSeconds = report.VideoDuration.TotalSeconds;
        VideoType = report.Type.ToString();
        AllPassed = report.AllPassed;
        AnyFailed = report.AnyFailed;

        if (report.LegacySubtitle != null)
            LegacySubtitleCount = report.LegacySubtitle.Paragraphs.Count;
        if (report.ChunkedSubtitle != null)
            ChunkedSubtitleCount = report.ChunkedSubtitle.Paragraphs.Count;

        if (report.LegacyRuntime != null)
            LegacyRuntimeSeconds = report.LegacyRuntime.Duration.TotalSeconds;
        if (report.ChunkedRuntime != null)
            ChunkedRuntimeSeconds = report.ChunkedRuntime.Duration.TotalSeconds;

        if (report.TimestampResult.Status == CertificationStatus.Passed ||
            report.TimestampResult.Status == CertificationStatus.Warning)
        {
            TimestampMetrics = new TimestampMetrics
            {
                TotalSubtitles = GetIntDetail(report.TimestampResult, "TotalSubtitles") ?? 0,
                Overlaps = GetIntDetail(report.TimestampResult, "Overlaps") ?? 0,
                Gaps = GetIntDetail(report.TimestampResult, "Gaps") ?? 0,
                OutOfDuration = GetIntDetail(report.TimestampResult, "OutOfDuration") ?? 0
            };
        }

        if (report.MemoryResult.Status == CertificationStatus.Passed ||
            report.MemoryResult.Status == CertificationStatus.Warning)
        {
            MemoryMetrics = new MemoryMetrics
            {
                MinMb = GetFloatDetail(report.MemoryResult, "MinMb") ?? 0f,
                MaxMb = GetFloatDetail(report.MemoryResult, "MaxMb") ?? 0f,
                DeltaMb = GetFloatDetail(report.MemoryResult, "DeltaMb") ?? 0f
            };
        }

        CleanupMetrics = new CleanupMetrics
        {
            TempFiles = GetIntDetail(report.CleanupResult, "TempFiles") ?? 0,
            OrphanWhisper = GetIntDetail(report.CleanupResult, "OrphanWhisper") ?? 0,
            OrphanFfmpeg = GetIntDetail(report.CleanupResult, "OrphanFfmpeg") ?? 0
        };
    }

    private static int? GetIntDetail(CertificationResult result, string key)
    {
        if (result.Details.TryGetValue(key, out var val))
            return Convert.ToInt32(val);
        return null;
    }

    private static float? GetFloatDetail(CertificationResult result, string key)
    {
        if (result.Details.TryGetValue(key, out var val))
            return Convert.ToSingle(val);
        return null;
    }
}

public class TimestampMetrics
{
    public int TotalSubtitles { get; set; }
    public int Overlaps { get; set; }
    public int Gaps { get; set; }
    public int OutOfDuration { get; set; }
}

public class QualityMetrics
{
    public int LegacyCount { get; set; }
    public int ChunkedCount { get; set; }
}

public class MemoryMetrics
{
    public float MinMb { get; set; }
    public float MaxMb { get; set; }
    public float DeltaMb { get; set; }
}

public class CleanupMetrics
{
    public int TempFiles { get; set; }
    public int OrphanWhisper { get; set; }
    public int OrphanFfmpeg { get; set; }
}