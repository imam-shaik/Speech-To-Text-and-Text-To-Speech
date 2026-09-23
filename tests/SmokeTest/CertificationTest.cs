using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.Engines;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

namespace Nikse.SubtitleEdit.Tests.SmokeTest;

public class CertificationTest
{
    private readonly string _tempPath;
    private readonly string _whisperExe;
    private readonly string _ffmpegExe;
    private readonly string _modelPath;
    private readonly List<string> _testFiles = new();

    public CertificationTest(string whisperExe, string modelPath, string ffmpegExe)
    {
        _whisperExe = whisperExe;
        _modelPath = modelPath;
        _ffmpegExe = ffmpegExe;
        _tempPath = Path.Combine(Path.GetTempPath(), $"se_cert_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempPath);
    }

    public static async Task<int> Main(string[] args)
    {
        var baseDir = @"C:\Users\IMAM\Desktop\Final Testing\subtitleedit-fresh\src\ui\bin\Debug\net10.0";
        var whisperExe = Path.Combine(baseDir, @"SpeechToText\Cpp\whisper-cli.exe");
        var ffmpegExe = Path.Combine(baseDir, @"ffmpeg\ffmpeg.exe");
        var modelDir = Path.Combine(baseDir, @"SpeechToText\Cpp\Models");

        var modelPath = Directory.GetFiles(modelDir, "*.bin").FirstOrDefault();
        if (modelPath == null)
        {
            Console.WriteLine("ERROR: No whisper model found");
            return 1;
        }

        Console.WriteLine("============================================================");
        Console.WriteLine("  Phase 4 - Speech-to-Text Pipeline Certification");
        Console.WriteLine("============================================================");
        Console.WriteLine();
        Console.WriteLine($"Whisper:  {whisperExe}");
        Console.WriteLine($"Model:    {modelPath}");
        Console.WriteLine($"FFmpeg:   {ffmpegExe}");
        Console.WriteLine();

        var test = new CertificationTest(whisperExe, modelPath, ffmpegExe);
        try
        {
            await test.RunCertification();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
        finally
        {
            test.Cleanup();
        }
    }

    public async Task RunCertification()
    {
        var testCases = new List<(string Name, string AudioPath, TimeSpan Duration, VideoType Type)>
        {
            ("30s Test", CreateTestAudio(30), TimeSpan.FromSeconds(30), VideoType.SingleSpeaker),
            ("2min Test", CreateTestAudio(120), TimeSpan.FromMinutes(2), VideoType.SingleSpeaker),
            ("5min Test", CreateTestAudio(300), TimeSpan.FromMinutes(5), VideoType.SingleSpeaker),
        };

        var results = new List<(string Name, bool Passed, string Message)>();

        foreach (var (name, audioPath, duration, type) in testCases)
        {
            Console.WriteLine($"========================================");
            Console.WriteLine($"Testing: {name}");
            Console.WriteLine($"========================================");

            var passed = await RunTestCase(name, audioPath, duration, type);
            results.Add((name, passed.Item1, passed.Item2));
            Console.WriteLine();
        }

        Console.WriteLine("============================================================");
        Console.WriteLine("  CERTIFICATION SUMMARY");
        Console.WriteLine("============================================================");
        var allPassed = true;
        foreach (var (testName, passed, message) in results)
        {
            var status = passed ? "PASS" : "FAIL";
            Console.WriteLine($"  [{status}] {testName}: {message}");
            if (!passed) allPassed = false;
        }
        Console.WriteLine("============================================================");
        Console.WriteLine($"OVERALL: {(allPassed ? "CERTIFIED" : "NOT CERTIFIED")}");
        Console.WriteLine("============================================================");
    }

    private async Task<(bool, string)> RunTestCase(string name, string audioPath, TimeSpan duration, VideoType type)
    {
        var results = new List<(string Test, bool Passed, string Details)>();

        // Stage 1: Legacy Mode
        Console.WriteLine("  [Stage 1] Legacy Mode...");
        var legacyResult = await RunLegacyMode(audioPath, Path.Combine(_tempPath, $"{name}_legacy.srt"));
        Console.WriteLine($"    Legacy: {legacyResult.Item2}");
        results.Add(("Legacy Mode", legacyResult.Item1, legacyResult.Item2));

        // Stage 2: Chunked Mode
        Console.WriteLine("  [Stage 2] Chunked Mode...");
        var chunkedResult = await RunChunkedMode(audioPath, Path.Combine(_tempPath, $"{name}_chunked.srt"));
        Console.WriteLine($"    Chunked: {chunkedResult.Item2}");
        results.Add(("Chunked Mode", chunkedResult.Item1, chunkedResult.Item2));

        // Stage 3: Compare Legacy vs Chunked
        Console.WriteLine("  [Stage 3] Quality Comparison...");
        var compareResult = CompareOutputs(
            Path.Combine(_tempPath, $"{name}_legacy.srt"),
            Path.Combine(_tempPath, $"{name}_chunked.srt"));
        Console.WriteLine($"    Compare: {compareResult.Item2}");
        results.Add(("Quality Comparison", compareResult.Item1, compareResult.Item2));

        // Stage 4: Timestamp Validation
        Console.WriteLine("  [Stage 4] Timestamp Validation...");
        var timestampResult = ValidateTimestamps(Path.Combine(_tempPath, $"{name}_chunked.srt"), duration);
        Console.WriteLine($"    Timestamps: {timestampResult.Item2}");
        results.Add(("Timestamp Validation", timestampResult.Item1, timestampResult.Item2));

        // Stage 5: Memory/Cleanup Check
        Console.WriteLine("  [Stage 5] Cleanup Check...");
        var cleanupResult = CheckCleanup();
        Console.WriteLine($"    Cleanup: {cleanupResult.Item2}");
        results.Add(("Cleanup", cleanupResult.Item1, cleanupResult.Item2));

        var allPassed = results.All(r => r.Passed);
        var passedCount = results.Count(r => r.Passed);
        return (allPassed, $"{passedCount}/{results.Count} stages passed");
    }

    private async Task<(bool, string)> RunLegacyMode(string audioPath, string outputSrt)
    {
        try
        {
            if (File.Exists(outputSrt))
                File.Delete(outputSrt);

            var tempWav = Path.Combine(_tempPath, $"legacy_{Guid.NewGuid():N}.wav");
            try
            {
                await ExtractAudioAsync(audioPath, tempWav);

                var success = await TranscribeWithWhisperAsync(tempWav, outputSrt);
                if (!success)
                    return (false, "Whisper transcription failed");

                if (!File.Exists(outputSrt))
                    return (false, "No SRT file produced");

                var subtitle = new Subtitle();
                new SubRip().LoadSubtitle(subtitle, File.ReadAllLines(outputSrt).ToList(), outputSrt);

                return (true, $"{subtitle.Paragraphs.Count} subtitles, {subtitle.Paragraphs.Sum(p => p.Text.Split(' ').Length)} words");
            }
            finally
            {
                if (File.Exists(tempWav)) File.Delete(tempWav);
            }
        }
        catch (Exception ex)
        {
            return (false, $"Exception: {ex.Message}");
        }
    }

    private async Task<(bool, string)> RunChunkedMode(string audioPath, string outputSrt)
    {
        try
        {
            if (File.Exists(outputSrt))
                File.Delete(outputSrt);

            var duration = await GetAudioDurationAsync(audioPath);
            Console.WriteLine($"    Duration: {duration}");

            var silenceRegions = new List<(TimeSpan start, TimeSpan end)>();
            var chunker = new SilenceAwareChunker();
            var chunks = chunker.CalculateChunkBoundaries(duration, silenceRegions, null);
            Console.WriteLine($"    Chunks: {chunks.Count}");

            var tempWavs = new List<string>();
            try
            {
                var allSubtitles = new List<Paragraph>();

                for (var i = 0; i < chunks.Count; i++)
                {
                    var chunk = chunks[i];
                    var tempWav = Path.Combine(_tempPath, $"chunk_{i}_{Guid.NewGuid():N}.wav");
                    tempWavs.Add(tempWav);

                    await ExtractAudioChunkAsync(audioPath, chunk.Start, chunk.End, tempWav);

                    var chunkSrt = Path.Combine(_tempPath, $"chunk_{i}.srt");
                    var success = await TranscribeWithWhisperAsync(tempWav, chunkSrt);

                    if (success && File.Exists(chunkSrt))
                    {
                        var chunkSubtitle = new Subtitle();
                        new SubRip().LoadSubtitle(chunkSubtitle, File.ReadAllLines(chunkSrt).ToList(), chunkSrt);

                        foreach (var p in chunkSubtitle.Paragraphs)
                        {
                            p.StartTime.TotalMilliseconds += chunk.Start.TotalMilliseconds;
                            p.EndTime.TotalMilliseconds += chunk.Start.TotalMilliseconds;
                            allSubtitles.Add(p);
                        }
                    }

                    Console.WriteLine($"    Chunk {i + 1}/{chunks.Count}: {allSubtitles.Count} total subtitles so far");
                }

                // Write combined SRT
                using (var writer = new StreamWriter(outputSrt))
                {
                    var idx = 1;
                    foreach (var p in allSubtitles.OrderBy(x => x.StartTime.TotalMilliseconds))
                    {
                        writer.WriteLine(idx.ToString());
                        writer.WriteLine($"{FormatTime(p.StartTime)} --> {FormatTime(p.EndTime)}");
                        writer.WriteLine(p.Text);
                        writer.WriteLine();
                        idx++;
                    }
                }

                return (true, $"{allSubtitles.Count} subtitles from {chunks.Count} chunks");
            }
            finally
            {
                foreach (var wav in tempWavs)
                    if (File.Exists(wav)) File.Delete(wav);
            }
        }
        catch (Exception ex)
        {
            return (false, $"Exception: {ex.Message}");
        }
    }

    private async Task ExtractAudioAsync(string inputPath, string outputPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegExe,
            Arguments = $"-y -i \"{inputPath}\" -vn -ar 16000 -ac 1 -ab 32k -af volume=1.75 -hide_banner -loglevel error -f wav \"{outputPath}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) throw new InvalidOperationException("Failed to start ffmpeg");
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
        {
            var stderr = await proc.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"FFmpeg extract failed: {stderr}");
        }
    }

    private async Task ExtractAudioChunkAsync(string inputPath, TimeSpan start, TimeSpan end, string outputPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegExe,
            Arguments = $"-y -ss {start.TotalSeconds:0.000} -to {end.TotalSeconds:0.000} -i \"{inputPath}\" -vn -ar 16000 -ac 1 -ab 32k -af volume=1.75 -hide_banner -loglevel error -f wav \"{outputPath}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) throw new InvalidOperationException("Failed to start ffmpeg");
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
        {
            var stderr = await proc.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"FFmpeg chunk extract failed: {stderr}");
        }
    }

    private async Task<TimeSpan> GetAudioDurationAsync(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegExe,
            Arguments = $"-i \"{path}\" -hide_banner -f null -",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) throw new InvalidOperationException("Failed to start ffmpeg");
        await proc.WaitForExitAsync();
        var errorOutput = await proc.StandardError.ReadToEndAsync();

        var match = System.Text.RegularExpressions.Regex.Match(errorOutput, @"Duration: (\d{2}):(\d{2}):(\d{2})\.(\d{2})");
        if (match.Success)
        {
            var h = int.Parse(match.Groups[1].Value);
            var m = int.Parse(match.Groups[2].Value);
            var s = int.Parse(match.Groups[3].Value);
            var cs = int.Parse(match.Groups[4].Value);
            return new TimeSpan(0, h, m, s, cs * 10);
        }
        return TimeSpan.Zero;
    }

    private async Task<bool> TranscribeWithWhisperAsync(string audioPath, string outputSrt)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _whisperExe,
            Arguments = $"--language en --model \"{_modelPath}\" --output-srt \"{audioPath}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) return false;
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
        {
            var stderr = await proc.StandardError.ReadToEndAsync();
            Console.WriteLine($"    Whisper error: {stderr}");
            return false;
        }

        var tempSrt = audioPath + ".srt";
        if (File.Exists(tempSrt) && !File.Exists(outputSrt))
        {
            File.Move(tempSrt, outputSrt);
        }
        else if (File.Exists(tempSrt))
        {
            File.Delete(tempSrt);
        }

        return true;
    }

    private (bool, string) CompareOutputs(string legacySrt, string chunkedSrt)
    {
        try
        {
            if (!File.Exists(legacySrt) || !File.Exists(chunkedSrt))
                return (false, "Missing output files");

            var legacy = new Subtitle();
            new SubRip().LoadSubtitle(legacy, File.ReadAllLines(legacySrt).ToList(), legacySrt);

            var chunked = new Subtitle();
            new SubRip().LoadSubtitle(chunked, File.ReadAllLines(chunkedSrt).ToList(), chunkedSrt);

            var legacyCount = legacy.Paragraphs.Count;
            var chunkedCount = chunked.Paragraphs.Count;

            var diff = Math.Abs(legacyCount - chunkedCount);
            var tolerance = Math.Max(1, (int)(Math.Min(legacyCount, chunkedCount) * 0.2));

            var passed = diff <= tolerance;
            var diffPercent = legacyCount > 0 ? (diff * 100.0 / legacyCount) : 0;

            return (passed, $"Legacy={legacyCount}, Chunked={chunkedCount}, Diff={diffPercent:F1}%");
        }
        catch (Exception ex)
        {
            return (false, $"Exception: {ex.Message}");
        }
    }

    private (bool, string) ValidateTimestamps(string srtPath, TimeSpan expectedDuration)
    {
        try
        {
            if (!File.Exists(srtPath))
                return (false, "No SRT file");

            var subtitle = new Subtitle();
            new SubRip().LoadSubtitle(subtitle, File.ReadAllLines(srtPath).ToList(), srtPath);

            var errors = new List<string>();

            // Check for negative timestamps
            if (subtitle.Paragraphs.Any(p => p.StartTime.TotalMilliseconds < 0 || p.EndTime.TotalMilliseconds < 0))
                errors.Add("Negative timestamps");

            // Check for overlaps
            for (var i = 0; i < subtitle.Paragraphs.Count - 1; i++)
            {
                if (subtitle.Paragraphs[i].EndTime.TotalMilliseconds > subtitle.Paragraphs[i + 1].StartTime.TotalMilliseconds)
                {
                    errors.Add("Overlap detected");
                    break;
                }
            }

            // Check if last subtitle ends after video duration
            var last = subtitle.Paragraphs.LastOrDefault();
            if (last != null && last.EndTime.TotalMilliseconds > expectedDuration.TotalMilliseconds + 1000)
                errors.Add("Last subtitle exceeds duration");

            return errors.Count == 0
                ? (true, $"{subtitle.Paragraphs.Count} subs, timestamps valid")
                : (false, string.Join(", ", errors));
        }
        catch (Exception ex)
        {
            return (false, $"Exception: {ex.Message}");
        }
    }

    private (bool, string) CheckCleanup()
    {
        try
        {
            var tempFiles = Directory.GetFiles(_tempPath, "*.wav")
                .Concat(Directory.GetFiles(_tempPath, "*.srt"))
                .Where(f => !f.Contains("cert_test"))
                .ToList();

            // Clean up our test files
            foreach (var f in tempFiles)
            {
                try { File.Delete(f); } catch { }
            }

            // Check for orphan whisper processes
            var whisperProcs = Process.GetProcesses()
                .Where(p => p.ProcessName.Contains("whisper", StringComparison.OrdinalIgnoreCase))
                .ToList();

            return (tempFiles.Count == 0, $"Temp files: {tempFiles.Count}, Whisper processes: {whisperProcs.Count}");
        }
        catch (Exception ex)
        {
            return (false, $"Exception: {ex.Message}");
        }
    }

    private string CreateTestAudio(int durationSeconds)
    {
        var path = Path.Combine(_tempPath, $"cert_test_{durationSeconds}s.wav");
        if (File.Exists(path))
            return path;

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegExe,
            Arguments = $"-f lavfi -i \"sine=frequency=440:duration={durationSeconds}\" -ar 16000 -ac 1 -y \"{path}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var proc = Process.Start(psi);
        proc.WaitForExit();

        _testFiles.Add(path);
        return path;
    }

    private static string FormatTime(TimeCode tc)
    {
        return $"{tc.Hours:00}:{tc.Minutes:00}:{tc.Seconds:00},{tc.Milliseconds:000}";
    }

    private void Cleanup()
    {
        foreach (var f in _testFiles)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        }
        try
        {
            if (Directory.Exists(_tempPath))
                Directory.Delete(_tempPath, true);
        }
        catch { }
    }
}