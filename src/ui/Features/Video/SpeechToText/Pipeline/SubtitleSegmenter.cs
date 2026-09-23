using System;
using System.Collections.Generic;
using System.Linq;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public readonly struct WordTiming
{
    public string Text { get; }
    public double StartSeconds { get; }
    public double EndSeconds { get; }

    public WordTiming(string text, double startSeconds, double endSeconds)
    {
        Text = text;
        StartSeconds = startSeconds;
        EndSeconds = endSeconds;
    }

    public WordTiming(string text, TimeSpan start, TimeSpan end)
    {
        Text = text;
        StartSeconds = start.TotalSeconds;
        EndSeconds = end.TotalSeconds;
    }

    public TimeSpan Start => TimeSpan.FromSeconds(StartSeconds);
    public TimeSpan End => TimeSpan.FromSeconds(EndSeconds);
}

public sealed class SubtitleSegmentationOptions
{
    public int MaxCharsPerLine { get; set; } = 80;
    public double MaxGapSeconds { get; set; } = 0.5;
    public double MaxDurationSeconds { get; set; } = 10.0;
    public int MaxLinesPerSubtitle { get; set; } = 2;

    public static SubtitleSegmentationOptions Default => new();

    public static SubtitleSegmentationOptions ReadingOptimized => new()
    {
        MaxCharsPerLine = 42,
        MaxGapSeconds = 0.3,
        MaxDurationSeconds = 5.0,
        MaxLinesPerSubtitle = 2
    };

    public static SubtitleSegmentationOptions Karaoke => new()
    {
        MaxCharsPerLine = 50,
        MaxGapSeconds = 1.5,
        MaxDurationSeconds = 15.0,
        MaxLinesPerSubtitle = 3
    };
}

public static class SubtitleSegmenter
{
    public static Subtitle FromWordTimings(
        IEnumerable<WordTiming> words,
        TimeSpan offset = default,
        SubtitleSegmentationOptions? options = null)
    {
        var result = new Subtitle();
        var opts = options ?? SubtitleSegmentationOptions.Default;

        var currentText = new System.Text.StringBuilder();
        var startTimeMs = -1.0;
        var endTimeMs = -1.0;
        var lineCount = 0;

        foreach (var word in words)
        {
            var startMs = word.StartSeconds * 1000.0 + offset.TotalMilliseconds;
            var endMs = word.EndSeconds * 1000.0 + offset.TotalMilliseconds;
            var text = word.Text ?? string.Empty;

            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (startTimeMs < 0)
            {
                startTimeMs = startMs;
            }

            var gap = word.StartSeconds - (endTimeMs / 1000.0);
            var durationMs = endMs - startMs;

            var exceedsMaxDuration = durationMs > opts.MaxDurationSeconds * 1000;
            var exceedsMaxGap = gap > opts.MaxGapSeconds;
            var exceedsMaxChars = currentText.Length + text.Length > opts.MaxCharsPerLine;
            var exceedsMaxLines = lineCount >= opts.MaxLinesPerSubtitle;

            var newParagraph = currentText.Length > 0 &&
                (exceedsMaxGap || exceedsMaxChars || exceedsMaxLines || exceedsMaxDuration);

            if (newParagraph)
            {
                result.Paragraphs.Add(new Paragraph(
                    currentText.ToString().Trim(),
                    startTimeMs,
                    endTimeMs));

                currentText.Clear();
                lineCount = 0;
                startTimeMs = startMs;
            }

            if (currentText.Length > 0)
            {
                currentText.Append(' ');
            }

            currentText.Append(text);
            lineCount++;
            endTimeMs = endMs;
        }

        if (currentText.Length > 0 && endTimeMs >= 0)
        {
            result.Paragraphs.Add(new Paragraph(
                currentText.ToString().Trim(),
                startTimeMs,
                endTimeMs));
        }

        return result;
    }

    public static Subtitle FromWordTimings(
        IEnumerable<(string word, double start, double end)> words,
        TimeSpan offset = default,
        SubtitleSegmentationOptions? options = null)
    {
        var timings = words.Select(w => new WordTiming(w.word, w.start, w.end));
        return FromWordTimings(timings, offset, options);
    }

    public static Subtitle FromSrtLines(
        string[] lines,
        TimeSpan offset = default,
        SubtitleSegmentationOptions? options = null)
    {
        var result = new Subtitle();

        if (lines == null || lines.Length < 3)
        {
            return result;
        }

        int index = 0;
        while (index < lines.Length)
        {
            while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index].Trim()))
            {
                index++;
            }

            if (index >= lines.Length)
            {
                break;
            }

            if (!int.TryParse(lines[index].Trim(), out _))
            {
                index++;
                continue;
            }
            index++;

            if (index >= lines.Length)
            {
                break;
            }

            var timeLine = lines[index].Trim();
            var timestampMatch = System.Text.RegularExpressions.Regex.Match(
                timeLine,
                @"(\d{2}):(\d{2}):(\d{2})[.,](\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2})[.,](\d{3})");

            if (!timestampMatch.Success)
            {
                index++;
                continue;
            }

            var startMs = int.Parse(timestampMatch.Groups[1].Value) * 3600000 +
                          int.Parse(timestampMatch.Groups[2].Value) * 60000 +
                          int.Parse(timestampMatch.Groups[3].Value) * 1000 +
                          int.Parse(timestampMatch.Groups[4].Value);

            var endMs = int.Parse(timestampMatch.Groups[5].Value) * 3600000 +
                        int.Parse(timestampMatch.Groups[6].Value) * 60000 +
                        int.Parse(timestampMatch.Groups[7].Value) * 1000 +
                        int.Parse(timestampMatch.Groups[8].Value);

            index++;

            if (index >= lines.Length)
            {
                break;
            }

            var textLines = new List<string>();
            while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
            {
                textLines.Add(lines[index]);
                index++;
            }

            var text = string.Join(Environment.NewLine, textLines);

            result.Paragraphs.Add(new Paragraph(
                text,
                startMs + offset.TotalMilliseconds,
                endMs + offset.TotalMilliseconds));
        }

        return result;
    }
}