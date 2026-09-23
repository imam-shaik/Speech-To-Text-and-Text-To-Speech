using System;
using System.Collections.Generic;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class ChunkBoundary
{
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public TimeSpan Duration => End - Start;
}

public sealed class SilenceAwareChunker
{
    private readonly TimeSpan _minChunkDuration;
    private readonly TimeSpan _maxChunkDuration;
    private readonly TimeSpan _minSilenceDuration;
    private readonly double _silenceThresholdDb;

    public TimeSpan MinChunkDuration => _minChunkDuration;
    public TimeSpan MaxChunkDuration => _maxChunkDuration;

    public SilenceAwareChunker(
        TimeSpan minChunkDuration = default,
        TimeSpan maxChunkDuration = default,
        TimeSpan minSilenceDuration = default,
        double silenceThresholdDb = default)
    {
        _minChunkDuration = minChunkDuration == default ? TimeSpan.FromSeconds(20) : minChunkDuration;
        _maxChunkDuration = maxChunkDuration == default ? TimeSpan.FromSeconds(60) : maxChunkDuration;
        _minSilenceDuration = minSilenceDuration == default ? TimeSpan.FromMilliseconds(300) : minSilenceDuration;
        _silenceThresholdDb = silenceThresholdDb == default ? -40.0 : silenceThresholdDb;
    }

    public List<ChunkBoundary> CalculateChunkBoundaries(TimeSpan videoDuration, IReadOnlyList<(TimeSpan start, TimeSpan end)> silenceRegions, Action<string>? log = null)
    {
        log ??= s => System.Diagnostics.Debug.WriteLine(s);
        log("[CHUNKER] ===============================================");
        log("[CHUNKER] CalculateChunkBoundaries ENTERED");
        log($"[CHUNKER]   Video Duration: {videoDuration}");
        log($"[CHUNKER]   Video Duration Ticks: {videoDuration.Ticks}");
        log($"[CHUNKER]   Min Chunk Duration: {_minChunkDuration}");
        log($"[CHUNKER]   Max Chunk Duration: {_maxChunkDuration}");
        log($"[CHUNKER]   Min Silence Duration: {_minSilenceDuration}");
        log($"[CHUNKER]   Silence Regions Count: {silenceRegions.Count}");

        if (videoDuration <= TimeSpan.Zero)
        {
            log("[CHUNKER] ERROR: Video duration is ZERO or NEGATIVE!");
            log("[CHUNKER] RETURNING EMPTY LIST");
            return new List<ChunkBoundary>();
        }

        var chunks = new List<ChunkBoundary>();
        var currentStart = TimeSpan.Zero;

        if (videoDuration < _minChunkDuration)
        {
            log($"[CHUNKER] WARNING: Video duration ({videoDuration}) < Min Chunk Duration ({_minChunkDuration})");
        }

        log($"[CHUNKER] Fallback Mode: {silenceRegions.Count == 0}");

        int iteration = 0;
        while (currentStart < videoDuration)
        {
            iteration++;
            if (iteration > 1000)
            {
                log("[CHUNKER] ERROR: Infinite loop detected (>1000 iterations)");
                break;
            }

            var targetEnd = currentStart + _maxChunkDuration;
            var actualEnd = FindBestBoundary(currentStart, targetEnd, silenceRegions, videoDuration);

            log($"[CHUNKER]   Chunk {chunks.Count + 1}: Start={currentStart}, TargetEnd={targetEnd}, ActualEnd={actualEnd}");

            if (chunks.Count > 0 && actualEnd - currentStart < _minChunkDuration)
            {
                var lastChunk = chunks[^1];
                var mergedEnd = actualEnd;
                if (mergedEnd - lastChunk.Start <= _maxChunkDuration)
                {
                    chunks[^1] = new ChunkBoundary { Start = lastChunk.Start, End = mergedEnd };
                    log($"[CHUNKER]     Merged with previous chunk. New end: {mergedEnd}");
                }
                else
                {
                    chunks.Add(new ChunkBoundary { Start = currentStart, End = mergedEnd });
                    log($"[CHUNKER]     Added new chunk (small residual)");
                }
                currentStart = mergedEnd;
            }
            else
            {
                chunks.Add(new ChunkBoundary { Start = currentStart, End = actualEnd });
                log($"[CHUNKER]     Added chunk. Duration: {actualEnd - currentStart}");
                currentStart = actualEnd;
            }
        }

        log($"[CHUNKER] Total chunks generated: {chunks.Count}");
        for (int i = 0; i < Math.Min(3, chunks.Count); i++)
        {
            log($"[CHUNKER]   Chunk {i + 1}: Start={chunks[i].Start}, End={chunks[i].End}, Duration={chunks[i].Duration}");
        }
        if (chunks.Count > 3)
        {
            log($"[CHUNKER]   ... and {chunks.Count - 3} more chunks");
        }
        log("[CHUNKER] CalculateChunkBoundaries EXIT");
        log("[CHUNKER] ===============================================");

        return chunks;
    }

    private TimeSpan FindBestBoundary(TimeSpan chunkStart, TimeSpan targetEnd, IReadOnlyList<(TimeSpan start, TimeSpan end)> silenceRegions, TimeSpan videoDuration)
    {
        var maxEnd = chunkStart + _maxChunkDuration;
        var minEnd = chunkStart + _minChunkDuration;
        var searchStart = chunkStart + TimeSpan.FromSeconds(15);
        var searchEnd = chunkStart + _maxChunkDuration;

        if (searchEnd > videoDuration)
            searchEnd = videoDuration;

        if (searchStart >= searchEnd)
            return searchEnd;

        TimeSpan bestBoundary = searchEnd;
        var bestDistance = double.MaxValue;

        foreach (var silence in silenceRegions)
        {
            if (silence.start < searchStart || silence.start >= searchEnd)
                continue;

            if (silence.end - silence.start < _minSilenceDuration)
                continue;

            var distance = Math.Abs((silence.start - targetEnd).TotalSeconds);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestBoundary = silence.start;
            }
        }

        if (bestBoundary < minEnd)
            bestBoundary = searchEnd;

        if (bestBoundary > maxEnd)
            bestBoundary = maxEnd;

        if (bestBoundary > videoDuration)
            bestBoundary = videoDuration;

        return bestBoundary;
    }

    public static TimeSpan FindNextSilenceEnd(TimeSpan from, TimeSpan maxSearch, IReadOnlyList<(TimeSpan start, TimeSpan end)> silenceRegions)
    {
        foreach (var silence in silenceRegions)
        {
            if (silence.start >= from && silence.start <= maxSearch)
                return silence.end;
        }
        return maxSearch;
    }
}