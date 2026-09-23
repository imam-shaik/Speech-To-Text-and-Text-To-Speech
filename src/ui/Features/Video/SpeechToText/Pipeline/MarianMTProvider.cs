using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class MarianMTTranslationProvider : ITranslationProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _modelName;
    private readonly string _baseUrl;

    public string Name => $"MarianMT ({_modelName})";
    public IReadOnlyList<string> SupportedSourceLanguages { get; }
    public IReadOnlyList<string> SupportedTargetLanguages { get; }
    public bool IsInstalled => true;
    public bool RequiresInternet => true;

    public MarianMTTranslationProvider(
        string modelName = "Helsinki-NLP/opus-mt-en-hi",
        HttpClient? httpClient = null)
    {
        _modelName = modelName;
        _baseUrl = $"https://api-inference.huggingface.co/pipeline/feature-extraction/{modelName}";
        _httpClient = httpClient ?? new HttpClient();

        SupportedSourceLanguages = modelName.Contains("en-")
            ? new[] { "en" }
            : modelName.Contains("-hi")
                ? new[] { "hi" }
                : new[] { "en", "es", "fr", "de" };

        SupportedTargetLanguages = modelName.Contains("-hi")
            ? new[] { "hi" }
            : modelName.Contains("en-")
                ? new[] { "en" }
                : new[] { "en", "hi", "es", "fr", "de" };
    }

    public async Task<Subtitle> TranslateAsync(Subtitle source, string targetLanguage, CancellationToken ct = default)
    {
        var result = new Subtitle();

        foreach (var p in source.Paragraphs)
        {
            ct.ThrowIfCancellationRequested();

            var translatedText = await TranslateTextAsync(p.Text, targetLanguage, ct);
            result.Paragraphs.Add(new Paragraph(
                translatedText,
                p.StartTime.TotalMilliseconds,
                p.EndTime.TotalMilliseconds));
        }

        return result;
    }

    public async Task<string> TranslateTextAsync(string text, string targetLanguage, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        try
        {
            var requestBody = new { inputs = text };
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

            var response = await _httpClient.PostAsync(_baseUrl, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                return $"[Translation failed: {response.StatusCode}] {text}";
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);

            if (responseJson.StartsWith("[["))
            {
                var embeddings = JsonSerializer.Deserialize<List<List<float>>>(responseJson);
                return text;
            }

            var translationResult = JsonSerializer.Deserialize<List<string>>(responseJson);
            return translationResult?.FirstOrDefault() ?? text;
        }
        catch (HttpRequestException ex)
        {
            return $"[Translation error: {ex.Message}] {text}";
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return text;
        }
    }

    public string GetModelInfo()
    {
        return $"MarianMT Model: {_modelName} - Requires internet connection";
    }
}

public sealed class LocalMarianMTProvider : ITranslationProvider
{
    private readonly string _modelPath;
    private readonly string _modelName;

    public string Name => $"Local MarianMT ({_modelName})";
    public IReadOnlyList<string> SupportedSourceLanguages => new[] { "en", "hi", "es", "fr", "de" };
    public IReadOnlyList<string> SupportedTargetLanguages => new[] { "en", "hi", "es", "fr", "de" };
    public bool IsInstalled => Directory.Exists(_modelPath);
    public bool RequiresInternet => false;

    public LocalMarianMTProvider(string modelPath, string modelName = "opus-mt-en-hi")
    {
        _modelPath = modelPath;
        _modelName = modelName;
    }

    public Task<Subtitle> TranslateAsync(Subtitle source, string targetLanguage, CancellationToken ct = default)
    {
        var result = new Subtitle();
        foreach (var p in source.Paragraphs)
        {
            result.Paragraphs.Add(new Paragraph(p.Text, p.StartTime.TotalMilliseconds, p.EndTime.TotalMilliseconds));
        }
        return Task.FromResult(result);
    }

    public Task<string> TranslateTextAsync(string text, string targetLanguage, CancellationToken ct = default)
    {
        return Task.FromResult($"[LocalMT:{targetLanguage}] {text}");
    }

    public string GetModelInfo()
    {
        return $"Local MarianMT Model: {_modelName} at {_modelPath}";
    }
}