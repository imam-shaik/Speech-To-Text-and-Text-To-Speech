using System;
using System.Collections.Generic;
using System.Linq;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class QualityComparison
{
    public record ComparisonResult(
        int LegacySubtitleCount,
        int ChunkedSubtitleCount,
        double SubtitleCountDifferencePercent,
        double LegacyAverageDuration,
        double ChunkedAverageDuration,
        double AverageDurationDifferencePercent,
        int LegacyWordCount,
        int ChunkedWordCount,
        double WordCountDifferencePercent,
        List<QualityIssue> Issues);

    public record QualityIssue(
        QualityIssueType Type,
        string Description,
        double Severity);

    public enum QualityIssueType
    {
        MissingSubtitles,
        ExtraSubtitles,
        DurationMismatch,
        WordCountMismatch,
        TimestampDrift,
        QualityDegradation
    }

    public static ComparisonResult Compare(Subtitle legacy, Subtitle chunked)
    {
        var issues = new List<QualityIssue>();

        var legacyCount = legacy.Paragraphs.Count;
        var chunkedCount = chunked.Paragraphs.Count;
        var countDiff = legacyCount > 0
            ? Math.Abs(legacyCount - chunkedCount) / (double)legacyCount * 100.0
            : 0;

        if (countDiff > 20)
        {
            issues.Add(new QualityIssue(
                QualityIssueType.ExtraSubtitles,
                $"Subtitle count differs by {countDiff:F1}%",
                countDiff));
        }

        var legacyAvgDuration = legacy.Paragraphs.Count > 0
            ? legacy.Paragraphs.Average(p => p.Duration.TotalSeconds)
            : 0;
        var chunkedAvgDuration = chunked.Paragraphs.Count > 0
            ? chunked.Paragraphs.Average(p => p.Duration.TotalSeconds)
            : 0;
        var durationDiff = legacyAvgDuration > 0
            ? Math.Abs(legacyAvgDuration - chunkedAvgDuration) / legacyAvgDuration * 100.0
            : 0;

        if (durationDiff > 30)
        {
            issues.Add(new QualityIssue(
                QualityIssueType.DurationMismatch,
                $"Average duration differs by {durationDiff:F1}%",
                durationDiff));
        }

        var legacyWords = legacy.Paragraphs.Sum(p => CountWords(p.Text));
        var chunkedWords = chunked.Paragraphs.Sum(p => CountWords(p.Text));
        var wordDiff = legacyWords > 0
            ? Math.Abs(legacyWords - chunkedWords) / (double)legacyWords * 100.0
            : 0;

        if (wordDiff > 15)
        {
            issues.Add(new QualityIssue(
                QualityIssueType.WordCountMismatch,
                $"Word count differs by {wordDiff:F1}%",
                wordDiff));
        }

        if (legacyCount > 0 && chunkedCount > 0)
        {
            var minOverlap = Math.Min(legacyCount, chunkedCount) * 0.8;
            var matchedTimestamps = 0;

            var maxIndex = Math.Min(legacyCount, chunkedCount);
            for (var i = 0; i < maxIndex; i++)
            {
                var startDiff = Math.Abs(
                    legacy.Paragraphs[i].StartTime.TotalMilliseconds -
                    chunked.Paragraphs[i].StartTime.TotalMilliseconds);

                if (startDiff < 500)
                    matchedTimestamps++;
            }

            var matchRatio = matchedTimestamps / (double)minOverlap;
            if (matchRatio < 0.7)
            {
                issues.Add(new QualityIssue(
                    QualityIssueType.TimestampDrift,
                    $"Only {matchRatio:P0} of chunk boundaries match legacy",
                    (1 - matchRatio) * 100));
            }
        }

        return new ComparisonResult(
            legacyCount,
            chunkedCount,
            countDiff,
            legacyAvgDuration,
            chunkedAvgDuration,
            durationDiff,
            legacyWords,
            chunkedWords,
            wordDiff,
            issues);
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    public static string FormatReport(ComparisonResult result)
    {
        var lines = new List<string>
        {
            "=== Quality Comparison Report ===",
            "",
            "Subtitle Count:",
            $"  Legacy:  {result.LegacySubtitleCount}",
            $"  Chunked: {result.ChunkedSubtitleCount}",
            $"  Diff:    {result.SubtitleCountDifferencePercent:F1}%",
            "",
            "Average Duration:",
            $"  Legacy:  {result.LegacyAverageDuration:F1}s",
            $"  Chunked: {result.ChunkedAverageDuration:F1}s",
            $"  Diff:    {result.AverageDurationDifferencePercent:F1}%",
            "",
            "Word Count:",
            $"  Legacy:  {result.LegacyWordCount}",
            $"  Chunked: {result.ChunkedWordCount}",
            $"  Diff:    {result.WordCountDifferencePercent:F1}%",
            ""
        };

        if (result.Issues.Count > 0)
        {
            lines.Add("Issues Found:");
            foreach (var issue in result.Issues)
            {
                lines.Add($"  [{issue.Severity:F0}] {issue.Type}: {issue.Description}");
            }
        }
        else
        {
            lines.Add("No significant quality issues detected.");
        }

        return string.Join(Environment.NewLine, lines);
    }
}