using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Timer = System.Timers.Timer;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class RuntimeMetrics : IDisposable
{
    private readonly Timer _timer;
    private readonly List<RuntimeSample> _samples;
    private readonly string _tempPath;
    private bool _disposed;

    public RuntimeMetrics(int intervalMs = 2000, string? tempPath = null)
    {
        _tempPath = tempPath ?? Path.GetTempPath();
        _samples = new List<RuntimeSample>();
        _timer = new Timer(intervalMs);
        _timer.Elapsed += (_, _) => _samples.Add(Sample());
    }

    public void Start()
    {
        _samples.Clear();
        _timer.Start();
        _samples.Add(Sample());
    }

    public void Stop()
    {
        _timer.Stop();
        _samples.Add(Sample());
    }

    public RuntimeReport GetReport()
    {
        if (_samples.Count < 2)
            return new RuntimeReport(
                DateTime.MinValue, DateTime.MinValue, TimeSpan.Zero,
                0, 0, 0, 0, 0,
                0, 0,
                Array.Empty<RuntimeSample>());

        var cpuValues = _samples.Select(s => s.CpuPercent).ToArray();
        var memValues = _samples.Select(s => s.WorkingSetMb).ToArray();
        var diskValues = _samples.Select(s => s.TempFileCount).ToArray();

        Array.Sort(cpuValues);
        Array.Sort(memValues);

        return new RuntimeReport(
            _samples.First().Timestamp,
            _samples.Last().Timestamp,
            _samples.Last().Timestamp - _samples.First().Timestamp,
            memValues.Min(),
            memValues.Max(),
            memValues[memValues.Length / 2],
            cpuValues.Max(),
            cpuValues.Average(),
            diskValues.Max(),
            diskValues.Sum() > 0 ? diskValues.Max() : 0,
            _samples.ToArray());
    }

    public TempFileSnapshot TakeTempSnapshot()
    {
        var files = Directory.GetFiles(_tempPath, "*.wav")
            .Concat(Directory.GetFiles(_tempPath, "*.checkpoint*"))
            .Concat(Directory.GetFiles(_tempPath, "*.srt"))
            .ToList();

        return new TempFileSnapshot(DateTime.Now, files.Count, files);
    }

    private RuntimeSample Sample()
    {
        var process = Process.GetCurrentProcess();
        var cpuTime = process.TotalProcessorTime;
        var now = DateTime.Now;

        var tempFiles = Directory.GetFiles(_tempPath, "*.wav")
            .Concat(Directory.GetFiles(_tempPath, "*.checkpoint*"))
            .Count();

        return new RuntimeSample(
            now,
            process.WorkingSet64 / (1024.0 * 1024.0),
            process.PrivateMemorySize64 / (1024.0 * 1024.0),
            GetProcessCpuPercent(process, cpuTime),
            tempFiles,
            GetDiskUsage(_tempPath));
    }

    private static double GetProcessCpuPercent(Process process, TimeSpan previousTotalTime)
    {
        try
        {
            var currentTotalTime = process.TotalProcessorTime;
            var cpuUsedMs = (currentTotalTime - previousTotalTime).TotalMilliseconds;
            var elapsedMs = (DateTime.Now - process.StartTime).TotalMilliseconds;
            return Math.Min(100, cpuUsedMs / elapsedMs * 100);
        }
        catch
        {
            return 0;
        }
    }

    private static long GetDiskUsage(string path)
    {
        try
        {
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _timer.Stop();
        _timer.Dispose();
        _disposed = true;
    }
}

public record RuntimeSample(
    DateTime Timestamp,
    double WorkingSetMb,
    double PrivateMb,
    double CpuPercent,
    int TempFileCount,
    long DiskUsageBytes);

public record RuntimeReport(
    DateTime StartTime,
    DateTime EndTime,
    TimeSpan Duration,
    double MinMemoryMb,
    double MaxMemoryMb,
    double MedianMemoryMb,
    double PeakCpuPercent,
    double AverageCpuPercent,
    int PeakTempFiles,
    int FinalTempFiles,
    RuntimeSample[] Samples)
{
    public long DiskUsageMb => Samples.Sum(s => s.DiskUsageBytes) / (1024 * 1024);

    public override string ToString()
    {
        return $"Duration: {Duration:mm\\:ss}, Memory: {MinMemoryMb:F0}-{MaxMemoryMb:F0}MB (median {MedianMemoryMb:F0}), " +
               $"CPU: {AverageCpuPercent:F0}% avg, Peak temp files: {PeakTempFiles}, Final temp files: {FinalTempFiles}";
    }
}

public record TempFileSnapshot(DateTime Timestamp, int FileCount, List<string> Files);