using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class CleanupValidator
{
    private readonly string _tempPath;

    public CleanupValidator(string? tempPath = null)
    {
        _tempPath = tempPath ?? Path.GetTempPath();
    }

    public CleanupReport ValidateTempFiles(IEnumerable<string> expectedChunkFiles)
    {
        var actualFiles = Directory.GetFiles(_tempPath, "*.wav")
            .Concat(Directory.GetFiles(_tempPath, "*.checkpoint*"))
            .Concat(Directory.GetFiles(_tempPath, "*.srt"))
            .ToList();

        var expectedList = expectedChunkFiles.ToList();
        var unexpectedFiles = actualFiles.Except(expectedList).ToList();

        return new CleanupReport(
            actualFiles.Count,
            unexpectedFiles.Count,
            unexpectedFiles,
            actualFiles.Count == 0 || unexpectedFiles.Count == 0);
    }

    public static CleanupReport ValidateNoOrphanWhisperProcesses(string? processName = null)
    {
        var name = processName ?? "whisper";
        var processes = Process.GetProcesses()
            .Where(p => p.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new CleanupReport(
            processes.Count,
            processes.Count,
            processes.Select(p => $"{p.ProcessName} (PID: {p.Id})").ToList(),
            processes.Count == 0);
    }

    public static CleanupReport ValidateNoOrphanFfmpegProcesses()
    {
        var processes = Process.GetProcesses()
            .Where(p => p.ProcessName.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new CleanupReport(
            processes.Count,
            processes.Count,
            processes.Select(p => $"{p.ProcessName} (PID: {p.Id})").ToList(),
            processes.Count == 0);
    }

    public void CleanupOrphanProcesses()
    {
        foreach (var p in Process.GetProcesses()
            .Where(p => p.ProcessName.Contains("whisper", StringComparison.OrdinalIgnoreCase) ||
                       p.ProcessName.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                p.Kill();
            }
            catch
            {
            }
        }
    }

    public void CleanupTempChunkFiles(IEnumerable<string> chunkPaths)
    {
        foreach (var path in chunkPaths)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        foreach (var path in Directory.GetFiles(_tempPath, "*.wav")
            .Where(f => Path.GetFileName(f).StartsWith("tmp", StringComparison.OrdinalIgnoreCase) ||
                       Path.GetFileName(f).StartsWith("chunk", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }
}

public record CleanupReport(
    int TotalFound,
    int UnexpectedCount,
    List<string> UnexpectedItems,
    bool IsClean)
{
    public bool IsClean => UnexpectedCount == 0;

    public override string ToString()
    {
        if (UnexpectedCount == 0)
            return "Clean - no unexpected files/processes";
        return $"Found {UnexpectedCount} unexpected items: {string.Join(", ", UnexpectedItems.Take(5))}";
    }
}