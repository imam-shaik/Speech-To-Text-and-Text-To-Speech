using System;
using System.Collections.Generic;
using System.Linq;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class TimestampValidator
{
    public record ValidationResult(
        bool IsValid,
        List<string> Errors,
        List<string> Warnings,
        TimestampMetrics Metrics);

    public record TimestampMetrics(
        int TotalSubtitles,
        int OverlapCount,
        int GapCount,
        int NegativeTimestampCount,
        int OutOfDurationCount,
        double AverageDuration,
        double MinDuration,
        double MaxDuration);

    public static ValidationResult Validate(Subtitle subtitle, TimeSpan? maxDuration = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var paragraphs = subtitle.Paragraphs;

        if (paragraphs.Count == 0)
        {
            warnings.Add("No subtitles in output");
            return new ValidationResult(true, errors, warnings, new TimestampMetrics(0, 0, 0, 0, 0, 0, 0, 0));
        }

        var overlapCount = 0;
        var gapCount = 0;
        var negativeCount = 0;
        var outOfDurationCount = 0;
        var durations = new List<double>();

        for (var i = 0; i < paragraphs.Count; i++)
        {
            var p = paragraphs[i];

            if (p.StartTime.TotalMilliseconds < 0)
            {
                negativeCount++;
                errors.Add($"Subtitle {i + 1} has negative start time: {p.StartTime}");
            }

            if (p.EndTime.TotalMilliseconds < 0)
            {
                negativeCount++;
                errors.Add($"Subtitle {i + 1} has negative end time: {p.EndTime}");
            }

            if (p.StartTime.TotalMilliseconds >= p.EndTime.TotalMilliseconds)
            {
                overlapCount++;
                errors.Add($"Subtitle {i + 1} has start >= end: {p.StartTime} >= {p.EndTime}");
            }

            if (maxDuration.HasValue)
            {
                if (p.StartTime.TotalMilliseconds > maxDuration.Value.TotalMilliseconds)
                {
                    outOfDurationCount++;
                    errors.Add($"Subtitle {i + 1} starts after video end: {p.StartTime} > {maxDuration}");
                }

                if (p.EndTime.TotalMilliseconds > maxDuration.Value.TotalMilliseconds)
                {
                    outOfDurationCount++;
                    warnings.Add($"Subtitle {i + 1} ends after video end: {p.EndTime} > {maxDuration}");
                }
            }

            durations.Add(p.Duration.TotalSeconds);

            if (i > 0)
            {
                var prev = paragraphs[i - 1];
                if (prev.EndTime.TotalMilliseconds > p.StartTime.TotalMilliseconds)
                {
                    overlapCount++;
                    errors.Add($"Overlap between subtitle {i} and {i + 1}: {prev.EndTime} > {p.StartTime}");
                }

                var gap = p.StartTime.TotalMilliseconds - prev.EndTime.TotalMilliseconds;
                if (gap > 1000)
                {
                    gapCount++;
                    warnings.Add($"Gap of {gap / 1000.0:F1}s before subtitle {i + 1}");
                }
            }
        }

        var metrics = new TimestampMetrics(
            paragraphs.Count,
            overlapCount,
            gapCount,
            negativeCount,
            outOfDurationCount,
            durations.Count > 0 ? durations.Average() : 0,
            durations.Count > 0 ? durations.Min() : 0,
            durations.Count > 0 ? durations.Max() : 0);

        return new ValidationResult(
            errors.Count == 0,
            errors,
            warnings,
            metrics);
    }

    public static bool ValidateTimestampsMatch(Subtitle subtitle1, Subtitle subtitle2, double toleranceMs = 50)
    {
        var p1 = subtitle1.Paragraphs;
        var p2 = subtitle2.Paragraphs;

        if (Math.Abs(p1.Count - p2.Count) > p1.Count * 0.1)
            return false;

        var minCount = Math.Min(p1.Count, p2.Count);
        var matched = 0;

        for (var i = 0; i < minCount; i++)
        {
            var startDiff = Math.Abs(p1[i].StartTime.TotalMilliseconds - p2[i].StartTime.TotalMilliseconds);
            var endDiff = Math.Abs(p1[i].EndTime.TotalMilliseconds - p2[i].EndTime.TotalMilliseconds);

            if (startDiff <= toleranceMs && endDiff <= toleranceMs)
                matched++;
        }

        return matched >= minCount * 0.9;
    }
}