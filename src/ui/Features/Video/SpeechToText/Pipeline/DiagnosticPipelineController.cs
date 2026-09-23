using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.Engines;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class DiagnosticPipelineController : IDisposable
{
    private readonly string _videoPath;
    private readonly ISpeechToTextEngine _engine;
    private readonly string _modelId;
    private readonly string _language;
    private readonly int _audioTrackIndex;
    private readonly bool _preserveDiagnostics;

    private readonly string _diagnosticsFolder;
    private readonly string _runTimestamp;
    private readonly Action<string>? _log;

    private bool _disposed;

    public bool PreserveDiagnostics => _preserveDiagnostics;
    public string DiagnosticsFolder => _diagnosticsFolder;

    public DiagnosticPipelineController(
        string videoPath,
        ISpeechToTextEngine engine,
        string modelId,
        string language,
        int audioTrackIndex = 0,
        bool preserveDiagnostics = true,
        Action<string>? log = null)
    {
        _videoPath = videoPath;
        _engine = engine;
        _modelId = modelId;
        _language = language;
        _audioTrackIndex = audioTrackIndex;
        _preserveDiagnostics = preserveDiagnostics;
        _log = log;

        _runTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        _diagnosticsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "Diagnostics",
            $"Run_{_runTimestamp}");

        if (_preserveDiagnostics)
        {
            Directory.CreateDirectory(_diagnosticsFolder);
            ChunkLog($"Diagnostics folder created: {_diagnosticsFolder}");
        }
    }

    private void ChunkLog(string message)
    {
        var prefixed = $"[DIAG] {message}";
        System.Diagnostics.Debug.WriteLine(prefixed);
        _log?.Invoke(prefixed);
    }

    public async Task<DiagnosticReport> RunDiagnosticAsync(
        string videoPath,
        TimeSpan videoDuration,
        CancellationToken ct = default)
    {
        var report = new DiagnosticReport
        {
            VideoPath = videoPath,
            VideoDuration = videoDuration,
            RunTimestamp = _runTimestamp,
            EngineId = _engine.Choice,
            ModelId = _modelId,
            Language = _language
        };

        if (!_preserveDiagnostics)
        {
            ChunkLog("Diagnostics not enabled");
            return report;
        }

        ChunkLog("=== STARTING DIAGNOSTIC RUN ===");
        ChunkLog($"Video: {videoPath}");
        ChunkLog($"Duration: {videoDuration}");
        ChunkLog($"Engine: {_engine.Choice}");
        ChunkLog($"Model: {_modelId}");

        var tempPath = Path.GetTempPath();
        var legacyFolder = Path.Combine(_diagnosticsFolder, "Legacy");
        var chunkedFolder = Path.Combine(_diagnosticsFolder, "Chunked");

        Directory.CreateDirectory(legacyFolder);
        Directory.CreateDirectory(chunkedFolder);

        report.LegacyFolder = legacyFolder;
        report.ChunkedFolder = chunkedFolder;

        try
        {
            report.LegacyResult = await RunLegacyModeAsync(videoPath, legacyFolder, tempPath, ct);
            report.ChunkedResult = await RunChunkedModeAsync(videoPath, videoDuration, chunkedFolder, tempPath, ct);
            report.Comparison = GenerateComparisonReport(report.LegacyResult, report.ChunkedResult);

            SaveReport(report);
        }
        catch (Exception ex)
        {
            ChunkLog($"DIAGNOSTIC ERROR: {ex.Message}");
            report.Error = ex.Message;
        }

        return report;
    }

    private async Task<LegacyDiagnosticResult> RunLegacyModeAsync(
        string videoPath,
        string outputFolder,
        string tempPath,
        CancellationToken ct)
    {
        var result = new LegacyDiagnosticResult();

        ChunkLog("");
        ChunkLog("========== LEGACY MODE ==========");
        ChunkLog($"Output folder: {outputFolder}");

        var legacyWavPath = Path.Combine(outputFolder, "input.wav");
        var ffmpegCmdPath = Path.Combine(outputFolder, "ffmpeg_command.txt");
        var whisperCmdPath = Path.Combine(outputFolder, "whisper_command.txt");
        var rawSrtPath = Path.Combine(outputFolder, "whisper_raw.srt");
        var parsedPath = Path.Combine(outputFolder, "parsed.txt");
        var finalPath = Path.Combine(outputFolder, "final.srt");

        result.WavPath = legacyWavPath;
        result.FFmpegCommandPath = ffmpegCmdPath;
        result.WhisperCommandPath = whisperCmdPath;
        result.RawSrtPath = rawSrtPath;
        result.ParsedPath = parsedPath;
        result.FinalPath = finalPath;

        try
        {
            var extractor = new FfmpegAudioExtractor(videoPath, _audioTrackIndex);

            var audioTrackArg = _audioTrackIndex > 0 ? $"-map 0:a:{_audioTrackIndex}" : "-vn";
            var ffmpegArgs = $"-y -i \"{videoPath}\" {audioTrackArg} -ar 16000 -ac 1 -ab 32k -af volume=1.75 -hide_banner -loglevel error -f wav \"{legacyWavPath}\"";

            File.WriteAllText(ffmpegCmdPath, ffmpegArgs);
            ChunkLog($"FFmpeg command saved: {ffmpegArgs}");

            using (var runner = new AsyncProcessRunner())
            {
                var exitCode = await runner.RunAsync("ffmpeg", ffmpegArgs, ct, captureOutput: true);
                ChunkLog($"FFmpeg exit: {exitCode}");

                if (!string.IsNullOrWhiteSpace(runner.ErrorOutput))
                {
                    File.AppendAllText(ffmpegCmdPath, Environment.NewLine + "STDERR: " + runner.ErrorOutput);
                }
            }

            if (File.Exists(legacyWavPath))
            {
                var fi = new FileInfo(legacyWavPath);
                ChunkLog($"WAV created: {fi.Length:N0} bytes");
                result.WavSize = fi.Length;

                var probeResult = await GetAudioPropertiesAsync(legacyWavPath, ct);
                result.WavDuration = probeResult.Duration;
                result.SampleRate = probeResult.SampleRate;
                result.Channels = probeResult.Channels;
                ChunkLog($"WAV duration: {result.WavDuration}");
                ChunkLog($"Sample rate: {result.SampleRate}");
                ChunkLog($"Channels: {result.Channels}");
            }

            var engine = _engine;
            var exe = engine.GetExecutable();
            var model = engine.GetModelForCmdLine(_modelId);

            var paramsBuilder = new StringBuilder();
            paramsBuilder.Append($"--language {_language} --model \"{model}\" --output-srt ");

            if (engine is WhisperEngineCTranslate2)
            {
                paramsBuilder.Replace("--model", "--model_directory");
            }

            var extraArgs = engine.CommandLineParameter ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(extraArgs))
            {
                paramsBuilder.Append($" {extraArgs}");
            }

            paramsBuilder.Append($" \"{legacyWavPath}\"");

            var whisperCommand = paramsBuilder.ToString();
            File.WriteAllText(whisperCmdPath, $"{exe} {whisperCommand}");
            ChunkLog($"Whisper command saved: {exe} {whisperCommand}");

            using (var runner = new AsyncProcessRunner())
            {
                var exitCode = await runner.RunAsync(exe, whisperCommand, ct, captureOutput: true);
                ChunkLog($"Whisper exit: {exitCode}");

                if (!string.IsNullOrWhiteSpace(runner.ErrorOutput))
                {
                    File.AppendAllText(whisperCmdPath, Environment.NewLine + "STDERR: " + runner.ErrorOutput);
                }
            }

            var tempSrt = legacyWavPath + ".srt";
            if (File.Exists(tempSrt))
            {
                File.Copy(tempSrt, rawSrtPath, true);
                ChunkLog($"Raw SRT copied to: {rawSrtPath}");

                var rawLines = File.ReadAllLines(tempSrt);

                var subtitle = new Subtitle();
                new SubRip().LoadSubtitle(subtitle, rawLines.ToList(), tempSrt);
                result.RawSubtitles = subtitle.Paragraphs.Count;
                result.RawWords = subtitle.Paragraphs.Sum(p => p.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);

                if (subtitle.Paragraphs.Count > 0)
                {
                    result.FirstSubtitleText = subtitle.Paragraphs.First().Text;
                    result.LastSubtitleText = subtitle.Paragraphs.Last().Text;
                }

                File.WriteAllText(parsedPath, FormatSubtitleDebug(subtitle));
                ChunkLog($"Parsed subtitle: {subtitle.Paragraphs.Count} paragraphs, {result.RawWords} words");
                ChunkLog($"First sub: {TruncateText(result.FirstSubtitleText, 50)}");
                ChunkLog($"Last sub: {TruncateText(result.LastSubtitleText, 50)}");

                File.Copy(tempSrt, finalPath, true);
                result.FinalSubtitles = subtitle.Paragraphs.Count;
                result.FinalWords = result.RawWords;

                File.Delete(tempSrt);
            }
            else
            {
                ChunkLog("ERROR: No SRT file produced by Whisper!");
            }
        }
        catch (Exception ex)
        {
            ChunkLog($"LEGACY ERROR: {ex.Message}");
            result.Error = ex.Message;
        }

        ChunkLog($"Legacy mode complete: {result.RawSubtitles} raw, {result.FinalSubtitles} final");
        return result;
    }

    private async Task<ChunkedDiagnosticResult> RunChunkedModeAsync(
        string videoPath,
        TimeSpan videoDuration,
        string outputFolder,
        string tempPath,
        CancellationToken ct)
    {
        var result = new ChunkedDiagnosticResult();

        ChunkLog("");
        ChunkLog("========== CHUNKED MODE ==========");
        ChunkLog($"Output folder: {outputFolder}");

        var extractor = new FfmpegAudioExtractor(videoPath, _audioTrackIndex);
        var silenceRegions = await DetectSilenceRegionsAsync(ct);
        var chunker = new SilenceAwareChunker();
        var chunks = chunker.CalculateChunkBoundaries(videoDuration, silenceRegions, _log);

        ChunkLog($"Video duration: {videoDuration}");
        ChunkLog($"Silence regions: {silenceRegions.Count}");
        ChunkLog($"Chunks: {chunks.Count}");

        result.ChunkCount = chunks.Count;
        result.Chunks = new List<ChunkDiagnosticResult>();

        var allSubtitles = new List<Paragraph>();
        var tempWavs = new List<string>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var chunkFolder = Path.Combine(outputFolder, $"Chunk_{i + 1:D3}");
            Directory.CreateDirectory(chunkFolder);

            var chunkResult = new ChunkDiagnosticResult
            {
                ChunkIndex = i + 1,
                ChunkStart = chunk.Start,
                ChunkEnd = chunk.End,
                ExpectedDuration = chunk.Duration
            };

            ChunkLog("");
            ChunkLog($"--- Chunk {i + 1}/{chunks.Count} ---");
            ChunkLog($"Start: {chunk.Start}, End: {chunk.End}, Duration: {chunk.Duration}");

            var chunkWavPath = Path.Combine(chunkFolder, "input.wav");
            var ffmpegCmdPath = Path.Combine(chunkFolder, "ffmpeg_command.txt");
            var whisperCmdPath = Path.Combine(chunkFolder, "whisper_command.txt");
            var rawSrtPath = Path.Combine(chunkFolder, "whisper_raw.srt");
            var parsedPath = Path.Combine(chunkFolder, "parsed.txt");

            chunkResult.WavPath = chunkWavPath;
            chunkResult.FFmpegCommandPath = ffmpegCmdPath;
            chunkResult.WhisperCommandPath = whisperCmdPath;
            chunkResult.RawSrtPath = rawSrtPath;
            chunkResult.ParsedPath = parsedPath;

            try
            {
                var audioTrackArg = _audioTrackIndex > 0 ? $"-map 0:a:{_audioTrackIndex}" : "-vn";
                var ffmpegArgs = $"-y -ss {chunk.Start.TotalSeconds:0.000} -to {chunk.End.TotalSeconds:0.000} -i \"{videoPath}\" {audioTrackArg} -ar 16000 -ac 1 -ab 32k -af volume=1.75 -hide_banner -loglevel error -f wav \"{chunkWavPath}\"";

                File.WriteAllText(ffmpegCmdPath, ffmpegArgs);
                ChunkLog($"FFmpeg command: {ffmpegArgs}");

                using (var runner = new AsyncProcessRunner())
                {
                    var exitCode = await runner.RunAsync("ffmpeg", ffmpegArgs, ct, captureOutput: true);
                    ChunkLog($"FFmpeg exit: {exitCode}");
                }

                if (File.Exists(chunkWavPath))
                {
                    var fi = new FileInfo(chunkWavPath);
                    chunkResult.WavSize = fi.Length;

                    var probeResult = await GetAudioPropertiesAsync(chunkWavPath, ct);
                    chunkResult.WavDuration = probeResult.Duration;
                    chunkResult.SampleRate = probeResult.SampleRate;
                    chunkResult.Channels = probeResult.Channels;
                    ChunkLog($"WAV duration: {probeResult.Duration}");

                    tempWavs.Add(chunkWavPath);
                }

                var engine = _engine;
                var exe = engine.GetExecutable();
                var model = engine.GetModelForCmdLine(_modelId);

                var paramsBuilder = new StringBuilder();
                paramsBuilder.Append($"--language {_language} --model \"{model}\" --output-srt ");

                if (engine is WhisperEngineCTranslate2)
                {
                    paramsBuilder.Replace("--model", "--model_directory");
                }

                var extraArgs = engine.CommandLineParameter ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(extraArgs))
                {
                    paramsBuilder.Append($" {extraArgs}");
                }

                paramsBuilder.Append($" \"{chunkWavPath}\"");

                var whisperCommand = paramsBuilder.ToString();
                File.WriteAllText(whisperCmdPath, $"{exe} {whisperCommand}");
                ChunkLog($"Whisper command: {exe} {whisperCommand}");

                using (var runner = new AsyncProcessRunner())
                {
                    var exitCode = await runner.RunAsync(exe, whisperCommand, ct, captureOutput: true);
                    ChunkLog($"Whisper exit: {exitCode}");
                }

                var tempSrt = chunkWavPath + ".srt";
                if (File.Exists(tempSrt))
                {
                    File.Copy(tempSrt, rawSrtPath, true);
                    ChunkLog($"Raw SRT saved");

                    var rawLines = File.ReadAllLines(tempSrt);
                    var subtitle = new Subtitle();
                    new SubRip().LoadSubtitle(subtitle, rawLines.ToList(), tempSrt);

                    foreach (var p in subtitle.Paragraphs)
                    {
                        p.StartTime.TotalMilliseconds += chunk.Start.TotalMilliseconds;
                        p.EndTime.TotalMilliseconds += chunk.Start.TotalMilliseconds;
                    }

                    chunkResult.RawSubtitles = subtitle.Paragraphs.Count;
                    chunkResult.RawWords = subtitle.Paragraphs.Sum(p => p.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);

                    if (subtitle.Paragraphs.Count > 0)
                    {
                        chunkResult.FirstSubtitleText = subtitle.Paragraphs.First().Text;
                        chunkResult.LastSubtitleText = subtitle.Paragraphs.Last().Text;
                    }

                    File.WriteAllText(parsedPath, FormatSubtitleDebug(subtitle));
                    ChunkLog($"Parsed: {subtitle.Paragraphs.Count} subs, {chunkResult.RawWords} words");

                    allSubtitles.AddRange(subtitle.Paragraphs);

                    File.Delete(tempSrt);
                }
                else
                {
                    ChunkLog("ERROR: No SRT produced!");
                }
            }
            catch (Exception ex)
            {
                ChunkLog($"Chunk {i + 1} ERROR: {ex.Message}");
                chunkResult.Error = ex.Message;
            }

            result.Chunks.Add(chunkResult);
        }

        ChunkLog("");
        ChunkLog($"Total after merge (before dedup): {allSubtitles.Count} subtitles");

        var mergedPath = Path.Combine(outputFolder, "merged.txt");
        var mergedSubtitle = new Subtitle();
        foreach (var p in allSubtitles)
            mergedSubtitle.Paragraphs.Add(p);
        File.WriteAllText(mergedPath, FormatSubtitleDebug(mergedSubtitle));
        result.MergedSubtitles = allSubtitles.Count;
        result.MergedWords = allSubtitles.Sum(p => p.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);

        var duplicateDetector = new SubtitleDuplicateDetector();
        var lastKept = (Paragraph?)null;
        var rejectedCount = 0;
        var finalSubtitles = new List<Paragraph>();

        foreach (var sub in allSubtitles)
        {
            bool isDuplicate;
            if (lastKept != null)
            {
                isDuplicate = duplicateDetector.IsDuplicate(sub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity);
            }
            else
            {
                isDuplicate = false;
            }

            if (isDuplicate)
            {
                var score = SubtitleDuplicateDetector.CalculateSimilarity(sub.Text, lastKept.Text);
                ChunkLog($"REJECTED (dedup): similarity={score:P0}, prev=\"{TruncateText(lastKept.Text, 40)}\", curr=\"{TruncateText(sub.Text, 40)}\"");
                rejectedCount++;
                continue;
            }

            finalSubtitles.Add(sub);
            lastKept = sub;
        }

        ChunkLog($"After dedup: {finalSubtitles.Count} subtitles (rejected {rejectedCount})");

        result.RejectedByDedup = rejectedCount;

        var dedupedPath = Path.Combine(outputFolder, "deduped.txt");
        var dedupedSubtitle = new Subtitle();
        foreach (var p in finalSubtitles)
            dedupedSubtitle.Paragraphs.Add(p);
        File.WriteAllText(dedupedPath, FormatSubtitleDebug(dedupedSubtitle));

        var finalPath = Path.Combine(outputFolder, "final.srt");
        using (var writer = new StreamWriter(finalPath, false, Encoding.UTF8))
        {
            var srtIndex = 1;
            foreach (var p in finalSubtitles)
            {
                writer.WriteLine(srtIndex++);
                writer.WriteLine($"{FormatTimeCode(p.StartTime)} --> {FormatTimeCode(p.EndTime)}");
                writer.WriteLine(p.Text);
                writer.WriteLine();
            }
        }

        result.FinalSubtitles = finalSubtitles.Count;
        result.FinalWords = finalSubtitles.Sum(p => p.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        result.FinalPath = finalPath;

        foreach (var wav in tempWavs)
        {
            try { if (File.Exists(wav)) File.Delete(wav); } catch { }
        }

        ChunkLog($"Chunked mode complete: {result.FinalSubtitles} final subtitles");
        return result;
    }

    private ComparisonReport GenerateComparisonReport(LegacyDiagnosticResult legacy, ChunkedDiagnosticResult chunked)
    {
        var report = new ComparisonReport();

        ChunkLog("");
        ChunkLog("========== COMPARISON ==========");
        ChunkLog($"                  Legacy    Chunked    Delta");
        ChunkLog($"Raw Whisper:      {legacy.RawSubtitles,-8} {chunked.TotalRawSubtitles(),-8} {legacy.RawSubtitles - chunked.TotalRawSubtitles(),+8}");
        ChunkLog($"After Merge:      {legacy.RawSubtitles,-8} {chunked.MergedSubtitles,-8} {legacy.RawSubtitles - chunked.MergedSubtitles,+8}");
        ChunkLog($"After Dedup:      {legacy.FinalSubtitles,-8} {chunked.FinalSubtitles,-8} {legacy.FinalSubtitles - chunked.FinalSubtitles,+8}");
        ChunkLog($"Final:            {legacy.FinalSubtitles,-8} {chunked.FinalSubtitles,-8} {legacy.FinalSubtitles - chunked.FinalSubtitles,+8}");

        report.LegacyRaw = legacy.RawSubtitles;
        report.ChunkedRaw = chunked.TotalRawSubtitles();
        report.LegacyMerged = legacy.RawSubtitles;
        report.ChunkedMerged = chunked.MergedSubtitles;
        report.LegacyDeduped = legacy.FinalSubtitles;
        report.ChunkedDeduped = chunked.FinalSubtitles;
        report.LegacyFinal = legacy.FinalSubtitles;
        report.ChunkedFinal = chunked.FinalSubtitles;

        var rawLoss = legacy.RawSubtitles - chunked.TotalRawSubtitles();
        var mergeLoss = chunked.MergedSubtitles - chunked.TotalRawSubtitles();
        var dedupLoss = chunked.RejectedByDedup;
        var finalLoss = legacy.FinalSubtitles - chunked.FinalSubtitles;

        ChunkLog("");
        ChunkLog("========== WHERE LOSS OCCURS ==========");
        ChunkLog($"Raw Whisper: {(rawLoss == 0 ? "No loss" : $"Legacy has {rawLoss} more")}");
        ChunkLog($"Parser: No change in chunked mode");
        ChunkLog($"Merge: {(mergeLoss == 0 ? "No loss" : $"{mergeLoss} subtitles lost")}");
        ChunkLog($"Dedup: {(dedupLoss == 0 ? "No loss" : $"{dedupLoss} duplicates removed")}");
        ChunkLog($"Final: {(finalLoss == 0 ? "No loss" : $"Chunked has {finalLoss} fewer")}");

        report.FirstLossStage = DetermineFirstLossStage(legacy, chunked);
        report.RootCauseEvidence = GenerateRootCauseEvidence(legacy, chunked, report.FirstLossStage);

        return report;
    }

    private string DetermineFirstLossStage(LegacyDiagnosticResult legacy, ChunkedDiagnosticResult chunked)
    {
        var rawDiff = legacy.RawSubtitles - chunked.TotalRawSubtitles();
        if (rawDiff != 0) return "Whisper Transcription";

        var mergeLoss = chunked.MergedSubtitles - chunked.TotalRawSubtitles();
        if (mergeLoss != 0) return "Chunk Merge";

        if (chunked.RejectedByDedup > 0) return "Duplicate Detection";

        var finalDiff = legacy.FinalSubtitles - chunked.FinalSubtitles;
        if (finalDiff != 0) return "Unknown";

        return "No Loss Detected";
    }

    private string GenerateRootCauseEvidence(LegacyDiagnosticResult legacy, ChunkedDiagnosticResult chunked, string firstLossStage)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"First point of loss: {firstLossStage}");
        sb.AppendLine();
        sb.AppendLine("Evidence:");

        switch (firstLossStage)
        {
            case "Whisper Transcription":
                sb.AppendLine($"  - Legacy raw: {legacy.RawSubtitles} subtitles");
                sb.AppendLine($"  - Chunked total raw: {chunked.TotalRawSubtitles()} subtitles");
                sb.AppendLine($"  - Difference: {legacy.RawSubtitles - chunked.TotalRawSubtitles()}");
                break;
            case "Chunk Merge":
                sb.AppendLine($"  - Sum of chunk raws: {chunked.TotalRawSubtitles()}");
                sb.AppendLine($"  - After merge: {chunked.MergedSubtitles}");
                break;
            case "Duplicate Detection":
                sb.AppendLine($"  - After merge: {chunked.MergedSubtitles}");
                sb.AppendLine($"  - Rejected by dedup: {chunked.RejectedByDedup}");
                sb.AppendLine($"  - Final: {chunked.FinalSubtitles}");
                break;
            default:
                sb.AppendLine("  - No measurable loss detected at any stage");
                break;
        }

        return sb.ToString();
    }

    private void SaveReport(DiagnosticReport report)
    {
        var reportPath = Path.Combine(_diagnosticsFolder, "comparison_report.txt");

        var sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine("                     DIAGNOSTIC PIPELINE REPORT");
        sb.AppendLine("================================================================================");
        sb.AppendLine();
        sb.AppendLine($"Timestamp: {report.RunTimestamp}");
        sb.AppendLine($"Video: {report.VideoPath}");
        sb.AppendLine($"Duration: {report.VideoDuration}");
        sb.AppendLine($"Engine: {report.EngineId}");
        sb.AppendLine($"Model: {report.ModelId}");
        sb.AppendLine($"Language: {report.Language}");
        sb.AppendLine();

        if (report.LegacyResult != null)
        {
            sb.AppendLine("LEGACY MODE");
            sb.AppendLine("-----------");
            sb.AppendLine($"Video: {report.VideoPath}");
            sb.AppendLine($"Duration: {report.VideoDuration.TotalSeconds:F1} sec");
            sb.AppendLine();
            sb.AppendLine("Audio Extraction:");
            sb.AppendLine($"  FFmpeg Command: {File.ReadAllText(report.LegacyResult.FFmpegCommandPath)}");
            sb.AppendLine($"  Output WAV: {report.LegacyResult.WavPath}");
            sb.AppendLine($"  Actual Duration: {report.LegacyResult.WavDuration}");
            sb.AppendLine($"  Sample Rate: {report.LegacyResult.SampleRate} Hz");
            sb.AppendLine($"  Channels: {report.LegacyResult.Channels}");
            sb.AppendLine();
            sb.AppendLine("Whisper:");
            sb.AppendLine($"  Command: {File.ReadAllText(report.LegacyResult.WhisperCommandPath)}");
            sb.AppendLine($"  Raw SRT: {report.LegacyResult.RawSrtPath}");
            sb.AppendLine($"  Subtitles: {report.LegacyResult.RawSubtitles}");
            sb.AppendLine($"  Words: {report.LegacyResult.RawWords}");
            sb.AppendLine($"  First Sub: {report.LegacyResult.FirstSubtitleText}");
            sb.AppendLine($"  Last Sub: {report.LegacyResult.LastSubtitleText}");
            sb.AppendLine();
            sb.AppendLine("Final Output:");
            sb.AppendLine($"  Subtitles: {report.LegacyResult.FinalSubtitles}");
            sb.AppendLine($"  Words: {report.LegacyResult.FinalWords}");
            sb.AppendLine();
        }

        if (report.ChunkedResult != null)
        {
            sb.AppendLine("CHUNKED MODE");
            sb.AppendLine("----------------------------------------");

            foreach (var chunk in report.ChunkedResult.Chunks)
            {
                sb.AppendLine($"Chunk {chunk.ChunkIndex}:");
                sb.AppendLine($"  Start: {chunk.ChunkStart}");
                sb.AppendLine($"  End: {chunk.ChunkEnd}");
                sb.AppendLine($"  Expected Duration: {chunk.ExpectedDuration}");
                sb.AppendLine();
                sb.AppendLine("  Audio:");
                if (File.Exists(chunk.FFmpegCommandPath))
                    sb.AppendLine($"    FFmpeg Command: {File.ReadAllText(chunk.FFmpegCommandPath)}");
                sb.AppendLine($"    Output WAV: {chunk.WavPath}");
                sb.AppendLine($"    Actual Duration: {chunk.WavDuration}");
                sb.AppendLine();
                sb.AppendLine("  Whisper:");
                if (File.Exists(chunk.WhisperCommandPath))
                    sb.AppendLine($"    Command: {File.ReadAllText(chunk.WhisperCommandPath)}");
                sb.AppendLine($"    Raw SRT: {chunk.RawSrtPath}");
                sb.AppendLine($"    Subtitles: {chunk.RawSubtitles}");
                sb.AppendLine($"    Words: {chunk.RawWords}");
                sb.AppendLine();
            }

            sb.AppendLine("After Merge:");
            sb.AppendLine($"  Subtitles: {report.ChunkedResult.MergedSubtitles}");
            sb.AppendLine($"  Words: {report.ChunkedResult.MergedWords}");
            sb.AppendLine();
            sb.AppendLine("After Duplicate Filter:");
            sb.AppendLine($"  Rejected: {report.ChunkedResult.RejectedByDedup}");
            sb.AppendLine($"  Remaining: {report.ChunkedResult.FinalSubtitles}");
            sb.AppendLine();
            sb.AppendLine("Final Output:");
            sb.AppendLine($"  Subtitles: {report.ChunkedResult.FinalSubtitles}");
            sb.AppendLine($"  Words: {report.ChunkedResult.FinalWords}");
            sb.AppendLine();
        }

        if (report.Comparison != null)
        {
            sb.AppendLine("COMPARISON");
            sb.AppendLine("----------");
            sb.AppendLine($"                    Legacy    Chunked    Delta");
            sb.AppendLine($"Raw Whisper:        {report.Comparison.LegacyRaw,-8} {report.Comparison.ChunkedRaw,-8} {report.Comparison.LegacyRaw - report.Comparison.ChunkedRaw,+8}");
            sb.AppendLine($"After Merge:        {report.Comparison.LegacyMerged,-8} {report.Comparison.ChunkedMerged,-8} {report.Comparison.LegacyMerged - report.Comparison.ChunkedMerged,+8}");
            sb.AppendLine($"After Dedup:        {report.Comparison.LegacyDeduped,-8} {report.Comparison.ChunkedDeduped,-8} {report.Comparison.LegacyDeduped - report.Comparison.ChunkedDeduped,+8}");
            sb.AppendLine($"Final:              {report.Comparison.LegacyFinal,-8} {report.Comparison.ChunkedFinal,-8} {report.Comparison.LegacyFinal - report.Comparison.ChunkedFinal,+8}");
            sb.AppendLine();
            sb.AppendLine("WHERE LOSS OCCURS");
            sb.AppendLine("-----------------");
            sb.AppendLine(report.Comparison.RootCauseEvidence);
        }

        File.WriteAllText(reportPath, sb.ToString());
        ChunkLog($"Report saved to: {reportPath}");
    }

    private async Task<AudioProperties> GetAudioPropertiesAsync(string audioPath, CancellationToken ct)
    {
        var props = new AudioProperties();
        var args = $"-v error -show_entries format=duration:stream=sample_rate,channels -of default=noprint_wrappers=1:nokey=1 \"{audioPath}\"";

        try
        {
            using var runner = new AsyncProcessRunner();
            await runner.RunAsync("ffprobe", args, ct, captureOutput: true);

            var output = runner.ErrorOutput;
            ChunkLog($"ffprobe output: {output}");

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("duration="))
                {
                    if (double.TryParse(line.Substring(9), out var duration))
                    {
                        props.Duration = TimeSpan.FromSeconds(duration);
                    }
                }
                else if (line.StartsWith("sample_rate="))
                {
                    if (int.TryParse(line.Substring(12), out var sampleRate))
                    {
                        props.SampleRate = sampleRate;
                    }
                }
                else if (line.StartsWith("channels="))
                {
                    if (int.TryParse(line.Substring(9), out var channels))
                    {
                        props.Channels = channels;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ChunkLog($"ffprobe error: {ex.Message}");
        }

        return props;
    }

    private async Task<List<(TimeSpan start, TimeSpan end)>> DetectSilenceRegionsAsync(CancellationToken ct)
    {
        return await Task.Run(() => new List<(TimeSpan start, TimeSpan end)>(), ct);
    }

    private static string TruncateText(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "(empty)";
        return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
    }

    private static string FormatSubtitleDebug(Subtitle subtitle)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Subtitle: {subtitle.Paragraphs.Count} paragraphs");
        sb.AppendLine();
        foreach (var p in subtitle.Paragraphs)
        {
            sb.AppendLine($"{FormatTimeCode(p.StartTime)} --> {FormatTimeCode(p.EndTime)}");
            sb.AppendLine(p.Text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string FormatTimeCode(TimeCode tc)
    {
        return $"{tc.Hours:00}:{tc.Minutes:00}:{tc.Seconds:00},{tc.Milliseconds:000}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}

public class DiagnosticReport
{
    public string VideoPath { get; set; } = string.Empty;
    public TimeSpan VideoDuration { get; set; }
    public string RunTimestamp { get; set; } = string.Empty;
    public string EngineId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string? LegacyFolder { get; set; }
    public string? ChunkedFolder { get; set; }
    public LegacyDiagnosticResult? LegacyResult { get; set; }
    public ChunkedDiagnosticResult? ChunkedResult { get; set; }
    public ComparisonReport? Comparison { get; set; }
    public string? Error { get; set; }
}

public class LegacyDiagnosticResult
{
    public string? WavPath { get; set; }
    public long WavSize { get; set; }
    public TimeSpan WavDuration { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public string? FFmpegCommandPath { get; set; }
    public string? WhisperCommandPath { get; set; }
    public string? RawSrtPath { get; set; }
    public string? ParsedPath { get; set; }
    public string? FinalPath { get; set; }
    public int RawSubtitles { get; set; }
    public int RawWords { get; set; }
    public string? FirstSubtitleText { get; set; }
    public string? LastSubtitleText { get; set; }
    public int FinalSubtitles { get; set; }
    public int FinalWords { get; set; }
    public string? Error { get; set; }
}

public class ChunkedDiagnosticResult
{
    public int ChunkCount { get; set; }
    public List<ChunkDiagnosticResult> Chunks { get; set; } = new();
    public int MergedSubtitles { get; set; }
    public int MergedWords { get; set; }
    public int RejectedByDedup { get; set; }
    public int FinalSubtitles { get; set; }
    public int FinalWords { get; set; }
    public string? FinalPath { get; set; }

    public int TotalRawSubtitles() => Chunks.Sum(c => c.RawSubtitles);
}

public class ChunkDiagnosticResult
{
    public int ChunkIndex { get; set; }
    public TimeSpan ChunkStart { get; set; }
    public TimeSpan ChunkEnd { get; set; }
    public TimeSpan ExpectedDuration { get; set; }
    public string? WavPath { get; set; }
    public long WavSize { get; set; }
    public TimeSpan WavDuration { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public string? FFmpegCommandPath { get; set; }
    public string? WhisperCommandPath { get; set; }
    public string? RawSrtPath { get; set; }
    public string? ParsedPath { get; set; }
    public int RawSubtitles { get; set; }
    public int RawWords { get; set; }
    public string? FirstSubtitleText { get; set; }
    public string? LastSubtitleText { get; set; }
    public string? Error { get; set; }
}

public class ComparisonReport
{
    public int LegacyRaw { get; set; }
    public int ChunkedRaw { get; set; }
    public int LegacyMerged { get; set; }
    public int ChunkedMerged { get; set; }
    public int LegacyDeduped { get; set; }
    public int ChunkedDeduped { get; set; }
    public int LegacyFinal { get; set; }
    public int ChunkedFinal { get; set; }
    public string FirstLossStage { get; set; } = string.Empty;
    public string RootCauseEvidence { get; set; } = string.Empty;
}

public class AudioProperties
{
    public TimeSpan Duration { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
}