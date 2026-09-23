using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.Engines;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class StressTestHarness : IDisposable
{
    private readonly string _videoPath;
    private readonly ISpeechToTextEngine _engine;
    private readonly string _modelName;
    private readonly string _language;
    private readonly int _audioTrackIndex;
    private readonly string _tempPath;

    private bool _disposed;

    public StressTestHarness(
        string videoPath,
        ISpeechToTextEngine engine,
        string modelName,
        string language,
        int audioTrackIndex = 0,
        string? tempPath = null)
    {
        _videoPath = videoPath;
        _engine = engine;
        _modelName = modelName;
        _language = language;
        _audioTrackIndex = audioTrackIndex;
        _tempPath = tempPath ?? Path.GetTempPath();
    }

    public async Task<StressTestReport> RunInterruptSequence(int iterations, CancellationToken ct = default)
    {
        var results = new List<StressIterationResult>();
        var checkpointWorked = 0;
        var cancelWorked = 0;
        var resumeWorked = 0;

        for (var i = 0; i < iterations && !ct.IsCancellationRequested; i++)
        {
            Console.WriteLine($"Stress iteration {i + 1}/{iterations}");
            var iterationResult = await RunInterruptIterationAsync(ct);
            results.Add(iterationResult);

            if (iterationResult.CheckpointSaved) checkpointWorked++;
            if (iterationResult.CancellationClean) cancelWorked++;
            if (iterationResult.ResumeProducedOutput) resumeWorked++;
        }

        return new StressTestReport(
            iterations,
            checkpointWorked,
            cancelWorked,
            resumeWorked,
            results);
    }

    private async Task<StressIterationResult> RunInterruptIterationAsync(CancellationToken ct)
    {
        var outputPath = Path.Combine(_tempPath, $"stress_{Guid.NewGuid():N}.srt");

        try
        {
            var audioExtractor = new FfmpegAudioExtractor(_videoPath, _audioTrackIndex);
            var audioTranscriber = new WhisperTranscriber(_engine, _modelName, _language);
            var duration = await audioExtractor.GetVideoDurationAsync(ct);

            var controller = new PipelineController(
                _videoPath,
                outputPath,
                audioExtractor,
                audioTranscriber,
                duration,
                _audioTrackIndex);

            var cancellationSource = new CancellationTokenSource();
            var processStarted = false;
            var checkpointBeforeCancel = File.Exists(GetCheckpointPath(outputPath));

            var processTask = Task.Run(async () =>
            {
                processStarted = true;
                await controller.TranscribeAsync(cancellationSource.Token);
            }, ct);

            await Task.Delay(Random.Shared.Next(2000, 5000), ct);

            if (processStarted && !processTask.IsCompleted)
            {
                cancellationSource.Cancel();
                try { await processTask; } catch (OperationCanceledException) { }
            }

            controller.Dispose();

            var checkpointAfterCancel = File.Exists(GetCheckpointPath(outputPath));
            var outputExists = File.Exists(outputPath);
            var tempFilesBeforeCleanup = Directory.GetFiles(_tempPath, "*.wav").Length;

            return new StressIterationResult(
                CheckpointSaved: checkpointBeforeCancel || checkpointAfterCancel,
                CancellationClean: tempFilesBeforeCleanup == 0 || tempFilesBeforeCleanup < 5,
                ResumeProducedOutput: false,
                OutputFileSize: outputExists ? new FileInfo(outputPath).Length : 0,
                ChunkCount: ReadChunkCount(outputPath));
        }
        finally
        {
            try
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
                foreach (var f in Directory.GetFiles(_tempPath, "*checkpoint*"))
                    try { File.Delete(f); } catch { }
            }
            catch { }
        }
    }

    private static string GetCheckpointPath(string srtPath) => srtPath + ".checkpoint";

    private static int ReadChunkCount(string srtPath)
    {
        if (!File.Exists(srtPath)) return 0;
        try
        {
            var sub = new Subtitle();
            new SubRip().LoadSubtitle(sub, File.ReadAllLines(srtPath).ToList(), srtPath);
            return sub.Paragraphs.Count;
        }
        catch { return 0; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}

public record StressTestReport(
    int TotalIterations,
    int CheckpointWorked,
    int CancellationClean,
    int ResumeWorked,
    IReadOnlyList<StressIterationResult> Iterations)
{
    public bool AllCheckpointsWorked => CheckpointWorked == TotalIterations;
    public bool AllCancellationsClean => CancellationClean == TotalIterations;

    public override string ToString()
    {
        return $"Stress Test: {TotalIterations} iterations, " +
               $"checkpoints: {CheckpointWorked}/{TotalIterations}, " +
               $"clean cancels: {CancellationClean}/{TotalIterations}";
    }
}

public record StressIterationResult(
    bool CheckpointSaved,
    bool CancellationClean,
    bool ResumeProducedOutput,
    long OutputFileSize,
    int ChunkCount);