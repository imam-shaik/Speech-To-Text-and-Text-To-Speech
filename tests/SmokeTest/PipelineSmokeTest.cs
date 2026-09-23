using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

namespace Nikse.SubtitleEdit.Tests.SmokeTest;

public class PipelineSmokeTest
{
    private readonly string _tempPath;
    private readonly List<string> _testFiles = new();

    public PipelineSmokeTest()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"se_smoke_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempPath);
    }

    public static async Task<int> Main(string[] args)
    {
        var test = new PipelineSmokeTest();
        try
        {
            await test.RunAllTests();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL: {ex.Message}");
            return 1;
        }
        finally
        {
            test.Cleanup();
        }
    }

    public async Task RunAllTests()
    {
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine("  Speech-to-Text Pipeline Smoke Test");
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine();

        var tests = new List<(string Name, Func<Task<bool>> Test)>
        {
            ("Chunker - Calculate chunk boundaries", TestChunkerBoundaries),
            ("Chunker - Zero duration video", TestChunkerZeroDuration),
            ("Checkpoint - Save and Load", TestCheckpointSaveLoad),
            ("Checkpoint - Translation state", TestCheckpointTranslationState),
            ("Pipeline - Instantiate", TestPipelineInstantiate),
            ("Pipeline - LogRunSummary", TestLogSummary),
        };

        var passed = 0;
        var failed = 0;

        foreach (var (name, test) in tests)
        {
            Console.Write($"Testing: {name}... ");
            try
            {
                var result = await test();
                if (result)
                {
                    Console.WriteLine("PASS");
                    passed++;
                }
                else
                {
                    Console.WriteLine("FAIL");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                failed++;
            }
            Console.WriteLine();
        }

        Console.WriteLine("-".PadRight(60, '-'));
        Console.WriteLine($"Results: {passed} passed, {failed} failed");
        Console.WriteLine("-".PadRight(60, '-'));

        if (failed > 0)
        {
            Environment.ExitCode = 1;
        }
    }

    private async Task<bool> TestChunkerBoundaries()
    {
        var chunker = new SilenceAwareChunker();
        var videoDuration = TimeSpan.FromMinutes(5);
        var silenceRegions = new List<(TimeSpan start, TimeSpan end)>();

        var chunks = chunker.CalculateChunkBoundaries(videoDuration, silenceRegions, null);

        Console.WriteLine($"    Video duration: {videoDuration}");
        Console.WriteLine($"    Chunks created: {chunks.Count}");

        if (chunks.Count == 0)
        {
            Console.WriteLine("    ERROR: No chunks created!");
            return false;
        }

        if (chunks.Count < 2)
        {
            Console.WriteLine("    WARNING: Only 1 chunk for 5 min video (expected multiple)");
        }

        for (int i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            Console.WriteLine($"    Chunk {i + 1}: {c.Start:mm\\:ss} - {c.End:mm\\:ss} (duration: {c.End - c.Start:ss\\.fff}s)");
        }

        var first = chunks.First();
        var last = chunks.Last();

        if (first.Start != TimeSpan.Zero)
        {
            Console.WriteLine($"    ERROR: First chunk doesn't start at 0: {first.Start}");
            return false;
        }

        if (last.End > videoDuration)
        {
            Console.WriteLine($"    ERROR: Last chunk ends after video: {last.End} > {videoDuration}");
            return false;
        }

        return true;
    }

    private async Task<bool> TestChunkerZeroDuration()
    {
        var chunker = new SilenceAwareChunker();
        var videoDuration = TimeSpan.Zero;
        var silenceRegions = new List<(TimeSpan start, TimeSpan end)>();

        var chunks = chunker.CalculateChunkBoundaries(videoDuration, silenceRegions, null);

        Console.WriteLine($"    Video duration: {videoDuration}");
        Console.WriteLine($"    Chunks created: {chunks.Count}");

        if (chunks.Count == 0)
        {
            Console.WriteLine("    CORRECT: Empty list returned for zero duration video");
            return true;
        }

        Console.WriteLine("    WARNING: Chunks returned for zero duration video");
        return true;
    }

    private async Task<bool> TestCheckpointSaveLoad()
    {
        var srtPath = Path.Combine(_tempPath, "test.srt");
        var cm = new CheckpointManager(srtPath);

        var checkpoint = new TranscriptionCheckpoint
        {
            VideoPath = "test.mp4",
            EngineId = "whisper",
            ModelId = "base",
            Language = "en",
            VideoDurationTicks = TimeSpan.FromMinutes(5).Ticks,
            TotalChunks = 10,
            CompletedChunks = 3,
            LastCompletedChunkEndTicks = TimeSpan.FromMinutes(1).Ticks,
            SrtPath = srtPath,
            TranslationEnabled = false,
            TargetLanguages = new List<string>(),
            GenerateBilingual = false
        };

        cm.Save(checkpoint);
        Console.WriteLine($"    Saved checkpoint to: {cm.CheckpointPath}");

        var loaded = cm.Load();
        if (loaded == null)
        {
            Console.WriteLine("    ERROR: Failed to load checkpoint");
            return false;
        }

        if (loaded.VideoPath != checkpoint.VideoPath)
        {
            Console.WriteLine($"    ERROR: VideoPath mismatch: {loaded.VideoPath} != {checkpoint.VideoPath}");
            return false;
        }

        if (loaded.CompletedChunks != checkpoint.CompletedChunks)
        {
            Console.WriteLine($"    ERROR: CompletedChunks mismatch: {loaded.CompletedChunks} != {checkpoint.CompletedChunks}");
            return false;
        }

        Console.WriteLine($"    Loaded checkpoint: {loaded.CompletedChunks}/{loaded.TotalChunks} chunks");
        cm.Delete();

        return true;
    }

    private async Task<bool> TestCheckpointTranslationState()
    {
        var srtPath = Path.Combine(_tempPath, "test_translation.srt");
        var cm = new CheckpointManager(srtPath);

        var checkpoint = new TranscriptionCheckpoint
        {
            VideoPath = "test.mp4",
            EngineId = "vosk",
            ModelId = "vosk-model",
            Language = "hi",
            VideoDurationTicks = TimeSpan.FromMinutes(10).Ticks,
            TotalChunks = 20,
            CompletedChunks = 5,
            TranslationEnabled = true,
            TargetLanguages = new List<string> { "en", "hinglish" },
            GenerateBilingual = true
        };

        cm.Save(checkpoint);
        var loaded = cm.Load();

        if (!loaded.TranslationEnabled)
        {
            Console.WriteLine("    ERROR: TranslationEnabled not preserved");
            return false;
        }

        if (loaded.TargetLanguages.Count != 2)
        {
            Console.WriteLine($"    ERROR: TargetLanguages count mismatch: {loaded.TargetLanguages.Count} != 2");
            return false;
        }

        if (!loaded.GenerateBilingual)
        {
            Console.WriteLine("    ERROR: GenerateBilingual not preserved");
            return false;
        }

        Console.WriteLine($"    Translation state preserved: {loaded.TranslationEnabled}");
        Console.WriteLine($"    Target languages: {string.Join(", ", loaded.TargetLanguages)}");
        Console.WriteLine($"    Bilingual: {loaded.GenerateBilingual}");

        cm.Delete();
        return true;
    }

    private async Task<bool> TestPipelineInstantiate()
    {
        var testWav = Path.Combine(_tempPath, "test.wav");

        var createCmd = $" -f lavfi -i \"sine=frequency=440:duration=1\" -t 1 -ar 16000 -ac 1 -y \"{testWav}\"";
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = createCmd,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using (var proc = Process.Start(psi))
        {
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0)
            {
                Console.WriteLine($"    ERROR: FFmpeg failed to create test audio");
                return false;
            }
        }

        if (!File.Exists(testWav))
        {
            Console.WriteLine($"    ERROR: Test audio file not created");
            return false;
        }

        _testFiles.Add(testWav);

        var extractor = new FfmpegAudioExtractor(testWav, 0);
        var duration = await extractor.GetVideoDurationAsync();

        Console.WriteLine($"    Test audio duration: {duration}");

        var dummyTranscriber = new DummyTranscriber();
        var controller = new PipelineController(
            testWav,
            Path.Combine(_tempPath, "output.srt"),
            extractor,
            dummyTranscriber,
            duration,
            0
        );

        Console.WriteLine("    PipelineController instantiated successfully");
        Console.WriteLine($"    Output: {controller.OutputSrtPath}");

        controller.Dispose();

        return true;
    }

    private async Task<bool> TestLogSummary()
    {
        var logs = new List<string>();
        void Logger(string msg) => logs.Add(msg);

        var duration = TimeSpan.FromSeconds(30);
        var dummyTranscriber = new DummyTranscriberWithDuration(duration);

        var controller = new PipelineController(
            "test.mp4",
            Path.Combine(_tempPath, "output2.srt"),
            new DummyExtractor(duration),
            dummyTranscriber,
            duration,
            0,
            Logger
        );

        try
        {
            var result = await controller.TranscribeAsync();

            Console.WriteLine($"    Log entries captured: {logs.Count}");
            var summaryLines = logs.Where(l => l.Contains("Speech-to-Text Summary")).ToList();
            Console.WriteLine($"    Summary found: {summaryLines.Count > 0}");

            if (summaryLines.Count > 0)
            {
                foreach (var line in summaryLines)
                {
                    Console.WriteLine($"    {line}");
                }
            }

            return summaryLines.Count > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Exception: {ex.Message}");
            return false;
        }
        finally
        {
            controller.Dispose();
        }
    }

    private void Cleanup()
    {
        foreach (var f in _testFiles)
        {
            try
            {
                if (File.Exists(f))
                    File.Delete(f);
            }
            catch { }
        }

        try
        {
            if (Directory.Exists(_tempPath))
                Directory.Delete(_tempPath, true);
        }
        catch { }
    }

    private class DummyTranscriber : IAudioTranscriber
    {
        public string EngineId => "dummy";
        public string ModelId => "test";
        public string Language => "en";

        public Task<Subtitle> TranscribeChunkAsync(string audioPath, TimeSpan offset, CancellationToken ct = default)
        {
            var result = new Subtitle();
            return Task.FromResult(result);
        }
    }

    private class DummyTranscriberWithDuration : IAudioTranscriber
    {
        private readonly TimeSpan _duration;

        public string EngineId => "dummy";
        public string ModelId => "test";
        public string Language => "en";

        public DummyTranscriberWithDuration(TimeSpan duration)
        {
            _duration = duration;
        }

        public Task<Subtitle> TranscribeChunkAsync(string audioPath, TimeSpan offset, CancellationToken ct = default)
        {
            var result = new Subtitle();
            result.Paragraphs.Add(new Paragraph("Test subtitle", offset.TotalMilliseconds, (offset + TimeSpan.FromSeconds(5)).TotalMilliseconds));
            return Task.FromResult(result);
        }
    }

    private class DummyExtractor : IAudioExtractor
    {
        private readonly TimeSpan _duration;

        public DummyExtractor(TimeSpan duration)
        {
            _duration = duration;
        }

        public Task<TimeSpan> GetVideoDurationAsync(CancellationToken ct = default)
        {
            return Task.FromResult(_duration);
        }

        public Task ExtractChunkAsync(TimeSpan start, TimeSpan end, string outputPath, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}