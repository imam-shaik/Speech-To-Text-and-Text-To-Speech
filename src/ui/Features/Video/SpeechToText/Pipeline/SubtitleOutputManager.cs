using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class SubtitleOutputManager : IDisposable
{
    private readonly string _outputDirectory;
    private readonly string _baseFileName;
    private readonly string _sourceLanguage;

    private StreamingSrtWriter? _originalWriter;
    private readonly Dictionary<string, StreamingSrtWriter> _translationWriters = new();
    private StreamingSrtWriter? _bilingualWriter;

    private readonly object _lock = new();
    private bool _disposed;

    public string OriginalFilePath { get; private set; } = string.Empty;
    public string SourceLanguage => _sourceLanguage;

    public IReadOnlyDictionary<string, string> TranslationFilePaths => _translationFilePaths;
    private readonly Dictionary<string, string> _translationFilePaths = new();

    public string? BilingualFilePath { get; private set; }

    public bool HasOriginal => _originalWriter != null;
    public bool HasTranslations => _translationWriters.Count > 0;
    public bool HasBilingual => _bilingualWriter != null;

    public SubtitleOutputManager(
        string outputDirectory,
        string baseFileName,
        string sourceLanguage)
    {
        _outputDirectory = outputDirectory;
        _baseFileName = baseFileName;
        _sourceLanguage = sourceLanguage;
    }

    public void InitializeOriginal()
    {
        lock (_lock)
        {
            if (_originalWriter != null) return;

            OriginalFilePath = Path.Combine(_outputDirectory, $"{_baseFileName}.{_sourceLanguage}.srt");
            _originalWriter = new StreamingSrtWriter(OriginalFilePath);
        }
    }

    public void AddTargetLanguage(string targetLanguage)
    {
        if (string.IsNullOrEmpty(targetLanguage) || targetLanguage == _sourceLanguage)
            return;

        lock (_lock)
        {
            if (_translationWriters.ContainsKey(targetLanguage)) return;

            var filePath = Path.Combine(_outputDirectory, $"{_baseFileName}.{targetLanguage}.srt");
            _translationFilePaths[targetLanguage] = filePath;
            _translationWriters[targetLanguage] = new StreamingSrtWriter(filePath);
        }
    }

    public void EnableBilingual(string targetLanguage)
    {
        if (string.IsNullOrEmpty(targetLanguage) || targetLanguage == _sourceLanguage)
            return;

        lock (_lock)
        {
            if (_bilingualWriter != null) return;

            BilingualFilePath = Path.Combine(_outputDirectory, $"{_baseFileName}.{_sourceLanguage}-{targetLanguage}.srt");
            _bilingualWriter = new StreamingSrtWriter(BilingualFilePath);
        }
    }

    public void WriteOriginal(Subtitle subtitle)
    {
        if (_originalWriter == null)
            InitializeOriginal();

        lock (_lock)
        {
            foreach (var p in subtitle.Paragraphs)
            {
                _originalWriter?.Append(p);
            }
        }
    }

    public void WriteTranslation(string targetLanguage, Subtitle translatedSubtitle)
    {
        if (!_translationWriters.TryGetValue(targetLanguage, out var writer))
            return;

        lock (_lock)
        {
            foreach (var p in translatedSubtitle.Paragraphs)
            {
                writer.Append(p);
            }
        }
    }

    public void WriteBilingual(Subtitle original, Subtitle translated)
    {
        if (_bilingualWriter == null)
            return;

        lock (_lock)
        {
            var count = Math.Min(original.Paragraphs.Count, translated.Paragraphs.Count);
            for (var i = 0; i < count; i++)
            {
                var orig = original.Paragraphs[i];
                var trans = translated.Paragraphs[i];

                var bilingualParagraph = new Paragraph(
                    $"{orig.Text}\n{trans.Text}",
                    orig.StartTime.TotalMilliseconds,
                    orig.EndTime.TotalMilliseconds);

                _bilingualWriter.Append(bilingualParagraph);
            }
        }
    }

    public async Task FlushAndCloseAsync(CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            lock (_lock)
            {
                _originalWriter?.Dispose();
                _originalWriter = null;

                foreach (var writer in _translationWriters.Values)
                {
                    writer.Dispose();
                }
                _translationWriters.Clear();

                _bilingualWriter?.Dispose();
                _bilingualWriter = null;
            }
        }, ct);
    }

    public void Flush()
    {
        lock (_lock)
        {
            _originalWriter?.Flush();
            foreach (var writer in _translationWriters.Values)
            {
                writer.Flush();
            }
            _bilingualWriter?.Flush();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        Flush();
        FlushAndCloseAsync().GetAwaiter().GetResult();

        _disposed = true;
    }
}