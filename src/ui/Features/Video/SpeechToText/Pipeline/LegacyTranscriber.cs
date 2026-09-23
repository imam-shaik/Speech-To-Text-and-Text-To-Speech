using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.Engines;
using Nikse.SubtitleEdit.Logic.Config;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class LegacyTranscriber
{
    private readonly string _videoPath;
    private readonly string _outputSrtPath;
    private readonly ISpeechToTextEngine _engine;
    private readonly string _modelName;
    private readonly string _language;
    private readonly int _audioTrackIndex;
    private readonly string _tempPath;

    private static readonly string DebugFolder = Path.Combine(
        Se.DataFolder, "Logs", "WhisperDebug");

    public LegacyTranscriber(
        string videoPath,
        string outputSrtPath,
        ISpeechToTextEngine engine,
        string modelName,
        string language,
        int audioTrackIndex = 0,
        string? tempPath = null)
    {
        _videoPath = videoPath;
        _outputSrtPath = outputSrtPath;
        _engine = engine;
        _modelName = modelName;
        _language = language;
        _audioTrackIndex = audioTrackIndex;
        _tempPath = tempPath ?? Path.GetTempPath();

        try { Directory.CreateDirectory(DebugFolder); } catch { }
    }

    private static void Log(string message)
    {
        try
        {
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [LEGACY] {message}";
            System.Diagnostics.Debug.WriteLine(logEntry);
            File.AppendAllText(Path.Combine(DebugFolder, "legacy_transcriber.log"), logEntry + Environment.NewLine);
        }
        catch
        {
        }
    }

    private static void LogSection(string title)
    {
        Log("");
        Log("========================================");
        Log(title);
        Log("========================================");
    }

    public async Task<Subtitle?> TranscribeAsync(CancellationToken ct = default)
    {
        var tempWav = Path.Combine(_tempPath, $"legacy_{Guid.NewGuid():N}.wav");

        LogSection("LEGACY TRANSCRIBER - START");
        Log($"Video: {_videoPath}");
        Log($"Output: {_outputSrtPath}");

        try
        {
            await ExtractAudioAsync(tempWav, ct);
            await TranscribeWithWhisperAsync(tempWav, ct);
            return LoadResult();
        }
        finally
        {
            try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
        }
    }

    private async Task ExtractAudioAsync(string outputPath, CancellationToken ct)
    {
        Log($"EXTRACT AUDIO: {outputPath}");

        var audioTrackArg = _audioTrackIndex > 0 ? $"-map 0:a:{_audioTrackIndex}" : "-vn";
        var args = $"-y -i \"{_videoPath}\" {audioTrackArg} -ar 16000 -ac 1 -ab 32k -af volume=1.75 -hide_banner -loglevel error -f wav \"{outputPath}\"";

        Log($"FFmpeg args: {args}");

        using var runner = new AsyncProcessRunner();
        var exitCode = await runner.RunAsync("ffmpeg", args, ct, captureOutput: true);

        Log($"FFmpeg exit: {exitCode}");

        if (File.Exists(outputPath))
        {
            var fi = new FileInfo(outputPath);
            Log($"WAV created: {fi.Length:N0} bytes");
        }

        if (exitCode != 0 && !ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"FFmpeg failed with exit code {exitCode}: {runner.ErrorOutput}");
        }
    }

    private async Task TranscribeWithWhisperAsync(string audioPath, CancellationToken ct)
    {
        var exe = _engine.GetExecutable();
        var model = _engine.GetModelForCmdLine(_modelName);

        var paramsBuilder = new StringBuilder();
        paramsBuilder.Append($"--language {_language} --model \"{model}\"");

        if (_engine is WhisperEngineCTranslate2)
        {
            paramsBuilder.Replace("--model", "--model_directory");
        }

        var extraArgs = _engine.CommandLineParameter;
        if (!string.IsNullOrWhiteSpace(extraArgs))
        {
            paramsBuilder.Append($" {extraArgs}");
        }

        paramsBuilder.Append(" --output-srt ");
        paramsBuilder.Append($" \"{audioPath}\"");

        var fullCommand = paramsBuilder.ToString();
        Log($"WHISPER EXE: {exe}");
        Log($"WHISPER CMD: {fullCommand}");

        using var runner = new AsyncProcessRunner();
        var exitCode = await runner.RunAsync(exe, fullCommand, ct, captureOutput: true);

        Log($"Whisper exit: {exitCode}");
        if (!string.IsNullOrWhiteSpace(runner.ErrorOutput))
        {
            var errPreview = runner.ErrorOutput.Length > 500
                ? runner.ErrorOutput.Substring(0, 500) + "...[TRUNCATED]"
                : runner.ErrorOutput;
            Log($"Whisper stderr: {errPreview}");
        }

        if (ct.IsCancellationRequested)
        {
            return;
        }

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Whisper failed with exit code {exitCode}: {runner.ErrorOutput}");
        }

        var tempSrt = audioPath + ".srt";
        Log($"SRT exists: {File.Exists(tempSrt)}");
        if (File.Exists(tempSrt))
        {
            var rawLines = File.ReadAllLines(tempSrt);
            Log($"Raw SRT lines: {rawLines.Length}");
            File.Copy(tempSrt, Path.Combine(DebugFolder, "legacy_raw.srt"), true);
        }

        if (File.Exists(tempSrt) && !File.Exists(_outputSrtPath))
        {
            File.Move(tempSrt, _outputSrtPath);
        }
        else if (File.Exists(tempSrt))
        {
            File.Delete(tempSrt);
        }
    }

    private Subtitle? LoadResult()
    {
        if (!File.Exists(_outputSrtPath))
        {
            Log("No output SRT found");
            return null;
        }

        try
        {
            var result = new Subtitle();
            var lines = File.ReadAllLines(_outputSrtPath, Encoding.UTF8);
            new SubRip().LoadSubtitle(result, lines.ToList(), _outputSrtPath);
            Log($"Loaded result: {result.Paragraphs.Count} paragraphs");
            return result;
        }
        catch
        {
            return null;
        }
    }
}