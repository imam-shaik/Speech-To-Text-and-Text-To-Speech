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

public sealed class ParakeetAdapter : IAudioTranscriber
{
    private readonly ParakeetCppEngine _engine;
    private readonly string _modelName;
    private readonly string _language;
    private readonly string _extraArgs;
    private readonly ProcessOutputRouter? _router;

    private static readonly string LogFilePath = Path.Combine(
        Se.DataFolder, "Logs", "WhisperDebug", "parakeet_adapter_debug.log");

    public string EngineId => _engine.Choice;
    public string ModelId => _modelName;
    public string Language => _language;

    public const string AdapterVersion = "PROVISIONAL-1.0";
    public const string AdapterStatus = "NEEDS_VERIFICATION";

    public ParakeetAdapter(
        ParakeetCppEngine engine,
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
            var modelPath = _engine.GetModelForCmdLine(_modelName);
            var vocabPath = _engine.GetVocabForCmdLine(_modelName);

            LogSection("PARAKEET ADAPTER START");
            Log($"Executable: {exe}");
            Log($"Model path: {modelPath}");
            Log($"Vocab path: {vocabPath}");
            Log($"Audio path: {audioPath}");
            Log($"Model exists: {File.Exists(modelPath)}");
            Log($"Vocab exists: {File.Exists(vocabPath)}");
            Log($"Audio exists: {File.Exists(audioPath)}");

            var paramsBuilder = new StringBuilder();
            paramsBuilder.Append($"\"{modelPath}\" \"{audioPath}\" --vocab \"{vocabPath}\"");

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

            var possibleOutputPaths = new[]
            {
                audioPath + ".srt",
                Path.ChangeExtension(audioPath, ".srt"),
                audioPath + ".txt",
                Path.Combine(Path.GetDirectoryName(audioPath) ?? string.Empty, "output.srt"),
                Path.Combine(Path.GetDirectoryName(audioPath) ?? string.Empty, Path.GetFileNameWithoutExtension(audioPath) + ".srt"),
            };

            foreach (var p2 in possibleOutputPaths)
            {
                Log($"Checking output path: {p2} exists={File.Exists(p2)}");
            }

            Subtitle? parsedSubtitle = null;
            string? usedOutputPath = null;

            foreach (var possiblePath in possibleOutputPaths)
            {
                if (File.Exists(possiblePath))
                {
                    var rawLines = File.ReadAllLines(possiblePath, Encoding.UTF8);
                    if (IsValidSubtitleContent(rawLines))
                    {
                        parsedSubtitle = new Subtitle();
                        new SubRip().LoadSubtitle(parsedSubtitle, rawLines.ToList(), possiblePath);
                        usedOutputPath = possiblePath;
                        Log($"Valid subtitle found at: {possiblePath}");
                        break;
                    }
                    else
                    {
                        Log($"Invalid subtitle content at: {possiblePath}");
                    }
                }
            }

            if (parsedSubtitle != null)
            {
                foreach (var p in parsedSubtitle.Paragraphs)
                {
                    p.StartTime.TotalMilliseconds += offset.TotalMilliseconds;
                    p.EndTime.TotalMilliseconds += offset.TotalMilliseconds;
                }
                result = parsedSubtitle;
                Log($"Parsed {result.Paragraphs.Count} paragraphs");
            }
            else
            {
                var outputFromStderr = runner.ErrorOutput;
                if (!string.IsNullOrWhiteSpace(outputFromStderr))
                {
                    Log("Attempting stderr parsing fallback");
                    ParseTranscriptOutput(outputFromStderr, result, offset);
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

    private static bool IsValidSubtitleContent(string[] lines)
    {
        if (lines.Length < 3)
        {
            return false;
        }

        var hasNumber = lines.Any(l => int.TryParse(l.Trim(), out _));
        var hasTimestamp = lines.Any(l => l.Contains("-->"));
        var hasText = lines.Any(l => !string.IsNullOrWhiteSpace(l.Trim()) && !int.TryParse(l.Trim(), out _) && !l.Contains("-->"));

        return hasNumber && hasTimestamp && hasText;
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