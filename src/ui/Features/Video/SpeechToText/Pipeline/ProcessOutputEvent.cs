using System;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public enum ProcessOutputKind
{
    Progress,
    Transcript,
    Error,
    Warning,
    Debug,
    Unknown
}

public sealed record ProcessOutputEvent
{
    public ProcessOutputKind Kind { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Raw { get; init; } = string.Empty;

    public int? ProgressPercent { get; init; }
    public double? EstimatedTotalSeconds { get; init; }
    public string? Text { get; init; }
    public string? Message { get; init; }
    public string? Line { get; init; }

    public static ProcessOutputEvent Progress(int percent, double? estimatedTotalSeconds = null, string? raw = null) =>
        new()
        {
            Kind = ProcessOutputKind.Progress,
            ProgressPercent = percent,
            EstimatedTotalSeconds = estimatedTotalSeconds,
            Raw = raw ?? $"{percent}%"
        };

    public static ProcessOutputEvent Transcript(string text, string? raw = null) =>
        new()
        {
            Kind = ProcessOutputKind.Transcript,
            Text = text,
            Raw = raw ?? $"TEXT: {text}"
        };

    public static ProcessOutputEvent Error(string message, string? raw = null) =>
        new()
        {
            Kind = ProcessOutputKind.Error,
            Message = message,
            Raw = raw ?? $"ERROR: {message}"
        };

    public static ProcessOutputEvent Warning(string message, string? raw = null) =>
        new()
        {
            Kind = ProcessOutputKind.Warning,
            Message = message,
            Raw = raw ?? $"WARNING: {message}"
        };

    public static ProcessOutputEvent Debug(string message, string? raw = null) =>
        new()
        {
            Kind = ProcessOutputKind.Debug,
            Message = message,
            Raw = raw ?? $"DEBUG: {message}"
        };

    public static ProcessOutputEvent Unknown(string line) =>
        new()
        {
            Kind = ProcessOutputKind.Unknown,
            Line = line,
            Raw = line
        };
}