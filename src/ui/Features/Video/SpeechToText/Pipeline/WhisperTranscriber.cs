using System;
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

public sealed class WhisperTranscriber : IAudioTranscriber
{
    private readonly ISpeechToTextEngine _engine;
    private readonly string _modelName;
    private readonly string _language;
    private readonly string _extraArgs;
    private readonly ProcessOutputRouter? _router;

    private static readonly string DebugFolder = Path.Combine(
        Se.DataFolder, "Logs", "WhisperDebug");

    public string EngineId => _engine.Choice;
    public string ModelId => _modelName;
    public string Language => _language;

    public const string AdapterVersion = "1.0";
    public const string AdapterStatus = "STABLE";

    public WhisperTranscriber(
        ISpeechToTextEngine engine,
        string modelName,
        string language,
        string extraArgs = "",
        ProcessOutputRouter? router = null)
    {
        _engine = engine;
        _modelName = modelName;
        _language = language;
        _extraArgs = extraArgs;
        _router = router;

        try { Directory.CreateDirectory(DebugFolder); } catch { }
    }

    private static void Log(string message)
    {
        try
        {
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            System.Diagnostics.Debug.WriteLine(logEntry);
            File.AppendAllText(Path.Combine(DebugFolder, "whisper_transcriber.log"), logEntry + Environment.NewLine);
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

    public static void ClearDebugFolder()
    {
        try
        {
            if (Directory.Exists(DebugFolder))
            {
                foreach (var f in Directory.GetFiles(DebugFolder))
                {
                    try { File.Delete(f); } catch { }
                }
            }
        }
        catch
        {
        }
    }

    public async Task<Subtitle> TranscribeChunkAsync(string audioPath, TimeSpan offset, CancellationToken ct = default)
    {
        var result = new Subtitle();
        var tempSrtPath = audioPath + ".srt";
        var chunkIndex = Guid.NewGuid().ToString("N")[..8];
        var rawBackupPath = Path.Combine(DebugFolder, $"chunk_{chunkIndex}_offset_{offset.TotalSeconds:0.0}_raw.srt");

        try
        {
            var exe = _engine.GetExecutable();
            var model = _engine.GetModelForCmdLine(_modelName);

            LogSection($"WHISPER TRANSCRIBER - CHUNK {chunkIndex}");
            Log($"Audio Path: {audioPath}");
            Log($"Offset: {offset}");
            Log($"Audio exists: {File.Exists(audioPath)}");
            if (File.Exists(audioPath))
            {
                var fi = new FileInfo(audioPath);
                Log($"Audio size: {fi.Length:N0} bytes");
            }

            var paramsBuilder = new StringBuilder();
            paramsBuilder.Append($"--language {_language} --model \"{model}\" --output-srt ");

            if (_engine is WhisperEngineCTranslate2)
            {
                paramsBuilder.Replace("--model", "--model_directory");
            }

            paramsBuilder.Append(_extraArgs);
            paramsBuilder.Append($" \"{audioPath}\"");

            var fullCommand = paramsBuilder.ToString();
            Log($"Executable: {exe}");
            Log($"Command: {fullCommand}");

            using var runner = new AsyncProcessRunner(_router);
            var exitCode = await runner.RunAsync(exe, fullCommand, ct, captureOutput: true);

            Log($"Exit code: {exitCode}");
            if (!string.IsNullOrWhiteSpace(runner.ErrorOutput))
            {
                var errPreview = runner.ErrorOutput.Length > 500
                    ? runner.ErrorOutput.Substring(0, 500) + "...[TRUNCATED]"
                    : runner.ErrorOutput;
                Log($"Stderr: {errPreview}");
            }

            if (ct.IsCancellationRequested)
            {
                Log("Cancelled by token");
                return result;
            }

            if (File.Exists(tempSrtPath))
            {
                Log($"SRT exists at: {tempSrtPath}");

                // Save raw output before parsing
                File.Copy(tempSrtPath, rawBackupPath, true);
                Log($"Raw SRT saved to: {rawBackupPath}");

                var rawLines = File.ReadAllLines(tempSrtPath, Encoding.UTF8);
                Log($"Raw SRT lines: {rawLines.Length}");

                new SubRip().LoadSubtitle(result, rawLines.ToList(), tempSrtPath);
                Log($"Parsed paragraphs: {result.Paragraphs.Count}");

                foreach (var p in result.Paragraphs)
                {
                    p.StartTime.TotalMilliseconds += offset.TotalMilliseconds;
                    p.EndTime.TotalMilliseconds += offset.TotalMilliseconds;
                }

                Log($"After offset adjustment: {result.Paragraphs.Count} paragraphs");
            }
            else
            {
                Log($"SRT NOT found at: {tempSrtPath}");
            }
        }
        finally
        {
            if (File.Exists(tempSrtPath))
            {
                try { File.Delete(tempSrtPath); } catch { }
            }
        }

        Log($"Returning {result.Paragraphs.Count} paragraphs");
        return result;
    }
}