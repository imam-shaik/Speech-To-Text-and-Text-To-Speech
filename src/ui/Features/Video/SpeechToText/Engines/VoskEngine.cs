using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Core.AudioToText;
using Nikse.SubtitleEdit.Logic.Config;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Engines;

public class VoskEngine : ISpeechToTextEngine
{
    public static string StaticName => "Vosk";
    public string Name => StaticName;
    public string Choice => WhisperChoice.Vosk;
    public string Url => "https://alphacephei.com/vosk";

    public string Extension => string.Empty;
    public string UnpackSkipFolder => string.Empty;

    public string CommandLineParameter
    {
        get => Se.Settings.Tools.AudioToText.CommandLineParameterVosk;
        set => Se.Settings.Tools.AudioToText.CommandLineParameterVosk = value;
    }

    public List<WhisperLanguage> Languages
    {
        get
        {
            var languages = new List<WhisperLanguage>();
            var seenCodes = new HashSet<string>();

            foreach (var model in VoskModel.Models)
            {
                var code = model.TwoLetterLanguageCode;
                var name = GetLanguageName(code);

                if (seenCodes.Add(code))
                {
                    languages.Add(new WhisperLanguage(code, name));
                }
            }

            return languages.OrderBy(p => p.Name).ToList();
        }
    }

    private static string GetLanguageName(string code)
    {
        return code switch
        {
            "en" => "english",
            "cn" => "chinese",
            "de" => "german",
            "es" => "spanish",
            "ru" => "russian",
            "ko" => "korean",
            "fr" => "french",
            "ja" => "japanese",
            "pt" => "portuguese",
            "tr" => "turkish",
            "pl" => "polish",
            "ca" => "catalan",
            "nl" => "dutch",
            "ar" => "arabic",
            "sv" => "swedish",
            "it" => "italian",
            "hi" => "hindi",
            "uk" => "ukrainian",
            "fa" => "persian",
            "el" => "greek",
            "cs" => "czech",
            "br" => "breton",
            "uz" => "uzbek",
            "ph" => "filipino",
            "kz" => "kazakh",
            "te" => "telugu",
            _ => code
        };
    }

    public List<WhisperModel> Models
    {
        get
        {
            var models = new List<WhisperModel>();

            foreach (var voskModel in VoskModel.Models)
            {
                var folderName = GetFolderName(voskModel);
                var alreadyDownloaded = Directory.Exists(Path.Combine(VoskModel.ModelFolder, folderName));

                models.Add(new WhisperModel
                {
                    Name = folderName,
                    Size = voskModel.LanguageName,
                    Urls = new[] { voskModel.Url },
                    Folder = folderName,
                    AlreadyDownloaded = alreadyDownloaded,
                    Dynamic = false
                });
            }

            return models;
        }
    }

    public static string GetFolderName(VoskModel voskModel)
    {
        var url = voskModel.Url;
        var startIndex = url.LastIndexOf("vosk-model", StringComparison.Ordinal);
        var endIndex = url.LastIndexOf(".zip", StringComparison.Ordinal);
        if (endIndex < 0)
        {
            endIndex = url.Length;
        }

        return url.Substring(startIndex, endIndex - startIndex);
    }

    public bool IsEngineInstalled()
    {
        return true;
    }

    public bool CanBeDownloaded()
    {
        return true;
    }

    public string GetAndCreateWhisperFolder()
    {
        var folder = VoskModel.ModelFolder;
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }
        return folder;
    }

    public string GetAndCreateWhisperModelFolder(WhisperModel? whisperModel)
    {
        var folder = VoskModel.ModelFolder;
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }
        return folder;
    }

    public string GetExecutable()
    {
        return GetExecutableFileName();
    }

    public string GetExecutableFileName()
    {
        return "python";
    }

    public bool IsModelInstalled(WhisperModel model)
    {
        var modelPath = Path.Combine(VoskModel.ModelFolder, model.Folder);
        return Directory.Exists(modelPath) && Directory.GetFiles(modelPath, "*", SearchOption.AllDirectories).Any();
    }

    public string GetModelForCmdLine(string modelName)
    {
        var modelPath = Path.Combine(VoskModel.ModelFolder, modelName);
        return modelPath;
    }

    public async Task<string> GetHelpText()
    {
        return @"Vosk Speech Recognition Engine

Vosk is an offline open-source speech recognition engine.

Requirements:
1. Python 3.8+ installed and in PATH
2. Install vosk package: pip install vosk
3. Download a Vosk model from the available models list

Usage:
- Select a language and model from the dropdown
- The engine will use Python with the vosk package to transcribe
- Models should be extracted to the Vosk models folder

Note: Python must be installed and the vosk pip package must be available for transcription to work.";
    }

    public string GetWhisperModelDownloadFileName(WhisperModel whisperModel, string url)
    {
        var folder = GetAndCreateWhisperModelFolder(whisperModel);
        var fileName = Path.Combine(folder, Path.GetFileName(url));
        return fileName;
    }

    public override string ToString()
    {
        return Name;
    }

public static ProcessStartInfo GetPythonTranscribeProcessStartInfo(string modelPath, string waveFileName, string outputSrtPath, string languageCode)
    {
        var pythonCode = GeneratePythonScript();
        var scriptPath = Path.Combine(Path.GetTempPath(), $"vosk_{Guid.NewGuid():N}.py");

        using (var writer = new StreamWriter(scriptPath, false, new System.Text.UTF8Encoding(true)))
        {
            writer.Write(pythonCode);
        }

        var args = $"\"{scriptPath}\" --model \"{modelPath}\" --file \"{waveFileName}\" --output \"{outputSrtPath}\" --language {languageCode}";

        return new ProcessStartInfo
        {
            FileName = "python",
            Arguments = args,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
    }

    public SpeechToTextEngineCapabilities GetCapabilities() => new()
    {
        SupportsTranslateDuringTranscription = false,
        SupportsAutoTranslate = true,
        SupportsBilingualOutput = true,
        SupportsResumeFromCheckpoint = true,
        SupportsChunkedTranscription = true,
        SupportsLegacyTranscription = true,
        SupportsAutomaticMode = true,
        SupportsBackendSelection = false,
        SupportsForcedAlignerSelection = false,
        SupportsSceneAwareSplitting = true,
        SupportsCustomCommandLine = true,
        IsOffline = true,
        IsFastStartup = true,
        Description = "Vosk - Offline, fast startup, low memory"
    };

    private static string GeneratePythonScript()
    {
        return @"import sys,os,json,wave,argparse
sys.stderr.reconfigure(encoding='utf-8')
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
if not os.path.exists(a.model): print(f'Model not found: {a.model}',file=sys.stderr,flush=True); sys.exit(1)
if not os.path.exists(a.file): print(f'Audio not found: {a.file}',file=sys.stderr,flush=True); sys.exit(1)
print(f'Loading: {a.model}',file=sys.stderr,flush=True)
m=Model(a.model)
print(f'Audio: {a.file}',file=sys.stderr,flush=True)
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
            sys.stderr.write(f'TEXT: {txt}\n')
            sys.stderr.flush()
    pct=min(99,int(100*done/total)) if total>0 else 0
    print(f'{pct}%',file=sys.stderr,flush=True)
wf.close()
fr=json.loads(r.FinalResult())
if fr.get('text','').strip(): results.append(fr)
print('100%',file=sys.stderr,flush=True)
srt=gs(results)
dn=os.path.dirname(a.output)
if dn: os.makedirs(dn,exist_ok=True)
open(a.output,'w',encoding='utf-8').write(srt)
print(f'Done: {a.output}',file=sys.stderr,flush=True)
";
    }
}