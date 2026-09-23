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

public class FixVerificationTest
{
    private readonly string _tempPath;
    private readonly string _whisperExe;
    private readonly string _ffmpegExe;
    private readonly string _modelPath;

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
        Console.WriteLine("  Boundary Duplicate Detection Fix Verification");
        Console.WriteLine("============================================================");
        Console.WriteLine();
        Console.WriteLine($"Whisper:  {whisperExe}");
        Console.WriteLine($"Model:    {modelPath}");
        Console.WriteLine($"FFmpeg:   {ffmpegExe}");
        Console.WriteLine();

        var test = new FixVerificationTest(whisperExe, modelPath, ffmpegExe);
        try
        {
            await test.RunVerification();
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

    public FixVerificationTest(string whisperExe, string modelPath, string ffmpegExe)
    {
        _whisperExe = whisperExe;
        _modelPath = modelPath;
        _ffmpegExe = ffmpegExe;
        _tempPath = Path.Combine(Path.GetTempPath(), $"se_fix_verify_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempPath);
    }

    public async Task RunVerification()
    {
        var testCases = new List<(string Name, int DurationSeconds)>
        {
            ("2min Test", 120),
        };

        foreach (var (name, durationSeconds) in testCases)
        {
            Console.WriteLine($"========================================");
            Console.WriteLine($"Testing: {name}");
            Console.WriteLine($"========================================");

            var testAudio = CreateTestAudio(durationSeconds);
            var duration = TimeSpan.FromSeconds(durationSeconds);

            Console.WriteLine($"\n[Test Audio]");
            Console.WriteLine($"  File: {testAudio}");
            Console.WriteLine($"  Duration: {duration}");
            Console.WriteLine();

            var legacyResult = await RunLegacyMode(testAudio);
            Console.WriteLine($"\n[Legacy Mode]");
            Console.WriteLine($"  Subtitles: {legacyResult.paragraphs.Count}");
            Console.WriteLine($"  Words: {legacyResult.words}");
            foreach (var p in legacyResult.paragraphs)
            {
                Console.WriteLine($"    [{FormatTime(p.StartTime)} --> {FormatTime(p.EndTime)}] {p.Text}");
            }

            var chunkedResult = await RunChunkedModeWithDuplicateDetection(testAudio, duration);
            Console.WriteLine($"\n[Chunked Mode (WITH FIX)]");
            Console.WriteLine($"  Total chunks: {chunkedResult.chunks}");
            Console.WriteLine($"  Subtitles: {chunkedResult.paragraphs.Count}");
            Console.WriteLine($"  Words: {chunkedResult.words}");
            Console.WriteLine($"  Duplicates removed at boundaries: {chunkedResult.duplicatesRemoved}");

            var legacyCount = legacyResult.paragraphs.Count;
            var chunkedCount = chunkedResult.paragraphs.Count;
            var diff = Math.Abs(legacyCount - chunkedCount);
            var diffPercent = legacyCount > 0 ? (diff * 100.0 / legacyCount) : 0;

            Console.WriteLine($"\n[Comparison]");
            Console.WriteLine($"  Legacy subtitles: {legacyCount}");
            Console.WriteLine($"  Chunked subtitles: {chunkedCount}");
            Console.WriteLine($"  Difference: {diff} ({diffPercent:F1}%)");

            var passed = diffPercent <= 5;
            Console.WriteLine($"\n  Result: {(passed ? "PASS" : "NEEDS INVESTIGATION")}");
        }
    }

    private async Task<(List<Paragraph> paragraphs, int words)> RunLegacyMode(string audioPath)
    {
        var tempWav = Path.Combine(_tempPath, $"legacy_{Guid.NewGuid():N}.wav");
        var outputSrt = Path.Combine(_tempPath, "legacy_output.srt");

        try
        {
            await ExtractAudioAsync(audioPath, tempWav);
            await TranscribeWithWhisperAsync(tempWav, outputSrt);

            var subtitle = new Subtitle();
            new SubRip().LoadSubtitle(subtitle, File.ReadAllLines(outputSrt).ToList(), outputSrt);
            var words = subtitle.Paragraphs.Sum(p => p.Text.Split(' ').Length);

            return (subtitle.Paragraphs.ToList(), words);
        }
        finally
        {
            if (File.Exists(tempWav)) File.Delete(tempWav);
        }
    }

    private async Task<(List<Paragraph> paragraphs, int words, int chunks, int duplicatesRemoved)> RunChunkedModeWithDuplicateDetection(string audioPath, TimeSpan duration)
    {
        var silenceRegions = new List<(TimeSpan start, TimeSpan end)>();
        var chunker = new SilenceAwareChunker();
        var chunks = chunker.CalculateChunkBoundaries(duration, silenceRegions, null);

        Console.WriteLine($"  Chunk count: {chunks.Count}");
        for (var i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            Console.WriteLine($"    Chunk {i + 1}: {c.Start:mm\\:ss\\.fff} - {c.End:mm\\:ss\\.fff} ({(c.End - c.Start).TotalSeconds:F1}s)");
        }

        var duplicateDetector = new SubtitleDuplicateDetector();
        var allSubtitles = new List<Paragraph>();
        var lastKeptSubtitle = (Paragraph?)null;
        var totalDuplicates = 0;

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var tempWav = Path.Combine(_tempPath, $"chunk_{i}_{Guid.NewGuid():N}.wav");
            var chunkSrt = Path.Combine(_tempPath, $"chunk_{i}.srt");

            try
            {
                await ExtractAudioChunkAsync(audioPath, chunk.Start, chunk.End, tempWav);
                await TranscribeWithWhisperAsync(tempWav, chunkSrt);

                if (File.Exists(chunkSrt))
                {
                    var chunkSubtitle = new Subtitle();
                    new SubRip().LoadSubtitle(chunkSubtitle, File.ReadAllLines(chunkSrt).ToList(), chunkSrt);

                    var isFirstSubtitleOfChunk = true;
                    var chunkDuplicates = 0;

                    foreach (var p in chunkSubtitle.Paragraphs)
                    {
                        p.StartTime.TotalMilliseconds += chunk.Start.TotalMilliseconds;
                        p.EndTime.TotalMilliseconds += chunk.Start.TotalMilliseconds;

                        var isDuplicate = false;
                        if (lastKeptSubtitle != null && isFirstSubtitleOfChunk)
                        {
                            isDuplicate = duplicateDetector.IsDuplicateAtBoundary(
                                p, lastKeptSubtitle, SubtitleDuplicateDetector.CalculateSimilarity);
                        }
                        else if (lastKeptSubtitle != null)
                        {
                            isDuplicate = duplicateDetector.IsDuplicate(
                                p, lastKeptSubtitle, SubtitleDuplicateDetector.CalculateSimilarity);
                        }

                        if (isDuplicate)
                        {
                            var score = SubtitleDuplicateDetector.CalculateSimilarity(p.Text, lastKeptSubtitle.Text);
                            var overlap = p.StartTime.TotalMilliseconds - lastKeptSubtitle.EndTime.TotalMilliseconds;
                            Console.WriteLine($"    [DEDUP] Chunk {i + 1}: overlap={overlap:F0}ms, similarity={score:P0}");
                            Console.WriteLine($"      REMOVED: \"{TruncateText(p.Text, 50)}\"");
                            totalDuplicates++;
                            chunkDuplicates++;
                        }
                        else
                        {
                            allSubtitles.Add(p);
                            lastKeptSubtitle = p;
                            Console.WriteLine($"    [KEEP] Chunk {i + 1}: \"{TruncateText(p.Text, 50)}\"");
                        }

                        isFirstSubtitleOfChunk = false;
                    }

                    Console.WriteLine($"    Chunk {i + 1}: {chunkSubtitle.Paragraphs.Count} transcribed, {chunkDuplicates} duplicates removed");
                }
            }
            finally
            {
                if (File.Exists(tempWav)) File.Delete(tempWav);
            }
        }

        var words = allSubtitles.Sum(p => p.Text.Split(' ').Length);
        return (allSubtitles, words, chunks.Count, totalDuplicates);
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

        return path;
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
        using var proc = Process.Start(psi);
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
        using var proc = Process.Start(psi);
        if (proc == null) throw new InvalidOperationException("Failed to start ffmpeg");
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
        {
            var stderr = await proc.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"FFmpeg chunk extract failed: {stderr}");
        }
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
        using var proc = Process.Start(psi);
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

    private static string FormatTime(TimeCode tc)
    {
        return $"{tc.Hours:00}:{tc.Minutes:00}:{tc.Seconds:00},{tc.Milliseconds:000}";
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
    }

    private void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempPath))
                Directory.Delete(_tempPath, true);
        }
        catch { }
    }
}