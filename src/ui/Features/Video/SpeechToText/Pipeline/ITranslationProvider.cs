using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public interface ITranslationProvider
{
    string Name { get; }
    IReadOnlyList<string> SupportedSourceLanguages { get; }
    IReadOnlyList<string> SupportedTargetLanguages { get; }
    bool IsInstalled { get; }
    bool RequiresInternet { get; }

    Task<Subtitle> TranslateAsync(
        Subtitle source,
        string targetLanguage,
        CancellationToken ct = default);

    Task<string> TranslateTextAsync(
        string text,
        string targetLanguage,
        CancellationToken ct = default);

    string GetModelInfo();
}

public static class TranslationProviderExtensions
{
    public static bool SupportsLanguagePair(this ITranslationProvider provider, string sourceLang, string targetLang)
    {
        return provider.SupportedSourceLanguages.Contains(sourceLang) &&
               provider.SupportedTargetLanguages.Contains(targetLang);
    }
}

public class TranslationException : Exception
{
    public string? SourceText { get; }
    public string? TargetLanguage { get; }

    public TranslationException(string message) : base(message) { }

    public TranslationException(string message, Exception innerException)
        : base(message, innerException) { }

    public TranslationException(string message, string sourceText, string targetLanguage)
        : base(message)
    {
        SourceText = sourceText;
        TargetLanguage = targetLanguage;
    }
}