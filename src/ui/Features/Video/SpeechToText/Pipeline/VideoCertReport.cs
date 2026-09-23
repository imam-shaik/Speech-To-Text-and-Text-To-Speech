using System;
using System.Collections.Generic;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed record VideoCertReport
{
    public string VideoPath { get; init; } = string.Empty;
    public string VideoName => System.IO.Path.GetFileName(VideoPath);
    public TimeSpan VideoDuration { get; init; }
    public VideoType Type { get; init; }

    public CertificationResult TimestampResult { get; init; } = new CertificationResult("Timestamp", CertificationStatus.NotRun, "Not executed", new Dictionary<string, object>());
    public CertificationResult QualityResult { get; init; } = new CertificationResult("Quality", CertificationStatus.NotRun, "Not executed", new Dictionary<string, object>());
    public CertificationResult MemoryResult { get; init; } = new CertificationResult("Memory", CertificationStatus.NotRun, "Not executed", new Dictionary<string, object>());
    public CertificationResult CleanupResult { get; init; } = new CertificationResult("Cleanup", CertificationStatus.NotRun, "Not executed", new Dictionary<string, object>());
    public CertificationResult ResumeResult { get; init; } = new CertificationResult("Resume", CertificationStatus.NotRun, "Not executed", new Dictionary<string, object>());
    public CertificationResult CancelResult { get; init; } = new CertificationResult("Cancel", CertificationStatus.NotRun, "Not executed", new Dictionary<string, object>());

    public RuntimeReport? LegacyRuntime { get; init; }
    public RuntimeReport? ChunkedRuntime { get; init; }

    public Subtitle? LegacySubtitle { get; init; }
    public Subtitle? ChunkedSubtitle { get; init; }

    public bool AllPassed => TimestampResult.Status == CertificationStatus.Passed &&
                             QualityResult.Status == CertificationStatus.Passed &&
                             MemoryResult.Status == CertificationStatus.Passed &&
                             CleanupResult.Status == CertificationStatus.Passed;

    public bool AnyFailed => TimestampResult.Status == CertificationStatus.Failed ||
                             QualityResult.Status == CertificationStatus.Failed ||
                             MemoryResult.Status == CertificationStatus.Failed ||
                             CleanupResult.Status == CertificationStatus.Failed;

    public string GetSummary()
    {
        var lines = new List<string>
        {
            $"=== Video: {VideoName} ===",
            $"Type: {Type}, Duration: {VideoDuration:hh\\:mm\\:ss}",
            ""
        };

        AddResult(lines, "Timestamp", TimestampResult);
        AddResult(lines, "Quality", QualityResult);
        AddResult(lines, "Memory", MemoryResult);
        AddResult(lines, "Cleanup", CleanupResult);

        if (LegacyRuntime != null)
        {
            lines.Add("");
            lines.Add("Legacy Runtime:");
            lines.Add($"  {LegacyRuntime}");
        }

        if (ChunkedRuntime != null)
        {
            lines.Add("");
            lines.Add("Chunked Runtime:");
            lines.Add($"  {ChunkedRuntime}");
        }

        lines.Add("");
        lines.Add($"Overall: {(AllPassed ? "PASS" : AnyFailed ? "FAIL" : "WARNING")}");

        return string.Join(Environment.NewLine, lines);
    }

    private static void AddResult(List<string> lines, string name, CertificationResult result)
    {
        var icon = result.Status switch
        {
            CertificationStatus.Passed => "PASS",
            CertificationStatus.Failed => "FAIL",
            CertificationStatus.Warning => "WARN",
            _ => "----"
        };
        lines.Add($"  [{icon}] {name}: {result.Message}");
    }
}

public enum VideoType
{
    Unknown,
    Silent,
    SingleSpeaker,
    Conversation,
    Movie,
    NoisyMeeting,
    VeryLong,
    MultipleAudioTracks,
    Corrupted
}

public static class CertificationResultExtensions
{
    public static CertificationResult NotRun(string name) =>
        new(name, CertificationStatus.NotRun, "Not executed", new Dictionary<string, object>());

    public static CertificationResult Passed(string name, string message, Dictionary<string, object>? details = null) =>
        new(name, CertificationStatus.Passed, message, details ?? new Dictionary<string, object>());

    public static CertificationResult Failed(string name, string message, Dictionary<string, object>? details = null) =>
        new(name, CertificationStatus.Failed, message, details ?? new Dictionary<string, object>());
}