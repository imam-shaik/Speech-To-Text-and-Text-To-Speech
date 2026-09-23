using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed record TranslationJob(
    Subtitle Subtitle,
    string TargetLanguage,
    DateTime QueuedAt,
    int ChunkIndex);

public sealed class TranslationQueue : IDisposable
{
    private readonly ITranslationProvider _provider;
    private readonly ConcurrentQueue<TranslationJob> _jobs = new();
    private readonly ConcurrentDictionary<string, Subtitle> _translationCache = new();
    private bool _disposed;

    public int PendingCount => _jobs.Count;
    public ITranslationProvider Provider => _provider;

    public TranslationQueue(ITranslationProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public void Enqueue(Subtitle subtitle, string targetLanguage, int chunkIndex)
    {
        if (_disposed) return;

        var job = new TranslationJob(
            subtitle,
            targetLanguage,
            DateTime.Now,
            chunkIndex);

        _jobs.Enqueue(job);
    }

    public void EnqueueForAllTargets(Subtitle subtitle, int chunkIndex, params string[] targetLanguages)
    {
        foreach (var lang in targetLanguages)
        {
            Enqueue(subtitle, lang, chunkIndex);
        }
    }

    public async Task<Subtitle?> DequeueAndTranslateAsync(CancellationToken ct = default)
    {
        if (_disposed || !_jobs.TryDequeue(out var job))
            return null;

        try
        {
            var cacheKey = $"{job.TargetLanguage}_{job.ChunkIndex}";

            if (_translationCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var translated = await _provider.TranslateAsync(job.Subtitle, job.TargetLanguage, ct);

            _translationCache.TryAdd(cacheKey, translated);

            return translated;
        }
        catch (OperationCanceledException)
        {
            _jobs.Enqueue(job);
            throw;
        }
        catch (TranslationException)
        {
            return null;
        }
    }

    public void Clear()
    {
        while (_jobs.TryDequeue(out _)) { }
        _translationCache.Clear();
    }

    public Subtitle? GetCachedTranslation(string targetLanguage, int chunkIndex)
    {
        var key = $"{targetLanguage}_{chunkIndex}";
        return _translationCache.TryGetValue(key, out var result) ? result : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
    }
}