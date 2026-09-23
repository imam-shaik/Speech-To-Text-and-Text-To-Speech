using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class PipelineController : IDisposable
{
    private readonly string _videoPath;
    private readonly string _outputSrtPath;
    private readonly string _engineId;
    private readonly string _modelId;
    private readonly string _language;
    private readonly TimeSpan _videoDuration;
    private readonly int _audioTrackIndex;
    private readonly CheckpointManager _checkpointManager;
    private readonly ProgressReporter _progressReporter;
    private readonly SilenceAwareChunker _chunker;
    private readonly SubtitleDuplicateDetector _duplicateDetector;
    private readonly IAudioExtractor _audioExtractor;
    private readonly IAudioTranscriber _audioTranscriber;
    private readonly object _srtLock = new();
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private readonly Action<string>? _log;

    private SubtitleOutputManager? _outputManager;
    private ITranslationProvider? _translationProvider;
    private List<string>? _targetLanguages;
    private bool _generateBilingual;

    private int _totalChunksProcessed;
    private int _totalChunksFailed;
    private int _totalSubtitlesGenerated;
    private int _totalTranslationsCompleted;
    private int _totalTranslationErrors;
    private DateTime _runStartTime;
    private DateTime _runEndTime;

    public string VideoPath => _videoPath;
    public string OutputSrtPath => _outputSrtPath;
    public CheckpointManager CheckpointManager => _checkpointManager;
    public ProgressReporter ProgressReporter => _progressReporter;
    private void ChunkLog(string message)
    {
        var prefixed = $"[PIPELINE] {message}";
        System.Diagnostics.Debug.WriteLine(prefixed);
        try
        {
            _log?.Invoke(prefixed);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PIPELINE] Log callback failed: {ex.Message}");
        }
    }

    private static string TruncateText(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "(empty)";
        return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
    }

    public event EventHandler<TranscriptionProgress>? ProgressChanged
    {
        add => _progressReporter.ProgressChanged += value;
        remove => _progressReporter.ProgressChanged -= value;
    }

    public PipelineController(
        string videoPath,
        string outputSrtPath,
        IAudioExtractor audioExtractor,
        IAudioTranscriber audioTranscriber,
        TimeSpan videoDuration,
        int audioTrackIndex = 0,
        Action<string>? log = null)
    {
        _videoPath = videoPath;
        _outputSrtPath = outputSrtPath;
        _audioExtractor = audioExtractor;
        _audioTranscriber = audioTranscriber;
        _engineId = audioTranscriber.EngineId;
        _modelId = audioTranscriber.ModelId;
        _language = audioTranscriber.Language;
        _videoDuration = videoDuration;
        _audioTrackIndex = audioTrackIndex;
        _checkpointManager = new CheckpointManager(outputSrtPath);
        _progressReporter = new ProgressReporter();
        _chunker = new SilenceAwareChunker();
        _duplicateDetector = new SubtitleDuplicateDetector();
        _log = log;
    }

    public PipelineController(
        string videoPath,
        string outputSrtPath,
        IAudioExtractor audioExtractor,
        IAudioTranscriber audioTranscriber,
        TimeSpan videoDuration,
        int audioTrackIndex,
        SubtitleOutputManager outputManager,
        ITranslationProvider translationProvider,
        List<string> targetLanguages,
        bool generateBilingual = false,
        Action<string>? log = null)
        : this(videoPath, outputSrtPath, audioExtractor, audioTranscriber, videoDuration, audioTrackIndex, log)
    {
        _outputManager = outputManager;
        _translationProvider = translationProvider;
        _targetLanguages = targetLanguages;
        _generateBilingual = generateBilingual;
    }

    public async Task<Subtitle?> TranscribeAsync(CancellationToken externalCt = default)
    {
        ChunkLog("[PIPELINE] ===============================================");
        ChunkLog("[PIPELINE] TranscribeAsync ENTERED");
        ChunkLog($"[PIPELINE]   Video Path: {_videoPath}");
        ChunkLog($"[PIPELINE]   Output SRT Path: {_outputSrtPath}");
        ChunkLog($"[PIPELINE]   Video Duration (in controller): {_videoDuration}");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct = _cts.Token;

        try
        {
            ChunkLog("[PIPELINE] Checking for checkpoint...");
            var checkpoint = _checkpointManager.Load();
            if (checkpoint != null && checkpoint.VideoPath == _videoPath)
            {
                ChunkLog($"[PIPELINE] Checkpoint found! Video duration from checkpoint: {TimeSpan.FromTicks(checkpoint.VideoDurationTicks)}");
                ChunkLog($"[PIPELINE] Resuming from checkpoint. Completed chunks: {checkpoint.CompletedChunks}/{checkpoint.TotalChunks}");
                return await ResumeFromCheckpointAsync(checkpoint, ct);
            }
            ChunkLog("[PIPELINE] No checkpoint found. Starting fresh.");

            ChunkLog("[PIPELINE] Calling StartFreshAsync...");
            var result = await StartFreshAsync(ct);
            ChunkLog($"[PIPELINE] StartFreshAsync returned: {(result == null ? "null" : result.Paragraphs.Count + " paragraphs")}");
            return result;
        }
        catch (OperationCanceledException)
        {
            ChunkLog("[PIPELINE] OperationCancelledException caught");
            _progressReporter.Cancel();
            return null;
        }
        catch (Exception ex)
        {
            ChunkLog($"[PIPELINE] EXCEPTION in TranscribeAsync: {ex.GetType().Name}: {ex.Message}");
            ChunkLog($"[PIPELINE] Stack trace: {ex.StackTrace}");
            _progressReporter.Fail();
            throw;
        }
    }

    private async Task<Subtitle?> StartFreshAsync(CancellationToken ct)
    {
        _runStartTime = DateTime.Now;
        _totalChunksProcessed = 0;
        _totalChunksFailed = 0;
        _totalSubtitlesGenerated = 0;
        _totalTranslationsCompleted = 0;
        _totalTranslationErrors = 0;

        ChunkLog("[PIPELINE] ===============================================");
        ChunkLog("[PIPELINE] StartFreshAsync ENTERED");
        ChunkLog($"[PIPELINE]   Video Duration: {_videoDuration}");
        ChunkLog($"[PIPELINE]   Video Duration Ticks: {_videoDuration.Ticks}");
        ChunkLog($"[PIPELINE]   Audio Track Index: {_audioTrackIndex}");
        ChunkLog($"[PIPELINE]   Engine ID: {_engineId}");
        ChunkLog($"[PIPELINE]   Model ID: {_modelId}");
        ChunkLog($"[PIPELINE]   Language: {_language}");

        _progressReporter.SetStage(TranscriptionStage.ExtractingAudio);

        var silenceRegions = await DetectSilenceRegionsAsync(ct);
        ChunkLog($"[PIPELINE] Silence regions detected: {silenceRegions.Count}");

        ChunkLog("[PIPELINE] Calling _chunker.CalculateChunkBoundaries...");
        var chunks = _chunker.CalculateChunkBoundaries(_videoDuration, silenceRegions, _log);
        ChunkLog($"[PIPELINE] Chunks calculated: {chunks.Count}");
        for (int ci = 0; ci < chunks.Count; ci++)
        {
            var c = chunks[ci];
            ChunkLog($"[CHUNK-BOUNDARY] Chunk {ci + 1}: Start={c.Start}, End={c.End}, Duration={c.Duration}");
        }

        if (chunks.Count == 0)
        {
            ChunkLog("[PIPELINE] ERROR: No chunks produced! Stopping pipeline.");
            ChunkLog("[PIPELINE] This likely means videoDuration is 0 or chunker returned empty list.");
            _progressReporter.Stop();
            return new Subtitle();
        }

        ChunkLog($"[PIPELINE] Successfully got {chunks.Count} chunks. Starting processing...");

        _progressReporter.Start(chunks.Count);
        _progressReporter.SetStage(TranscriptionStage.Transcribing);

        ChunkLog($"[PIPELINE] About to delete existing output file if present: {_outputSrtPath}");
        if (File.Exists(_outputSrtPath))
        {
            ChunkLog("[PIPELINE] Deleting existing output file");
            File.Delete(_outputSrtPath);
        }

        var totalSubtitles = 0;
        var checkpointInterval = Math.Max(1, chunks.Count / 10);
        var lastKeptSubtitle = (Paragraph?)null;

        ChunkLog($"[PIPELINE] Creating StreamingSrtWriter...");
        using var writer = new StreamingSrtWriter(_outputSrtPath);

        ChunkLog($"[PIPELINE] Initializing output manager if present...");
        _outputManager?.InitializeOriginal();
        if (_targetLanguages != null)
        {
            foreach (var lang in _targetLanguages)
            {
                _outputManager?.AddTargetLanguage(lang);
            }
        }
        if (_generateBilingual && _targetLanguages?.Count > 0)
        {
            _outputManager?.EnableBilingual(_targetLanguages[0]);
        }

        ChunkLog($"[PIPELINE] Creating task list for {chunks.Count} chunks...");
        var maxParallelism = Math.Min(Environment.ProcessorCount, 4);
        ChunkLog($"[PIPELINE] Max parallelism: {maxParallelism}");
        using var semaphore = new SemaphoreSlim(maxParallelism);
        var completedCount = 0;
        var lockObj = new object();

        var tasks = new List<Task<ChunkResult>>();
        ChunkLog($"[PIPELINE] Creating tasks for all {chunks.Count} chunks...");
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunkIndex = i;
            var chunk = chunks[i];
            ct.ThrowIfCancellationRequested();

            ChunkLog($"[PIPELINE] Creating task for chunk {chunkIndex + 1}: Start={chunk.Start}, End={chunk.End}");

            var task = Task.Run(async () =>
            {
                ChunkLog($"[CHUNK-{chunkIndex + 1}] Started");

                await semaphore.WaitAsync(ct);
                try
                {
                    _progressReporter.SetStage(TranscriptionStage.ExtractingAudio);

                    var tempWav = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".wav");
                    try
                    {
                        ChunkLog($"[CHUNK-{chunkIndex + 1}] Extracting audio to: {tempWav}");
                        await ExtractAudioChunkAsync(chunk.Start, chunk.End, tempWav, ct);
                        ChunkLog($"[CHUNK-{chunkIndex + 1}] Audio extracted successfully");

                        _progressReporter.SetStage(TranscriptionStage.Transcribing);
                        ChunkLog($"[CHUNK-{chunkIndex + 1}] Starting transcription...");
                        var subtitles = await TranscribeChunkAsync(tempWav, chunk.Start, ct);
                        var wordCount = subtitles.Sum(s => s.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
                        ChunkLog($"[CHUNK-{chunkIndex + 1}] Transcription complete. Got {subtitles.Count} subtitles, {wordCount} words");
                        for (int si = 0; si < subtitles.Count; si++)
                        {
                            var p = subtitles[si];
                            ChunkLog($"[CHUNK-{chunkIndex + 1}]   Subtitle {si + 1}: [{p.StartTime} - {p.EndTime}] \"{TruncateText(p.Text, 50)}\"");
                        }

                        return new ChunkResult(chunkIndex, subtitles, chunk.End);
                    }
                    finally
                    {
                        if (File.Exists(tempWav))
                        {
                            ChunkLog($"[CHUNK-{chunkIndex + 1}] Deleting temp wav file");
                            File.Delete(tempWav);
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct);

            tasks.Add(task);
        }

        ChunkLog($"[PIPELINE] All {tasks.Count} tasks created. Waiting for completion...");
        var results = await Task.WhenAll(tasks);
        ChunkLog($"[PIPELINE] All tasks completed. Got {results.Length} results");

        Array.Sort(results, (a, b) => a.Index.CompareTo(b.Index));
        ChunkLog($"[PIPELINE] Processing {results.Length} results...");

        for (var i = 0; i < results.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            var result = results[i];
            var subtitles = result.Subtitles;
            var subtitlesCount = subtitles.Count;

            ChunkLog($"[AGGREGATE] Chunk {i + 1}: received {subtitlesCount} subtitles");

            var chunkSubtitle = new Subtitle();
            foreach (var p in subtitles)
                chunkSubtitle.Paragraphs.Add(p);

            if (_outputManager != null)
            {
                _outputManager.WriteOriginal(chunkSubtitle);
            }

            int keptCount = 0;
            int duplicateCount = 0;
            bool isFirstSubtitleOfChunk = true;
            foreach (var sub in subtitles)
            {
                bool isDuplicate;
                if (lastKeptSubtitle != null && isFirstSubtitleOfChunk)
                {
                    isDuplicate = _duplicateDetector.IsDuplicateAtBoundary(
                        sub, lastKeptSubtitle, SubtitleDuplicateDetector.CalculateSimilarity);
                }
                else if (lastKeptSubtitle != null)
                {
                    isDuplicate = _duplicateDetector.IsDuplicate(
                        sub, lastKeptSubtitle, SubtitleDuplicateDetector.CalculateSimilarity);
                }
                else
                {
                    isDuplicate = false;
                }

                if (isDuplicate)
                {
                    var score = SubtitleDuplicateDetector.CalculateSimilarity(sub.Text, lastKeptSubtitle.Text);
                    var overlap = sub.StartTime.TotalMilliseconds - lastKeptSubtitle.EndTime.TotalMilliseconds;
                    ChunkLog($"[DEDUP] Chunk {i + 1} subtitle REJECTED: overlap={overlap:F0}ms, similarity={score:P0}, prev=\"{TruncateText(lastKeptSubtitle.Text, 40)}\", curr=\"{TruncateText(sub.Text, 40)}\"");
                    duplicateCount++;
                    continue;
                }

                ChunkLog($"[DEDUP] Chunk {i + 1} subtitle ACCEPTED: start={sub.StartTime}, text=\"{TruncateText(sub.Text, 40)}\"");
                lock (_srtLock)
                {
                    writer.Append(sub);
                    totalSubtitles++;
                    keptCount++;
                }
                lastKeptSubtitle = sub;
                isFirstSubtitleOfChunk = false;
            }

            if (duplicateCount > 0 || keptCount > 0)
            {
                ChunkLog($"[AGGREGATE] Chunk {i + 1}: received={subtitlesCount}, kept={keptCount}, duplicates_skipped={duplicateCount}");
            }

            completedCount++;
            _totalChunksProcessed++;
            _totalSubtitlesGenerated += subtitlesCount;

            _progressReporter.UpdateChunk(completedCount, totalSubtitles,
                lastKeptSubtitle?.Text?.Length > 50
                    ? lastKeptSubtitle.Text.Substring(0, 50) + "..."
                    : lastKeptSubtitle?.Text ?? "");

            if (completedCount % checkpointInterval == 0 || completedCount == chunks.Count)
            {
                await SaveCheckpointAsync(completedCount, chunks.Count, result.ChunkEnd, ct);
            }

            if (_outputManager != null && _translationProvider != null && _targetLanguages != null)
            {
                _progressReporter.SetStage(TranscriptionStage.Translating);

                foreach (var targetLang in _targetLanguages)
                {
                    try
                    {
                        var translated = await _translationProvider.TranslateAsync(chunkSubtitle, targetLang, ct);
                        _outputManager.WriteTranslation(targetLang, translated);
                        _totalTranslationsCompleted++;

                        if (_generateBilingual)
                        {
                            var bilingual = BilingualMuxer.Combine(chunkSubtitle, translated);
                            _outputManager.WriteBilingual(chunkSubtitle, bilingual);
                        }
                    }
                    catch (Exception ex)
                    {
                        ChunkLog($"[PIPELINE] Translation failed for {targetLang}: {ex.Message}");
                        _totalTranslationErrors++;
                    }
                }
            }
        }

        _progressReporter.Stop();
        _checkpointManager.Delete();
        _outputManager?.Flush();

        ChunkLog($"[PIPELINE] All chunks processed. Total subtitles written: {totalSubtitles}");
        ChunkLog($"[PIPELINE] Reading final SRT from: {_outputSrtPath}");
        ChunkLog($"[PIPELINE] SRT file exists: {File.Exists(_outputSrtPath)}");

        var finalResult = new Subtitle();
        try
        {
            if (File.Exists(_outputSrtPath))
            {
                var srtLines = File.ReadAllLines(_outputSrtPath);
                ChunkLog($"[PIPELINE] SRT file has {srtLines.Length} lines");
                new SubRip().LoadSubtitle(finalResult, srtLines.ToList(), _outputSrtPath);
                ChunkLog($"[PIPELINE] Loaded {finalResult.Paragraphs.Count} paragraphs from SRT");
            }
            else
            {
                ChunkLog("[PIPELINE] ERROR: Output SRT file does not exist!");
            }
        }
        catch (Exception ex)
        {
            ChunkLog($"[PIPELINE] Exception reading SRT: {ex.Message}");
        }

        _runEndTime = DateTime.Now;
        LogRunSummary(finalResult);
        return finalResult;
    }

    private record ChunkResult(int Index, List<Paragraph> Subtitles, TimeSpan ChunkEnd);

    private void RestoreTranslationState(TranscriptionCheckpoint checkpoint)
    {
        if (!checkpoint.TranslationEnabled)
        {
            ChunkLog("[RESUME] Translation was not enabled in checkpoint");
            return;
        }

        // Dispose old output manager if exists (prevents file handle leaks on multiple resumes)
        if (_outputManager != null)
        {
            ChunkLog("[RESUME] Disposing old output manager");
            _outputManager.Dispose();
        }

        _targetLanguages = checkpoint.TargetLanguages?.Count > 0
            ? new List<string>(checkpoint.TargetLanguages)
            : new List<string> { "en" };

        _generateBilingual = checkpoint.GenerateBilingual;

        var outputDir = Path.GetDirectoryName(_outputSrtPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(_outputSrtPath);

        _outputManager = new SubtitleOutputManager(outputDir, baseName, _language);
        _outputManager.InitializeOriginal();

        foreach (var lang in _targetLanguages)
        {
            _outputManager.AddTargetLanguage(lang);
        }

        if (_generateBilingual && _targetLanguages.Count > 0)
        {
            _outputManager.EnableBilingual(_targetLanguages[0]);
        }

        _translationProvider = new MarianMTTranslationProvider();

        ChunkLog($"[RESUME] Translation state restored: {string.Join(", ", _targetLanguages)}, bilingual={_generateBilingual}");
        ChunkLog($"[RESUME] CompletedTranslationChunks: {checkpoint.CompletedTranslationChunks.Count} languages tracked");
    }

    private void LogRunSummary(Subtitle? result)
    {
        var elapsed = _runEndTime - _runStartTime;
        var wordCount = 0;
        var charCount = 0;
        if (result != null)
        {
            foreach (var p in result.Paragraphs)
            {
                wordCount += p.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                charCount += p.Text.Length;
            }
        }

        var translationStatus = _outputManager?.HasTranslations == true ? "PASS" : "N/A";
        var bilingualStatus = _outputManager?.HasBilingual == true ? "PASS" : "N/A";
        var checkpointStatus = _checkpointManager?.Current != null ? "PASS" : "N/A";

        ChunkLog(string.Empty);
        ChunkLog("========================= Speech-to-Text Summary =========================");
        ChunkLog($"Video:              {_videoPath}");
        ChunkLog($"Engine:             {_engineId}");
        ChunkLog($"Language:           {_language}");
        ChunkLog($"Mode:               {(_totalChunksProcessed > 1 ? "Chunked" : "Legacy")}");
        ChunkLog($"Video Duration:     {_videoDuration:hh\\:mm\\:ss}");
        ChunkLog($"Total Chunks:       {_totalChunksProcessed}");
        ChunkLog($"Chunks Failed:      {_totalChunksFailed}");
        ChunkLog($"Elapsed Time:       {elapsed:mm\\:ss\\.fff}");
        ChunkLog($"Subtitles:          {result?.Paragraphs.Count ?? 0}");
        ChunkLog($"Words:              {wordCount:N0}");
        ChunkLog($"Characters:         {charCount:N0}");
        ChunkLog($"Translation:        {translationStatus}");
        ChunkLog($"Bilingual:          {bilingualStatus}");
        ChunkLog($"Checkpoint:          {checkpointStatus}");
        ChunkLog($"Translation Errors: {_totalTranslationErrors}");
        ChunkLog("=======================================================================");
        ChunkLog($"Overall:            {(_totalChunksFailed == 0 && _totalTranslationErrors == 0 ? "PASS" : "FAIL")}");
        ChunkLog("========================================================================");
    }

    private async Task<Subtitle?> ResumeFromCheckpointAsync(TranscriptionCheckpoint checkpoint, CancellationToken ct)
    {
        _runStartTime = DateTime.Now;
        _totalChunksProcessed = 0;
        _totalChunksFailed = 0;
        _totalSubtitlesGenerated = 0;
        _totalTranslationsCompleted = 0;
        _totalTranslationErrors = 0;

        RestoreTranslationState(checkpoint);

        var lastEnd = checkpoint.LastCompletedChunkEnd;
        var totalChunks = checkpoint.TotalChunks;

        var silenceRegions = await DetectSilenceRegionsAsync(ct);
        var allChunks = _chunker.CalculateChunkBoundaries(_videoDuration, silenceRegions, _log);

        var resumeIndex = checkpoint.CompletedChunks;
        var remainingChunks = allChunks.Skip(resumeIndex).ToList();

        _progressReporter.Start(totalChunks);
        _progressReporter.SetStage(TranscriptionStage.Transcribing);

        var result = new Subtitle();
        if (File.Exists(_outputSrtPath))
        {
            try
            {
                new SubRip().LoadSubtitle(result, File.ReadAllLines(_outputSrtPath).ToList(), _outputSrtPath);
            }
            catch { }
        }

        var lastKeptSubtitle = result.Paragraphs.LastOrDefault();
        var totalSubtitles = result.Paragraphs.Count;

        using var writer = new StreamingSrtWriter(_outputSrtPath);
        foreach (var p in result.Paragraphs)
            writer.Append(p);

        var checkpointInterval = Math.Max(1, remainingChunks.Count / 10);

        for (var i = 0; i < remainingChunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var chunk = remainingChunks[i];
            _progressReporter.SetStage(TranscriptionStage.ExtractingAudio);

            var tempWav = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".wav");
            try
            {
                await ExtractAudioChunkAsync(chunk.Start, chunk.End, tempWav, ct);

                _progressReporter.SetStage(TranscriptionStage.Transcribing);
                var subtitles = await TranscribeChunkAsync(tempWav, chunk.Start, ct);
                _totalChunksProcessed++;
                _totalSubtitlesGenerated += subtitles.Count;

                _progressReporter.SetStage(TranscriptionStage.PostProcessing);

                var chunkSubtitle = new Subtitle();
                foreach (var p in subtitles)
                    chunkSubtitle.Paragraphs.Add(p);

                if (_outputManager != null)
                {
                    _outputManager.WriteOriginal(chunkSubtitle);
                }

                bool isFirstSubtitleOfChunk = true;
                foreach (var sub in subtitles)
                {
                    bool isDuplicate;
                    if (lastKeptSubtitle != null && isFirstSubtitleOfChunk)
                    {
                        isDuplicate = _duplicateDetector.IsDuplicateAtBoundary(
                            sub, lastKeptSubtitle, SubtitleDuplicateDetector.CalculateSimilarity);
                    }
                    else if (lastKeptSubtitle != null)
                    {
                        isDuplicate = _duplicateDetector.IsDuplicate(
                            sub, lastKeptSubtitle, SubtitleDuplicateDetector.CalculateSimilarity);
                    }
                    else
                    {
                        isDuplicate = false;
                    }

                    if (isDuplicate)
                    {
                        continue;
                    }

                    lock (_srtLock)
                    {
                        writer.Append(sub);
                        totalSubtitles++;
                    }
                    lastKeptSubtitle = sub;
                    isFirstSubtitleOfChunk = false;
                }

                var globalIndex = resumeIndex + i + 1;
                _progressReporter.UpdateChunk(globalIndex, totalSubtitles,
                    lastKeptSubtitle?.Text?.Length > 50
                        ? lastKeptSubtitle.Text.Substring(0, 50) + "..."
                        : lastKeptSubtitle?.Text ?? "");

                if ((i + 1) % checkpointInterval == 0 || i == remainingChunks.Count - 1)
                {
                    await SaveCheckpointAsync(globalIndex, totalChunks, chunk.End, ct);
                }

                if (_outputManager != null && _translationProvider != null && _targetLanguages != null)
                {
                    _progressReporter.SetStage(TranscriptionStage.Translating);

                    foreach (var targetLang in _targetLanguages)
                    {
                        try
                        {
                            var translated = await _translationProvider.TranslateAsync(chunkSubtitle, targetLang, ct);
                            _outputManager.WriteTranslation(targetLang, translated);
                            _totalTranslationsCompleted++;

                            if (_generateBilingual)
                            {
                                var bilingual = BilingualMuxer.Combine(chunkSubtitle, translated);
                                _outputManager.WriteBilingual(chunkSubtitle, bilingual);
                            }
                        }
                        catch (Exception ex)
                        {
                            ChunkLog($"[RESUME] Translation failed for {targetLang}: {ex.Message}");
                            _totalTranslationErrors++;
                        }
                    }
                }
            }
            finally
            {
                if (File.Exists(tempWav))
                    File.Delete(tempWav);
            }
        }

        _progressReporter.Stop();
        _checkpointManager.Delete();
        _outputManager?.Flush();

        try
        {
            result = new Subtitle();
            new SubRip().LoadSubtitle(result, File.ReadAllLines(_outputSrtPath).ToList(), _outputSrtPath);
        }
        catch { }

        _runEndTime = DateTime.Now;
        LogRunSummary(result);
        return result;
    }

    private async Task<List<(TimeSpan start, TimeSpan end)>> DetectSilenceRegionsAsync(CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var regions = new List<(TimeSpan start, TimeSpan end)>();

            // NOTE: Silence detection is not yet implemented.
            // Returning an empty list triggers SilenceAwareChunker's fallback mode,
            // which creates even-duration chunks (20-60s based on settings).
            // This is acceptable for now; real VAD-based detection can be added later.

            return regions;
        }, ct);
    }

    private async Task ExtractAudioChunkAsync(TimeSpan start, TimeSpan end, string outputPath, CancellationToken ct)
    {
        await _audioExtractor.ExtractChunkAsync(start, end, outputPath, ct);
    }

    private async Task<List<Paragraph>> TranscribeChunkAsync(string audioPath, TimeSpan offset, CancellationToken ct)
    {
        var subtitle = await _audioTranscriber.TranscribeChunkAsync(audioPath, offset, ct);
        return subtitle.Paragraphs;
    }

    private async Task SaveCheckpointAsync(int completedChunks, int totalChunks, TimeSpan lastEnd, CancellationToken ct)
    {
        _progressReporter.SetStage(TranscriptionStage.SavingCheckpoint);

        var checkpoint = new TranscriptionCheckpoint
        {
            VideoPath = _videoPath,
            EngineId = _engineId,
            ModelId = _modelId,
            Language = _language,
            VideoDurationTicks = _videoDuration.Ticks,
            TotalChunks = totalChunks,
            CompletedChunks = completedChunks,
            LastCompletedChunkEndTicks = lastEnd.Ticks,
            SrtPath = _outputSrtPath,
            TranslationEnabled = _outputManager != null,
            TargetLanguages = _targetLanguages ?? new List<string>(),
            GenerateBilingual = _generateBilingual
        };

        _checkpointManager.Save(checkpoint);
    }

    public void Cancel()
    {
        _cts?.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _cts?.Cancel();
        _cts?.Dispose();
        _outputManager?.Dispose();
        _disposed = true;
    }
}

public static class PipelineFactory
{
    public static PipelineController Create(
        string videoPath,
        string outputSrtPath,
        Engines.ISpeechToTextEngine engine,
        string modelName,
        string language,
        int audioTrackIndex = 0)
    {
        return CreateAsync(videoPath, outputSrtPath, engine, modelName, language, audioTrackIndex).GetAwaiter().GetResult();
    }

    public static async Task<PipelineController> CreateAsync(
        string videoPath,
        string outputSrtPath,
        Engines.ISpeechToTextEngine engine,
        string modelName,
        string language,
        int audioTrackIndex = 0,
        CancellationToken ct = default)
    {
        var audioExtractor = new FfmpegAudioExtractor(videoPath, audioTrackIndex);
        var audioTranscriber = CreateTranscriber(engine, modelName, language);

        var duration = await audioExtractor.GetVideoDurationAsync(ct);

        return new PipelineController(
            videoPath,
            outputSrtPath,
            audioExtractor,
            audioTranscriber,
            duration,
            audioTrackIndex);
    }

    public static IAudioTranscriber CreateTranscriber(
        Engines.ISpeechToTextEngine engine,
        string modelName,
        string language,
        ProcessOutputRouter? router = null)
    {
        var extraArgs = engine.CommandLineParameter ?? string.Empty;

        return engine switch
        {
            Engines.VoskEngine vosk => new VoskAdapter(vosk, modelName, language, extraArgs, router),
            Engines.ParakeetCppEngine parakeet => new ParakeetAdapter(parakeet, modelName, language, extraArgs, router),
            Engines.CrispAsrEngine crispAsr => new CrispAsrAdapter(crispAsr, modelName, language, extraArgs, router),
            Engines.Qwen3AsrCppEngine qwen3 => new Qwen3AsrAdapter(qwen3, modelName, language, extraArgs, router),
            _ => new WhisperTranscriber(engine, modelName, language, extraArgs, router),
        };
    }
}