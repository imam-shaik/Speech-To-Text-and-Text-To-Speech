using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class TranslationService : IDisposable
{
    private readonly ITranslationProvider _provider;
    private readonly TranslationQueue _queue;
    private readonly Dictionary<string, StreamingSrtWriter> _writers = new();
    private readonly object _lock = new();
    private bool _disposed;

    public ITranslationProvider Provider => _provider;
    public int PendingJobs => _queue.PendingCount;

    public event EventHandler<TranslationProgressEventArgs>? ProgressChanged;
    public event EventHandler<string>? TranslationCompleted;

    public TranslationService(ITranslationProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _queue = new TranslationQueue(provider);
    }

    public void EnqueueTranslation(Subtitle subtitle, string targetLanguage, int chunkIndex)
    {
        if (_disposed) return;
        _queue.Enqueue(subtitle, targetLanguage, chunkIndex);
    }

    public void RegisterWriter(string targetLanguage, StreamingSrtWriter writer)
    {
        lock (_lock)
        {
            _writers[targetLanguage] = writer;
        }
    }

    public async Task ProcessQueueAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested && !_disposed)
        {
            var translated = await _queue.DequeueAndTranslateAsync(ct);
            if (translated == null)
                break;

            var job = GetCurrentJob();
            if (job != null)
            {
                lock (_lock)
                {
                    if (_writers.TryGetValue(job.TargetLanguage, out var writer))
                    {
                        foreach (var p in translated.Paragraphs)
                        {
                            writer.Append(p);
                        }
                    }
                }

                TranslationCompleted?.Invoke(this, job.TargetLanguage);
            }
        }
    }

    public async Task<Subtitle?> TranslateDirectAsync(
        Subtitle source,
        string targetLanguage,
        CancellationToken ct = default)
    {
        try
        {
            return await _provider.TranslateAsync(source, targetLanguage, ct);
        }
        catch (TranslationException)
        {
            return null;
        }
    }

    public async Task<string?> TranslateTextDirectAsync(
        string text,
        string targetLanguage,
        CancellationToken ct = default)
    {
        try
        {
            return await _provider.TranslateTextAsync(text, targetLanguage, ct);
        }
        catch (TranslationException)
        {
            return null;
        }
    }

    private TranslationJob? _currentJob;

    private TranslationJob? GetCurrentJob()
    {
        lock (_lock)
        {
            return _currentJob;
        }
    }

    public void ClearQueue()
    {
        _queue.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _queue.Dispose();
        ClearQueue();
    }
}

public class TranslationProgressEventArgs : EventArgs
{
    public int PendingCount { get; }
    public int CompletedCount { get; }
    public string CurrentLanguage { get; }
    public int PercentComplete { get; }

    public TranslationProgressEventArgs(int pending, int completed, string language)
    {
        PendingCount = pending;
        CompletedCount = completed;
        CurrentLanguage = language;
        PercentComplete = pending + completed > 0
            ? (completed * 100) / (pending + completed)
            : 0;
    }
}