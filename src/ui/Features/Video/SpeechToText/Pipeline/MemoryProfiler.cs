using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class MemoryProfiler : IDisposable
{
    private readonly Timer _timer;
    private readonly List<MemorySample> _samples;
    private bool _disposed;

    public IReadOnlyList<MemorySample> Samples => _samples;

    public MemoryProfiler(int intervalMs = 5000)
    {
        _samples = new List<MemorySample>();
        _timer = new Timer(intervalMs);
        _timer.Elapsed += OnTimerElapsed;
    }

    public void Start()
    {
        _samples.Clear();
        _timer.Start();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _samples.Add(Sample());
    }

    public void Stop()
    {
        _timer.Stop();
        _samples.Add(Sample());
    }

    public MemoryReport GetReport()
    {
        if (_samples.Count < 2)
            return new MemoryReport(0, 0, 0, 0, 0, Array.Empty<MemorySample>());

        var workingSets = _samples.Select(s => s.WorkingSetMb).ToArray();
        var gcHeaps = _samples.Select(s => s.GcHeapMb).ToArray();
        var privateBytes = _samples.Select(s => s.PrivateMb).ToArray();

        Array.Sort(workingSets);
        Array.Sort(gcHeaps);
        Array.Sort(privateBytes);

        var medianIndex = _samples.Count / 2;

        return new MemoryReport(
            workingSets.Min(),
            workingSets.Max(),
            workingSets[medianIndex],
            workingSets[^1] - workingSets[0],
            gcHeaps[^1] - gcHeaps[0],
            _samples.ToArray());
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        _samples.Add(Sample());
    }

    private MemorySample Sample()
    {
        var process = Process.GetCurrentProcess();
        var gcMemory = GC.GetTotalMemory(false);

        return new MemorySample(
            DateTime.Now,
            process.WorkingSet64 / (1024.0 * 1024.0),
            gcMemory / (1024.0 * 1024.0),
            process.PrivateMemorySize64 / (1024.0 * 1024.0));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _timer.Stop();
        _timer.Dispose();
        _disposed = true;
    }
}

public record MemorySample(
    DateTime Timestamp,
    double WorkingSetMb,
    double GcHeapMb,
    double PrivateMb);

public record MemoryReport(
    double MinWorkingSetMb,
    double MaxWorkingSetMb,
    double MedianWorkingSetMb,
    double WorkingSetDeltaMb,
    double GcHeapDeltaMb,
    MemorySample[] Samples)
{
    public bool IsStable(double maxDeltaMb = 50)
    {
        return WorkingSetDeltaMb < maxDeltaMb && GcHeapDeltaMb < maxDeltaMb;
    }

    public override string ToString()
    {
        return $"Memory: min={MinWorkingSetMb:F1}MB, max={MaxWorkingSetMb:F1}MB, median={MedianWorkingSetMb:F1}MB, delta={WorkingSetDeltaMb:F1}MB";
    }
}