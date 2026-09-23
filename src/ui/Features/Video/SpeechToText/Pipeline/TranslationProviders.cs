using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class NullTranslationProvider : ITranslationProvider
{
    public string Name => "None";
    public IReadOnlyList<string> SupportedSourceLanguages => Array.Empty<string>();
    public IReadOnlyList<string> SupportedTargetLanguages => Array.Empty<string>();
    public bool IsInstalled => false;
    public bool RequiresInternet => false;

    public Task<Subtitle> TranslateAsync(Subtitle source, string targetLanguage, CancellationToken ct = default)
    {
        return Task.FromResult(source);
    }

    public Task<string> TranslateTextAsync(string text, string targetLanguage, CancellationToken ct = default)
    {
        return Task.FromResult(text);
    }

    public string GetModelInfo() => "No translation provider installed";
}

public sealed class PassthroughTranslationProvider : ITranslationProvider
{
    private readonly string _targetLanguage;

    public string Name => "Passthrough";
    public IReadOnlyList<string> SupportedSourceLanguages => new[] { "en", "hi", "es", "fr", "de" };
    public IReadOnlyList<string> SupportedTargetLanguages => new[] { "en", "hi", "es", "fr", "de" };
    public bool IsInstalled => true;
    public bool RequiresInternet => false;

    public PassthroughTranslationProvider(string targetLanguage = "en")
    {
        _targetLanguage = targetLanguage;
    }

    public Task<Subtitle> TranslateAsync(Subtitle source, string targetLanguage, CancellationToken ct = default)
    {
        var result = new Subtitle();
        foreach (var p in source.Paragraphs)
        {
            result.Paragraphs.Add(new Paragraph($"[{targetLanguage}] {p.Text}", p.StartTime.TotalMilliseconds, p.EndTime.TotalMilliseconds));
        }
        return Task.FromResult(result);
    }

    public Task<string> TranslateTextAsync(string text, string targetLanguage, CancellationToken ct = default)
    {
        return Task.FromResult($"[{targetLanguage}] {text}");
    }

    public string GetModelInfo() => "Passthrough mode - text is marked with language prefix";
}