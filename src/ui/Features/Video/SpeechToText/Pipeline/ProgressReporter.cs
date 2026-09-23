using System;
using System.Diagnostics;
using System.Text;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public enum TranscriptionStage
{
    Initializing,
    ExtractingAudio,
    Transcribing,
    Translating,
    SavingCheckpoint,
    PostProcessing,
    Complete,
    Cancelled,
    Failed
}

public sealed class TranscriptionProgress
{
    public int CurrentChunk { get; set; }
    public int TotalChunks { get; set; }
    public TimeSpan Elapsed { get; set; }
    public TimeSpan EstimatedRemaining { get; set; }
    public double SpeedMultiplier { get; set; }
    public DateTime EstimatedFinishTime { get; set; }
    public TranscriptionStage Stage { get; set; }
    public int SubtitleCount { get; set; }
    public string CurrentSubtitlePreview { get; set; } = string.Empty;
    public double ChunkPercent => TotalChunks > 0
        ? (double)CurrentChunk / TotalChunks * 100.0
        : 0;
    public double EnginePercent { get; set; } = -1;
    public double OverallPercent
    {
        get
        {
            if (EnginePercent >= 0)
                return EnginePercent;
            return ChunkPercent;
        }
    }
}

public sealed class ProgressReporter
{
    private readonly Stopwatch _stopwatch;
    private int _currentChunk;
    private int _totalChunks;
    private int _subtitleCount;
    private TranscriptionStage _stage;
    private string _currentSubtitlePreview = string.Empty;
    private double _externalProgressPercent = -1;
    private readonly object _lock = new();

    public event EventHandler<TranscriptionProgress>? ProgressChanged;

    public int CurrentChunk => _currentChunk;
    public int TotalChunks => _totalChunks;
    public int SubtitleCount => _subtitleCount;
    public TranscriptionStage Stage => _stage;
    public double ExternalProgressPercent => _externalProgressPercent;

    public ProgressReporter()
    {
        _stopwatch = new Stopwatch();
    }

    public void AttachOutputRouter(ProcessOutputRouter router)
    {
        DateTime _lastProgressReport = DateTime.MinValue;
        DateTime _lastTranscriptReport = DateTime.MinValue;
        int _lastReportedPercent = -1;
        string _lastReportedText = string.Empty;
        const int MinProgressIntervalMs = 300;
        const int MinTranscriptIntervalMs = 200;

        router.OutputReceived += (_, e) =>
        {
            switch (e.Kind)
            {
                case ProcessOutputKind.Progress:
                    if (e.ProgressPercent.HasValue)
                    {
                        lock (_lock)
                        {
                            _externalProgressPercent = e.ProgressPercent.Value;
                        }
                        var now = DateTime.Now;
                        var timeSinceLast = (now - _lastProgressReport).TotalMilliseconds;
                        if (timeSinceLast >= MinProgressIntervalMs || Math.Abs(_lastReportedPercent - e.ProgressPercent.Value) >= 10)
                        {
                            _lastProgressReport = now;
                            _lastReportedPercent = e.ProgressPercent.Value;
                            Report();
                        }
                    }
                    break;

                case ProcessOutputKind.Transcript:
                    if (!string.IsNullOrEmpty(e.Text) && e.Text != _lastReportedText)
                    {
                        lock (_lock)
                        {
                            _currentSubtitlePreview = e.Text;
                        }
                        var now = DateTime.Now;
                        var timeSinceLast = (now - _lastTranscriptReport).TotalMilliseconds;
                        if (timeSinceLast >= MinTranscriptIntervalMs)
                        {
                            _lastTranscriptReport = now;
                            _lastReportedText = e.Text;
                            Report();
                        }
                    }
                    break;

                case ProcessOutputKind.Error:
                    lock (_lock)
                    {
                        _stage = TranscriptionStage.Failed;
                    }
                    Report();
                    break;
            }
        };
    }

    public void Start(int totalChunks)
    {
        lock (_lock)
        {
            _totalChunks = totalChunks;
            _currentChunk = 0;
            _subtitleCount = 0;
            _stage = TranscriptionStage.Initializing;
        }
        _stopwatch.Restart();
        Report();
    }

    public void SetStage(TranscriptionStage stage)
    {
        lock (_lock)
        {
            _stage = stage;
        }
        Report();
    }

    public void UpdateChunk(int chunkNumber, int subtitleCount, string currentSubtitlePreview = "")
    {
        lock (_lock)
        {
            _currentChunk = chunkNumber;
            _subtitleCount = subtitleCount;
            if (!string.IsNullOrEmpty(currentSubtitlePreview))
                _currentSubtitlePreview = currentSubtitlePreview;
        }
        Report();
    }

    public void IncrementSubtitleCount(int count = 1)
    {
        lock (_lock)
        {
            _subtitleCount += count;
        }
        Report();
    }

    public void Stop()
    {
        _stopwatch.Stop();
        lock (_lock)
        {
            _stage = TranscriptionStage.Complete;
        }
        Report();
    }

    public void Cancel()
    {
        _stopwatch.Stop();
        lock (_lock)
        {
            _stage = TranscriptionStage.Cancelled;
        }
        Report();
    }

    public void Fail()
    {
        _stopwatch.Stop();
        lock (_lock)
        {
            _stage = TranscriptionStage.Failed;
        }
        Report();
    }

    public TranscriptionProgress GetProgress()
    {
        var elapsed = _stopwatch.Elapsed;
        int currentChunk;
        int totalChunks;
        int subtitleCount;
        TranscriptionStage stage;
        string currentSubtitlePreview;
        double externalProgressPercent;

        lock (_lock)
        {
            currentChunk = _currentChunk;
            totalChunks = _totalChunks;
            subtitleCount = _subtitleCount;
            stage = _stage;
            currentSubtitlePreview = _currentSubtitlePreview;
            externalProgressPercent = _externalProgressPercent;
        }

        var chunkBasedPercent = totalChunks > 0
            ? Math.Max(0, Math.Min(100, (double)currentChunk / totalChunks * 100.0))
            : 0.0;

        var combinedPercent = externalProgressPercent >= 0
            ? externalProgressPercent
            : chunkBasedPercent;

        var estimatedRemaining = combinedPercent > 0 && combinedPercent < 100
            ? TimeSpan.FromMilliseconds(elapsed.TotalMilliseconds / combinedPercent * (100.0 - combinedPercent))
            : TimeSpan.Zero;
        var speedMultiplier = currentChunk > 0 && elapsed.TotalSeconds > 0
            ? (currentChunk * 30.0) / elapsed.TotalSeconds
            : 0;
        var eta = combinedPercent > 0 && combinedPercent < 100 && estimatedRemaining > TimeSpan.Zero
            ? DateTime.Now.Add(estimatedRemaining)
            : DateTime.MinValue;

        return new TranscriptionProgress
        {
            CurrentChunk = totalChunks > 0 ? Math.Max(0, Math.Min(totalChunks, currentChunk)) : 0,
            TotalChunks = totalChunks,
            Elapsed = elapsed,
            EstimatedRemaining = estimatedRemaining,
            SpeedMultiplier = speedMultiplier,
            EstimatedFinishTime = eta,
            Stage = stage,
            SubtitleCount = subtitleCount,
            CurrentSubtitlePreview = currentSubtitlePreview,
            EnginePercent = externalProgressPercent
        };
    }

    public string GetDetailedReport()
    {
        var p = GetProgress();
        var sb = new StringBuilder();

        if (p.TotalChunks > 0)
        {
            sb.AppendLine($"Processing chunk {p.CurrentChunk} / {p.TotalChunks}");
        }

        sb.AppendLine();
        sb.AppendLine($"Elapsed: {FormatTimeSpan(p.Elapsed)}");

        if (p.EstimatedRemaining > TimeSpan.Zero || p.CurrentChunk > 0)
        {
            sb.AppendLine($"Remaining: {FormatTimeSpan(p.EstimatedRemaining)}");
        }

        if (p.SpeedMultiplier > 0)
        {
            sb.AppendLine($"Speed: {p.SpeedMultiplier:F1}x");
        }

        if (p.EstimatedFinishTime != DateTime.MinValue && p.CurrentChunk > 0)
        {
            sb.AppendLine($"ETA: {p.EstimatedFinishTime:h:mm tt}");
        }

        sb.AppendLine();
        sb.AppendLine($"Stage: {p.Stage}");
        sb.AppendLine($"Output: {p.SubtitleCount:N0} subtitles");

        return sb.ToString();
    }

    public string GetShortSummary()
    {
        var p = GetProgress();
        return $"Chunk {p.CurrentChunk}/{p.TotalChunks} | {p.SubtitleCount} subs | {FormatTimeSpan(p.Elapsed)} elapsed";
    }

    private void Report()
    {
        ProgressChanged?.Invoke(this, GetProgress());
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
        if (ts.TotalMinutes >= 1)
            return $"{ts.Minutes}m {ts.Seconds}s";
        return $"{ts.Seconds}s";
    }
}