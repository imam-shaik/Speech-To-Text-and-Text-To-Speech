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

public sealed class CrispAsrAdapter : IAudioTranscriber
{
    private readonly CrispAsrEngine _engine;
    private readonly string _modelName;
    private readonly string _language;
    private readonly string _extraArgs;
    private readonly ProcessOutputRouter? _router;

    private static readonly string LogFilePath = Path.Combine(
        Se.DataFolder, "Logs", "WhisperDebug", "crisp_adapter_debug.log");

    public string EngineId => _engine.Choice;
    public string ModelId => _modelName;
    public string Language => _language;

    public const string AdapterVersion = "1.0";
    public const string AdapterStatus = "VERIFIED";

    public CrispAsrAdapter(
        CrispAsrEngine engine,
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
            var backendName = _engine.BackendName;
            var tempSrtPath = audioPath + ".srt";

            LogSection("CRISP ADAPTER START");
            Log($"Executable: {exe}");
            Log($"Model path: {model}");
            Log($"Backend name: {backendName}");
            Log($"Audio path: {audioPath}");
            Log($"Expected SRT: {tempSrtPath}");
            Log($"Model exists: {File.Exists(model)}");
            Log($"Audio exists: {File.Exists(audioPath)}");

            var langPart = _engine.IncludeLanguage || _language != "auto"
                ? $"-l {_language} "
                : string.Empty;

            var paramsBuilder = new StringBuilder();
            paramsBuilder.Append($"--backend {backendName} {langPart}-m \"{model}\" -f \"{audioPath}\" --output-srt");

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

            if (File.Exists(tempSrtPath))
            {
                Log("SRT file exists, parsing...");
                var rawLines = File.ReadAllLines(tempSrtPath, Encoding.UTF8);
                new SubRip().LoadSubtitle(result, rawLines.ToList(), tempSrtPath);

                foreach (var p in result.Paragraphs)
                {
                    p.StartTime.TotalMilliseconds += offset.TotalMilliseconds;
                    p.EndTime.TotalMilliseconds += offset.TotalMilliseconds;
                }

                try { File.Delete(tempSrtPath); } catch { }
                Log($"Parsed {result.Paragraphs.Count} paragraphs");
            }
            else
            {
                Log($"SRT file NOT found at: {tempSrtPath}");
                if (!string.IsNullOrWhiteSpace(runner.ErrorOutput))
                {
                    Log("Attempting stderr parsing fallback");
                    ParseTranscriptOutput(runner.ErrorOutput, result, offset);
                    Log($"Fallback parsed {result.Paragraphs.Count} paragraphs");
                }
                else
                {
                    Log("No fallback content available");
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

    private void ParseTranscriptOutput(string output, Subtitle result, TimeSpan offset)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var currentText = new StringBuilder();
        var startTimeMs = -1.0;
        var lineStartMs = -1.0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            if (TryParseProgress(trimmed, out _))
            {
                continue;
            }

            var timestampMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"^\[?(\d{2}):(\d{2}):(\d{2})[.,](\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2})[.,](\d{3})\](.*)");
            if (timestampMatch.Success)
            {
                if (currentText.Length > 0 && startTimeMs >= 0)
                {
                    var endMs = lineStartMs + 3000;
                    var p = new Paragraph
                    {
                        StartTime = new TimeCode(startTimeMs + offset.TotalMilliseconds),
                        EndTime = new TimeCode(endMs + offset.TotalMilliseconds),
                        Text = currentText.ToString().Trim()
                    };
                    result.Paragraphs.Add(p);
                    currentText.Clear();
                }

                var startMs = int.Parse(timestampMatch.Groups[1].Value) * 3600000 +
                              int.Parse(timestampMatch.Groups[2].Value) * 60000 +
                              int.Parse(timestampMatch.Groups[3].Value) * 1000 +
                              int.Parse(timestampMatch.Groups[4].Value);
                startTimeMs = startMs;
                lineStartMs = startMs;
                currentText.Append(timestampMatch.Groups[9].Value.Trim());
            }
            else if (startTimeMs >= 0 && !trimmed.Contains("%"))
            {
                if (currentText.Length > 0)
                {
                    currentText.Append(" ");
                }
                currentText.Append(trimmed);
            }
        }

        if (currentText.Length > 0 && startTimeMs >= 0)
        {
            var endMs = lineStartMs + 3000;
            var p = new Paragraph
            {
                StartTime = new TimeCode(startTimeMs + offset.TotalMilliseconds),
                EndTime = new TimeCode(endMs + offset.TotalMilliseconds),
                Text = currentText.ToString().Trim()
            };
            result.Paragraphs.Add(p);
        }
    }

    private static bool TryParseProgress(string line, out int? percent)
    {
        percent = null;
        var trimmed = line.Trim();

        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d+%$"))
        {
            if (int.TryParse(System.Text.RegularExpressions.Regex.Match(trimmed, @"(\d+)").Value, out var p))
            {
                percent = p;
                return true;
            }
        }

        return false;
    }
}