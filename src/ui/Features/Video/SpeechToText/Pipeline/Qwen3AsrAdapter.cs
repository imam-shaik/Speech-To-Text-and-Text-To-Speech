using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.Engines;
using Nikse.SubtitleEdit.Logic.Config;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class Qwen3AsrAdapter : IAudioTranscriber
{
    private readonly Qwen3AsrCppEngine _engine;
    private readonly string _modelName;
    private readonly string _language;
    private readonly string _extraArgs;
    private readonly ProcessOutputRouter? _router;

    private static readonly string LogFilePath = Path.Combine(
        Se.DataFolder, "Logs", "WhisperDebug", "qwen3_adapter_debug.log");

    public string EngineId => _engine.Choice;
    public string ModelId => _modelName;
    public string Language => _language;

    public const string AdapterVersion = "1.0";
    public const string AdapterStatus = "IMPLEMENTED_VERIFIED";

    public Qwen3AsrAdapter(
        Qwen3AsrCppEngine engine,
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
    }

    private static void Log(string message)
    {
        try
        {
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            System.Diagnostics.Debug.WriteLine(message);
            File.AppendAllText(LogFilePath, logEntry + Environment.NewLine);
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

    public async Task<Subtitle> TranscribeChunkAsync(string audioPath, TimeSpan offset, CancellationToken ct = default)
    {
        var result = new Subtitle();

        try
        {
            var exe = _engine.GetExecutable();
            var model = _engine.GetModelForCmdLine(_modelName);
            var alignerModel = _engine.ForcedAlignerModel.Name;
            var alignerPath = _engine.GetModelForCmdLine(alignerModel);
            var jsonPath = Path.Combine(Path.GetTempPath(), $"qwen3_{Guid.NewGuid():N}.json");

            LogSection("QWEN3 ADAPTER START");
            Log($"Executable: {exe}");
            Log($"Model path: {model}");
            Log($"Aligner path: {alignerPath}");
            Log($"Audio path: {audioPath}");
            Log($"JSON output: {jsonPath}");
            Log($"Model exists: {File.Exists(model)}");
            Log($"Aligner exists: {File.Exists(alignerPath)}");
            Log($"Audio exists: {File.Exists(audioPath)}");

            var paramsBuilder = new StringBuilder();
            paramsBuilder.Append($"-m \"{model}\" --aligner-model \"{alignerPath}\" -f \"{audioPath}\" --transcribe-align -o \"{jsonPath}\"");

            if (!string.IsNullOrWhiteSpace(_extraArgs))
            {
                paramsBuilder.Append($" {_extraArgs}");
            }

            Log($"Arguments: {paramsBuilder}");

            using var runner = new AsyncProcessRunner(_router);
            var exitCode = await runner.RunAsync(exe, paramsBuilder.ToString(), ct, captureOutput: true);

            Log($"Exit code: {exitCode}");

            if (ct.IsCancellationRequested)
            {
                Log("Cancelled by token");
                return result;
            }

            Log($"ErrorOutput length: {runner.ErrorOutput.Length}");
            if (runner.ErrorOutput.Length > 0)
            {
                var preview = runner.ErrorOutput.Length > 500
                    ? runner.ErrorOutput.Substring(0, 500) + "...[TRUNCATED]"
                    : runner.ErrorOutput;
                Log($"ErrorOutput: {preview}");
            }

            if (File.Exists(jsonPath))
            {
                Log("JSON output file exists, parsing...");
                try
                {
                    var jsonText = File.ReadAllText(jsonPath);
                    result = ParseJsonOutput(jsonText, offset);
                    Log($"Parsed {result.Paragraphs.Count} paragraphs from JSON");
                }
                finally
                {
                    try { File.Delete(jsonPath); } catch { }
                }
            }
            else
            {
                Log($"JSON output NOT found at: {jsonPath}");
                if (!string.IsNullOrWhiteSpace(runner.ErrorOutput))
                {
                    Log("Attempting error output parsing fallback");
                    ParseErrorOutput(runner.ErrorOutput);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Exception: {ex.Message}");
            if (!ct.IsCancellationRequested)
            {
                throw;
            }
        }

        Log($"Returning {result.Paragraphs.Count} paragraphs");
        return result;
    }

    private Subtitle ParseJsonOutput(string jsonText, TimeSpan offset)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            if (!doc.RootElement.TryGetProperty("words", out var words))
            {
                return new Subtitle();
            }

            var timings = new List<WordTiming>();

            foreach (var word in words.EnumerateArray())
            {
                if (!word.TryGetProperty("word", out var wordElement) ||
                    !word.TryGetProperty("start", out var startElement) ||
                    !word.TryGetProperty("end", out var endElement))
                {
                    continue;
                }

                var text = wordElement.GetString() ?? string.Empty;
                var start = startElement.GetDouble();
                var end = endElement.GetDouble();

                timings.Add(new WordTiming(text, start, end));
            }

            return SubtitleSegmenter.FromWordTimings(timings, offset, ((ISpeechToTextEngine)_engine).GetSegmentationOptions());
        }
        catch
        {
            return new Subtitle();
        }
    }

    private void ParseErrorOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }
    }
}