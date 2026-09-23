using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.Engines;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class CertificationHarness : IDisposable
{
    private readonly string _videoPath;
    private readonly VideoType _videoType;
    private readonly ISpeechToTextEngine _engine;
    private readonly string _modelName;
    private readonly string _language;
    private readonly int _audioTrackIndex;
    private readonly string _tempPath;
    private readonly string _legacySrtPath;
    private readonly string _chunkedSrtPath;

    private bool _disposed;

    public CertificationHarness(
        string videoPath,
        VideoType videoType,
        ISpeechToTextEngine engine,
        string modelName,
        string language,
        int audioTrackIndex = 0,
        string? tempPath = null)
    {
        _videoPath = videoPath;
        _videoType = videoType;
        _engine = engine;
        _modelName = modelName;
        _language = language;
        _audioTrackIndex = audioTrackIndex;
        _tempPath = tempPath ?? Path.GetTempPath();

        var videoHash = Path.GetFileNameWithoutExtension(videoPath).GetHashCode().ToString("X8");
        var basePath = Path.Combine(_tempPath, $"cert_{videoHash}");
        _legacySrtPath = basePath + "_legacy.srt";
        _chunkedSrtPath = basePath + "_chunked.srt";
    }

    public async Task<VideoCertReport> RunCertificationAsync(CancellationToken ct = default)
    {
        var videoDuration = await GetVideoDurationAsync(ct);
        var report = new VideoCertReport
        {
            VideoPath = _videoPath,
            VideoDuration = videoDuration,
            Type = _videoType
        };

        CleanupOrphanProcesses();
        var initialSnapshots = CaptureSnapshots();

        try
        {
            report = await RunLegacyPipelineAsync(report, ct);
            report = await RunChunkedPipelineAsync(report, ct);
            report = ValidateTimestamps(report, videoDuration);
            report = ValidateQuality(report);
            report = ValidateMemoryStability(report);
            report = ValidateCleanup(report, initialSnapshots);
        }
        catch (Exception ex)
        {
            report = report with
            {
                TimestampResult = new CertificationResult("Timestamp", CertificationStatus.Failed, $"Exception: {ex.Message}", new Dictionary<string, object>())
            };
        }

        return report;
    }

    public async Task<VideoCertReport> RunLegacyPipelineAsync(VideoCertReport report, CancellationToken ct = default)
    {
        var metrics = new RuntimeMetrics(2000, _tempPath);
        metrics.Start();

        var transcriber = new LegacyTranscriber(
            _videoPath, _legacySrtPath, _engine, _modelName, _language, _audioTrackIndex, _tempPath);

        try
        {
            var subtitle = await transcriber.TranscribeAsync(ct);
            metrics.Stop();

            return report with
            {
                LegacySubtitle = subtitle,
                LegacyRuntime = metrics.GetReport()
            };
        }
        finally
        {
            metrics.Dispose();
        }
    }

    public async Task<VideoCertReport> RunChunkedPipelineAsync(VideoCertReport report, CancellationToken ct = default)
    {
        if (File.Exists(_chunkedSrtPath))
            File.Delete(_chunkedSrtPath);

        var metrics = new RuntimeMetrics(2000, _tempPath);
        metrics.Start();

        var audioExtractor = new FfmpegAudioExtractor(_videoPath, _audioTrackIndex);
        var audioTranscriber = PipelineFactory.CreateTranscriber(_engine, _modelName, _language);
        var duration = await audioExtractor.GetVideoDurationAsync(ct);

        var controller = new PipelineController(
            _videoPath,
            _chunkedSrtPath,
            audioExtractor,
            audioTranscriber,
            duration,
            _audioTrackIndex);

        try
        {
            var subtitle = await controller.TranscribeAsync(ct);
            metrics.Stop();

            return report with
            {
                ChunkedSubtitle = subtitle,
                ChunkedRuntime = metrics.GetReport()
            };
        }
        finally
        {
            metrics.Dispose();
            controller.Dispose();
        }
    }

    private VideoCertReport ValidateTimestamps(VideoCertReport report, TimeSpan videoDuration)
    {
        if (report.ChunkedSubtitle == null)
            return report with { TimestampResult = new CertificationResult("Timestamp", CertificationStatus.Failed, "No chunked output", new Dictionary<string, object>()) };

        var validation = TimestampValidator.Validate(report.ChunkedSubtitle, videoDuration);

        var first = report.ChunkedSubtitle.Paragraphs.FirstOrDefault();
        var last = report.ChunkedSubtitle.Paragraphs.LastOrDefault();

        var isValid = validation.IsValid;
        var errors = new List<string>(validation.Errors);

        if (first != null && first.StartTime.TotalMilliseconds < 0)
        {
            isValid = false;
            errors.Add("First subtitle starts before video start");
        }

        if (last != null && last.EndTime.TotalMilliseconds > videoDuration.TotalMilliseconds)
        {
            isValid = false;
            errors.Add("Last subtitle ends after video end");
        }

        var details = new Dictionary<string, object>
        {
            ["TotalSubtitles"] = validation.Metrics.TotalSubtitles,
            ["Overlaps"] = validation.Metrics.OverlapCount,
            ["Gaps"] = validation.Metrics.GapCount,
            ["Negatives"] = validation.Metrics.NegativeTimestampCount,
            ["OutOfDuration"] = validation.Metrics.OutOfDurationCount,
            ["AvgDuration"] = validation.Metrics.AverageDuration
        };

        var status = isValid ? CertificationStatus.Passed : CertificationStatus.Failed;
        var message = isValid
            ? $"{validation.Metrics.TotalSubtitles} subs, avg {validation.Metrics.AverageDuration:F1}s"
            : string.Join("; ", errors.Take(3));

        return report with { TimestampResult = new CertificationResult("Timestamp", status, message, details) };
    }

    private VideoCertReport ValidateQuality(VideoCertReport report)
    {
        if (report.LegacySubtitle == null || report.ChunkedSubtitle == null)
            return report with { QualityResult = new CertificationResult("Quality", CertificationStatus.Failed, "Missing subtitle output", new Dictionary<string, object>()) };

        var comparison = QualityComparison.Compare(report.LegacySubtitle, report.ChunkedSubtitle);
        var details = new Dictionary<string, object>
        {
            ["LegacyCount"] = comparison.LegacySubtitleCount,
            ["ChunkedCount"] = comparison.ChunkedSubtitleCount,
            ["CountDiff"] = $"{comparison.SubtitleCountDifferencePercent:F1}%",
            ["WordDiff"] = $"{comparison.WordCountDifferencePercent:F1}%",
            ["DurationDiff"] = $"{comparison.AverageDurationDifferencePercent:F1}%"
        };

        var hasSignificantIssues = comparison.Issues.Count > 0 &&
                                   comparison.Issues.Any(i => i.Severity >= 20);

        var status = hasSignificantIssues ? CertificationStatus.Failed
            : comparison.Issues.Count > 0 ? CertificationStatus.Warning
            : CertificationStatus.Passed;

        var message = comparison.Issues.Count == 0
            ? "Quality matches legacy"
            : $"{comparison.Issues.Count} issues: {comparison.Issues.First().Description}";

        return report with { QualityResult = new CertificationResult("Quality", status, message, details) };
    }

    private VideoCertReport ValidateMemoryStability(VideoCertReport report)
    {
        if (report.ChunkedRuntime == null)
            return report with { MemoryResult = new CertificationResult("Memory", CertificationStatus.NotRun, "Not executed", new Dictionary<string, object>()) };

        var runtime = report.ChunkedRuntime;
        var details = new Dictionary<string, object>
        {
            ["MinMb"] = runtime.MinMemoryMb,
            ["MaxMb"] = runtime.MaxMemoryMb,
            ["MedianMb"] = runtime.MedianMemoryMb,
            ["DeltaMb"] = runtime.MaxMemoryMb - runtime.MinMemoryMb,
            ["PeakTempFiles"] = runtime.PeakTempFiles
        };

        var maxDelta = runtime.MaxMemoryMb - runtime.MinMemoryMb;
        var status = maxDelta < 50 ? CertificationStatus.Passed
            : maxDelta < 100 ? CertificationStatus.Warning
            : CertificationStatus.Failed;

        var message = $"Memory delta: {maxDelta:F0}MB (max {runtime.MaxMemoryMb:F0}MB)";

        return report with { MemoryResult = new CertificationResult("Memory", status, message, details) };
    }

    private VideoCertReport ValidateCleanup(VideoCertReport report, (TempFileSnapshot legacy, TempFileSnapshot chunked) initialSnapshots)
    {
        var validator = new CleanupValidator(_tempPath);
        var tempReport = validator.ValidateTempFiles(Array.Empty<string>());
        var whisperReport = CleanupValidator.ValidateNoOrphanWhisperProcesses();
        var ffmpegReport = CleanupValidator.ValidateNoOrphanFfmpegProcesses();

        var allClean = tempReport.IsClean && whisperReport.IsClean && ffmpegReport.IsClean;

        var details = new Dictionary<string, object>
        {
            ["TempFiles"] = tempReport.TotalFound,
            ["OrphanWhisper"] = whisperReport.TotalFound,
            ["OrphanFfmpeg"] = ffmpegReport.TotalFound
        };

        var status = allClean ? CertificationStatus.Passed : CertificationStatus.Failed;
        var issues = new List<string>();
        if (!tempReport.IsClean) issues.Add($"{tempReport.UnexpectedCount} temp files");
        if (!whisperReport.IsClean) issues.Add($"{whisperReport.UnexpectedCount} whisper procs");
        if (!ffmpegReport.IsClean) issues.Add($"{ffmpegReport.UnexpectedCount} ffmpeg procs");

        return report with { CleanupResult = new CertificationResult("Cleanup", status, string.Join("; ", issues), details) };
    }

    private async Task<TimeSpan> GetVideoDurationAsync(CancellationToken ct)
    {
        var extractor = new FfmpegAudioExtractor(_videoPath, _audioTrackIndex);
        return await extractor.GetVideoDurationAsync(ct);
    }

    private (TempFileSnapshot legacy, TempFileSnapshot chunked) CaptureSnapshots()
    {
        var validator = new CleanupValidator(_tempPath);
        var snap1 = new TempFileSnapshot(DateTime.Now, 0, new List<string>());
        var snap2 = new TempFileSnapshot(DateTime.Now, 0, new List<string>());
        return (snap1, snap2);
    }

    private static void CleanupOrphanProcesses()
    {
        foreach (var p in Process.GetProcesses()
            .Where(p => p.ProcessName.Contains("whisper", StringComparison.OrdinalIgnoreCase) ||
                       p.ProcessName.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase)))
        {
            try { p.Kill(); } catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (File.Exists(_legacySrtPath))
            try { File.Delete(_legacySrtPath); } catch { }

        if (File.Exists(_chunkedSrtPath))
            try { File.Delete(_chunkedSrtPath); } catch { }

        _disposed = true;
    }
}