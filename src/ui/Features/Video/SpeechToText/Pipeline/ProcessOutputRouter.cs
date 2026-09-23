using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class ProcessOutputRouter
{
    private static readonly Regex ProgressPercentRegex = new(@"^(\d+)%$", RegexOptions.Compiled);
    private static readonly Regex WhisperProgressRegex = new(@"^\s*\[.*\]\s+(\d+)%$", RegexOptions.Compiled);
    private static readonly Regex FasterWhisperProgressRegex = new(@"^(\d+)%\s*$", RegexOptions.Compiled);

    public event EventHandler<ProcessOutputEvent>? OutputReceived;

    public void Route(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        var trimmed = line.Trim();

        if (TryParseProgress(trimmed, out var evt))
        {
            OutputReceived?.Invoke(this, evt);
            return;
        }

        if (trimmed.StartsWith("TEXT:", StringComparison.OrdinalIgnoreCase))
        {
            var text = trimmed.Length > 5 ? trimmed.Substring(5).Trim() : string.Empty;
            if (!string.IsNullOrEmpty(text))
            {
                text = DecodeJsonEscapedString(text);
                OutputReceived?.Invoke(this, ProcessOutputEvent.Transcript(text, line));
            }
            return;
        }

        if (trimmed.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase))
        {
            var message = ExtractMessage(trimmed, "ERROR");
            OutputReceived?.Invoke(this, ProcessOutputEvent.Error(message, line));
            return;
        }

        if (trimmed.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("[WARNING]", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("warning", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsLikelyProgress(trimmed))
            {
                var message = ExtractMessage(trimmed, "WARNING");
                OutputReceived?.Invoke(this, ProcessOutputEvent.Warning(message, line));
                return;
            }
        }

        if (trimmed.StartsWith("DEBUG:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("[DEBUG]", StringComparison.OrdinalIgnoreCase))
        {
            var message = ExtractMessage(trimmed, "DEBUG");
            OutputReceived?.Invoke(this, ProcessOutputEvent.Debug(message, line));
            return;
        }

        if (IsLikelyDebugOutput(trimmed))
        {
            OutputReceived?.Invoke(this, ProcessOutputEvent.Debug(trimmed, line));
            return;
        }

        OutputReceived?.Invoke(this, ProcessOutputEvent.Unknown(trimmed));
    }

    public void Route(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            Route(line);
        }
    }

    private bool TryParseProgress(string line, out ProcessOutputEvent evt)
    {
        evt = null!;

        var match = ProgressPercentRegex.Match(line);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var percent) && percent >= 0 && percent <= 100)
        {
            evt = ProcessOutputEvent.Progress(percent, raw: line);
            return true;
        }

        match = WhisperProgressRegex.Match(line);
        if (match.Success && int.TryParse(match.Groups[1].Value, out percent) && percent >= 0 && percent <= 100)
        {
            evt = ProcessOutputEvent.Progress(percent, raw: line);
            return true;
        }

        match = FasterWhisperProgressRegex.Match(line);
        if (match.Success && int.TryParse(match.Groups[1].Value, out percent) && percent >= 0 && percent <= 100)
        {
            evt = ProcessOutputEvent.Progress(percent, raw: line);
            return true;
        }

        return false;
    }

    private static bool IsLikelyProgress(string line)
    {
        return line.Length < 10 &&
               (line.Contains('%') || Regex.IsMatch(line, @"^\d+$"));
    }

    private static bool IsLikelyDebugOutput(string line)
    {
        if (line.Contains("Loading", StringComparison.OrdinalIgnoreCase) && line.Contains("model", StringComparison.OrdinalIgnoreCase))
            return true;
        if (line.Contains("Audio", StringComparison.OrdinalIgnoreCase) && line.Contains("file", StringComparison.OrdinalIgnoreCase))
            return true;
        if (line.StartsWith("Done:", StringComparison.OrdinalIgnoreCase))
            return true;
        if (line.Contains("vosk", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static string ExtractMessage(string line, string prefix)
    {
        var idx = line.IndexOf(':', StringComparison.OrdinalIgnoreCase);
        if (idx >= 0 && idx < line.Length - 1)
            return line.Substring(idx + 1).Trim();
        return line;
    }

    private static string DecodeJsonEscapedString(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (!text.Contains("\\u", StringComparison.OrdinalIgnoreCase))
            return text;

        try
        {
            var result = System.Text.Json.JsonSerializer.Deserialize<string>(text);
            return result ?? text;
        }
        catch
        {
            return text;
        }
    }
}