using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class CertificationRunner
{
    private readonly string _videoPath;
    private readonly string _legacySrtPath;
    private readonly string _chunkedSrtPath;
    private readonly string _tempPath;
    private readonly CleanupValidator _cleanupValidator;

    public CertificationRunner(
        string videoPath,
        string legacySrtPath,
        string chunkedSrtPath,
        string? tempPath = null)
    {
        _videoPath = videoPath;
        _legacySrtPath = legacySrtPath;
        _chunkedSrtPath = chunkedSrtPath;
        _tempPath = tempPath ?? Path.GetTempPath();
        _cleanupValidator = new CleanupValidator(_tempPath);
    }

    public CertificationReport RunFullCertification(
        Subtitle legacySubtitle,
        Subtitle chunkedSubtitle,
        TimeSpan videoDuration,
        MemoryReport memoryReport)
    {
        var results = new List<CertificationResult>();

        results.Add(TimestampCertification(chunkedSubtitle, videoDuration));
        results.Add(QualityCertification(legacySubtitle, chunkedSubtitle));
        results.Add(MemoryCertification(memoryReport));
        results.Add(CleanupCertification());

        return new CertificationReport(results);
    }

    public CertificationResult TimestampCertification(Subtitle subtitle, TimeSpan videoDuration)
    {
        var validation = TimestampValidator.Validate(subtitle, videoDuration);

        return new CertificationResult(
            "Timestamp Certification",
            validation.IsValid ? CertificationStatus.Passed : CertificationStatus.Failed,
            validation.Errors.Count > 0
                ? string.Join("; ", validation.Errors.Take(5))
                : "All timestamps valid",
            new Dictionary<string, object>
            {
                ["TotalSubtitles"] = validation.Metrics.TotalSubtitles,
                ["OverlapCount"] = validation.Metrics.OverlapCount,
                ["GapCount"] = validation.Metrics.GapCount,
                ["NegativeCount"] = validation.Metrics.NegativeTimestampCount,
                ["OutOfDurationCount"] = validation.Metrics.OutOfDurationCount,
                ["AverageDuration"] = validation.Metrics.AverageDuration
            });
    }

    public CertificationResult QualityCertification(Subtitle legacy, Subtitle chunked)
    {
        var comparison = QualityComparison.Compare(legacy, chunked);

        var status = comparison.Issues.Count == 0
            ? CertificationStatus.Passed
            : comparison.Issues.All(i => i.Severity < 20)
                ? CertificationStatus.Warning
                : CertificationStatus.Failed;

        return new CertificationResult(
            "Quality Certification",
            status,
            comparison.Issues.Count == 0
                ? "Output quality matches legacy"
                : $"{comparison.Issues.Count} quality issues found",
            new Dictionary<string, object>
            {
                ["SubtitleCountDiff"] = comparison.SubtitleCountDifferencePercent,
                ["DurationDiff"] = comparison.AverageDurationDifferencePercent,
                ["WordCountDiff"] = comparison.WordCountDifferencePercent,
                ["IssueCount"] = comparison.Issues.Count
            });
    }

    public CertificationResult MemoryCertification(MemoryReport report)
    {
        var status = report.IsStable(50)
            ? CertificationStatus.Passed
            : report.IsStable(100)
                ? CertificationStatus.Warning
                : CertificationStatus.Failed;

        return new CertificationResult(
            "Memory Certification",
            status,
            report.ToString(),
            new Dictionary<string, object>
            {
                ["MinMemory"] = report.MinWorkingSetMb,
                ["MaxMemory"] = report.MaxWorkingSetMb,
                ["MedianMemory"] = report.MedianWorkingSetMb,
                ["IsStable"] = report.IsStable()
            });
    }

    public CertificationResult CleanupCertification()
    {
        var report = _cleanupValidator.ValidateTempFiles(Array.Empty<string>());
        var processReport = CleanupValidator.ValidateNoOrphanWhisperProcesses();
        var ffmpegReport = CleanupValidator.ValidateNoOrphanFfmpegProcesses();

        var allClean = report.IsClean && processReport.IsClean && ffmpegReport.IsClean;

        var details = new List<string>();
        if (!report.IsClean) details.Add($"{report.UnexpectedCount} temp files");
        if (!processReport.IsClean) details.Add($"{processReport.UnexpectedCount} orphan whisper processes");
        if (!ffmpegReport.IsClean) details.Add($"{ffmpegReport.UnexpectedCount} orphan ffmpeg processes");

        return new CertificationResult(
            "Cleanup Certification",
            allClean ? CertificationStatus.Passed : CertificationStatus.Failed,
            details.Count > 0 ? string.Join("; ", details) : "All clean",
            new Dictionary<string, object>
            {
                ["TempFiles"] = report.TotalFound,
                ["OrphanWhisper"] = processReport.TotalFound,
                ["OrphanFfmpeg"] = ffmpegReport.TotalFound
            });
    }

    public CertificationResult ResumeCertification(
        Func<Subtitle> loadCheckpoint,
        Func<Subtitle> transcribe,
        int checkpointInterval)
    {
        return new CertificationResult(
            "Resume Certification",
            CertificationStatus.NotRun,
            "Run manually with actual interruption",
            new Dictionary<string, object>());
    }
}

public enum CertificationStatus
{
    NotRun,
    Passed,
    Warning,
    Failed
}

public record CertificationResult(
    string Name,
    CertificationStatus Status,
    string Message,
    Dictionary<string, object> Details);

public record CertificationReport(List<CertificationResult> Results)
{
    public bool AllPassed => Results.All(r => r.Status == CertificationStatus.Passed);
    public bool AnyFailed => Results.Any(r => r.Status == CertificationStatus.Failed);
    public int PassedCount => Results.Count(r => r.Status == CertificationStatus.Passed);
    public int FailedCount => Results.Count(r => r.Status == CertificationStatus.Failed);

    public string GetSummary()
    {
        var lines = new List<string>
        {
            "=== Certification Report ===",
            $"Passed: {PassedCount}/{Results.Count}",
            $"Failed: {FailedCount}",
            ""
        };

        foreach (var result in Results)
        {
            var icon = result.Status switch
            {
                CertificationStatus.Passed => "✅",
                CertificationStatus.Warning => "⚠️",
                CertificationStatus.Failed => "❌",
                _ => "⬜"
            };
            lines.Add($"{icon} {result.Name}: {result.Status} - {result.Message}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}