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

public sealed class VoskAdapter : IAudioTranscriber
{
    private readonly VoskEngine _engine;
    private readonly string _modelName;
    private readonly string _language;
    private readonly string _extraArgs;
    private readonly ProcessOutputRouter? _router;

    private static readonly string LogFilePath = Path.Combine(
        Se.DataFolder, "Logs", "WhisperDebug", "vosk_adapter_debug.log");

    public string EngineId => _engine.Choice;
    public string ModelId => _modelName;
    public string Language => _language;

    public const string AdapterVersion = "1.2";
    public const string AdapterStatus = "STABLE";

    public VoskAdapter(
        VoskEngine engine,
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

    private void Trace(string message)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[VOSK] {message}");
            if (_router != null)
            {
                // This will send to the console log via ProcessOutputRouter
            }
        }
        catch
        {
        }
    }

    public async Task<Subtitle> TranscribeChunkAsync(string audioPath, TimeSpan offset, CancellationToken ct = default)
    {
        var result = new Subtitle();

        try
        {
            var modelPath = _engine.GetModelForCmdLine(_modelName);
            var outputSrtPath = Path.Combine(Path.GetTempPath(), $"vosk_{Guid.NewGuid():N}.srt");
            var scriptPath = GenerateAndSavePythonScript();

            var exe = _engine.GetExecutable();
            var args = $"\"{scriptPath}\" --model \"{modelPath}\" --file \"{audioPath}\" --output \"{outputSrtPath}\" --language {_language}";

            LogSection("VOSK ADAPTER START");
            Log($"Executable: {exe}");
            Log($"Arguments: {args}");
            Log($"Script path: {scriptPath}");
            Log($"Model path: {modelPath}");
            Log($"Audio path: {audioPath}");
            Log($"Output SRT: {outputSrtPath}");
            Log($"Model exists: {File.Exists(modelPath)}");
            Log($"Audio exists: {File.Exists(audioPath)}");

            using var runner = new AsyncProcessRunner(_router);
            var exitCode = await runner.RunAsync(exe, args, ct, captureOutput: true);

            Log($"Exit code: {exitCode}");

            if (ct.IsCancellationRequested)
            {
                Log("Cancelled by token");
                return result;
            }

            Log($"ErrorOutput length: {runner.ErrorOutput.Length}");
            if (runner.ErrorOutput.Length > 0)
            {
                var preview = runner.ErrorOutput.Length > 1000
                    ? runner.ErrorOutput.Substring(0, 1000) + "...[TRUNCATED]"
                    : runner.ErrorOutput;
                Log($"ErrorOutput: {preview}");
            }

            if (File.Exists(outputSrtPath))
            {
                Log($"Output SRT exists, parsing...");
                var rawLines = File.ReadAllLines(outputSrtPath, Encoding.UTF8);
                new SubRip().LoadSubtitle(result, rawLines.ToList(), outputSrtPath);
                Log($"Parsed {result.Paragraphs.Count} paragraphs");

                foreach (var p in result.Paragraphs)
                {
                    p.StartTime.TotalMilliseconds += offset.TotalMilliseconds;
                    p.EndTime.TotalMilliseconds += offset.TotalMilliseconds;
                }

                try { File.Delete(outputSrtPath); } catch { }
            }
            else
            {
                Log($"Output SRT NOT found at: {outputSrtPath}");
                Log($"Checking temp directory for vosk files...");
                var tempFiles = Directory.GetFiles(Path.GetTempPath(), "vosk_*.srt");
                foreach (var f in tempFiles)
                {
                    Log($"Found temp file: {f}");
                }

                if (!string.IsNullOrWhiteSpace(runner.ErrorOutput))
                {
                    Log("Attempting stderr parsing fallback");
                    ParseTranscriptOutput(runner.ErrorOutput, result, offset);
                    Log($"Fallback parsed {result.Paragraphs.Count} paragraphs");
                }
            }

            CleanupTempScript(scriptPath);
            Log($"Returning {result.Paragraphs.Count} paragraphs");
        }
        catch (Exception ex)
        {
            Log($"Exception: {ex.Message}");
            Log($"Stack trace: {ex.StackTrace}");
            if (!ct.IsCancellationRequested)
            {
                throw;
            }
        }

        return result;
    }

    private string GenerateAndSavePythonScript()
    {
        var scriptContent = GeneratePythonScript();
        var scriptPath = Path.Combine(Path.GetTempPath(), $"vosk_{Guid.NewGuid():N}.py");

        using (var writer = new StreamWriter(scriptPath, false, new UTF8Encoding(true)))
        {
            writer.Write(scriptContent);
        }

        return scriptPath;
    }

    private static void CleanupTempScript(string scriptPath)
    {
        try
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
        catch
        {
        }
    }

    private static string GeneratePythonScript()
    {
        return @"import sys,os,json,wave,argparse
try:
    from vosk import Model,KaldiRecognizer,SetLogLevel
except:
    print('ERROR: vosk not installed',file=sys.stderr); sys.exit(1)
SetLogLevel(-1)
p=argparse.ArgumentParser()
p.add_argument('--model',required=True); p.add_argument('--file',required=True)
p.add_argument('--output',required=True); p.add_argument('--language',default='en')
a=p.parse_args()
def mt(ms):
    h=ms//3600000; m=(ms%3600000)//60000; s=(ms%60000)//1000; ms=ms%1000
    return f'{h:02d}:{m:02d}:{s:02d},{ms:03d}'
def is_end(word,next_word):
    if not word: return False
    w=word.get('word','').strip()
    if w and w[-1] in '।?!.۔।।।।।。！？။':
        return True
    if next_word and word.get('end',0)>0:
        gap=next_word.get('start',0)-word.get('end',0)
        if gap>1.5:
            return True
    return False
def gs(r):
    l=[]; i=1
    for x in r:
        w=x.get('result',[])
        if not w:
            t=x.get('text','').strip()
            if t: l+=[str(i),'00:00:00,000 --> 00:00:05,000',t,'']; i+=1
            continue
        start_ms=int(w[0].get('start',0)*1000)
        sent_words=[]
        for j,u in enumerate(w):
            sent_words.append(u.get('word',''))
            if is_end(u,w[j+1] if j+1<len(w) else None):
                end_ms=int(u.get('end',0)*1000)
                sent_text=' '.join(sent_words).strip()
                if sent_text:
                    l+=[str(i),mt(start_ms)+' --> '+mt(end_ms),sent_text,'']; i+=1
                sent_words=[]
                start_ms=int(u.get('end',0)*1000)
        if sent_words:
            end_ms=int(w[-1].get('end',0)*1000) if w else start_ms+3000
            sent_text=' '.join(sent_words).strip()
            if sent_text:
                l+=[str(i),mt(start_ms)+' --> '+mt(end_ms),sent_text,'']; i+=1
    return '\n'.join(l)
if not os.path.exists(a.model): print(f'Model not found: {a.model}',file=sys.stderr); sys.exit(1)
if not os.path.exists(a.file): print(f'Audio not found: {a.file}',file=sys.stderr); sys.exit(1)
print(f'Loading: {a.model}',file=sys.stderr)
m=Model(a.model)
print(f'Audio: {a.file}',file=sys.stderr)
wf=wave.open(a.file,'rb'); r=KaldiRecognizer(m,wf.getframerate()); r.SetWords(True)
results=[]; chunk=4000; total=wf.getnframes(); done=0
while 1:
    d=wf.readframes(chunk)
    if not d: break
    done+=len(d)//2
    if r.AcceptWaveform(d):
        res=json.loads(r.Result())
        txt=res.get('text','').strip()
        if txt:
            results.append(res)
            print(f'TEXT: {txt}',file=sys.stderr)
    pct=min(99,int(100*done/total)) if total>0 else 0
    print(f'{pct}%',file=sys.stderr)
wf.close()
fr=json.loads(r.FinalResult())
if fr.get('text','').strip(): results.append(fr)
print('100%',file=sys.stderr)
srt=gs(results)
dn=os.path.dirname(a.output)
if dn: os.makedirs(dn,exist_ok=True)
open(a.output,'w',encoding='utf-8').write(srt)
print(f'Done: {a.output}',file=sys.stderr)
";
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

            if (trimmed.StartsWith("TEXT:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Done:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Loading:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Audio:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
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