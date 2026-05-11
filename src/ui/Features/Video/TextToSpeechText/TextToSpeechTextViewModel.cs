using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Shared;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.DownloadTts;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Engines;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.ManageTtsModels;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Voices;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Download;
using Nikse.SubtitleEdit.Logic.Media;
using Nikse.SubtitleEdit.Logic.VideoPlayers.LibMpvDynamic;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeechText;

public partial class TextToSpeechTextViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<ITtsEngine> _engines;
    [ObservableProperty] private ITtsEngine? _selectedEngine;
    [ObservableProperty] private ObservableCollection<Voice> _voices;
    [ObservableProperty] private Voice? _selectedVoice;
    [ObservableProperty] private ObservableCollection<TtsLanguage> _languages;
    [ObservableProperty] private TtsLanguage? _selectedLanguage;
    [ObservableProperty] private ObservableCollection<string> _regions;
    [ObservableProperty] private string? _selectedRegion;
    [ObservableProperty] private ObservableCollection<string> _models;
    [ObservableProperty] private string? _selectedModel;
    [ObservableProperty] private bool _hasLanguageParameter;
    [ObservableProperty] private bool _hasApiKey;
    [ObservableProperty] private string _apiKey;
    [ObservableProperty] private bool _hasRegion;
    [ObservableProperty] private string _region;
    [ObservableProperty] private bool _hasModel;
    [ObservableProperty] private bool _hasKeyFile;
    [ObservableProperty] private string _keyFile;
    [ObservableProperty] private string _inputText;
    [ObservableProperty] private ObservableCollection<TextToSpeechTextBatchItem> _batchItems;
    [ObservableProperty] private bool _isBatchMode;
    [ObservableProperty] private bool _isSingleMode = true;
    [ObservableProperty] private string _selectedAudioFormat;
    [ObservableProperty] private ObservableCollection<string> _audioFormats;
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private bool _isNotGenerating = true;
    [ObservableProperty] private string _progressText;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private double _individualProgressValue;
    [ObservableProperty] private string _individualProgressText;
    [ObservableProperty] private double _progressOpacity;
    [ObservableProperty] private string _outputFolder;
    [ObservableProperty] private bool _doSaveTxt = false;
    [ObservableProperty] private string _fileName = "output";
    [ObservableProperty] private bool _isVoiceTestEnabled = true;

    public Window? Window { get; set; }
    public bool OkPressed { get; private set; }

    private CancellationTokenSource _cancellationTokenSource;
    private CancellationToken _cancellationToken;
    private readonly IFileHelper _fileHelper;
    private readonly IFolderHelper _folderHelper;
    private readonly IWindowService _windowService;
    private readonly string _tempFolder;
    private readonly Timer _timer;
    private readonly Timer _playTimer;
    private LibMpvDynamicPlayer? _mpvContext;
    private readonly object _playLock;
    private bool _isCleaningUp;

    public TextToSpeechTextViewModel(ITtsDownloadService ttsDownloadService, IWindowService windowService, IFileHelper fileHelper, IFolderHelper folderHelper)
    {
        _windowService = windowService;
        _fileHelper = fileHelper;
        _folderHelper = folderHelper;

        Engines = new ObservableCollection<ITtsEngine>();
        Voices = new ObservableCollection<Voice>();
        Regions = new ObservableCollection<string>();
        Models = new ObservableCollection<string>();
        Languages = new ObservableCollection<TtsLanguage>();
        BatchItems = new ObservableCollection<TextToSpeechTextBatchItem>();
        AudioFormats = new ObservableCollection<string> { "WAV", "MP3" };

        InputText = string.Empty;
        ApiKey = string.Empty;
        Region = string.Empty;
        KeyFile = string.Empty;
        OutputFolder = string.Empty;
        ProgressText = string.Empty;
        ProgressValue = 0;
        IndividualProgressText = string.Empty;
        IndividualProgressValue = 0;
        SelectedAudioFormat = "WAV";
        IsBatchMode = false;
        FileName = "output";

        _cancellationTokenSource = new CancellationTokenSource();
        _tempFolder = Path.Combine(Path.GetTempPath(), "SubtitleEditTTS", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempFolder);

        _timer = new Timer(100);
        _playTimer = new Timer(500);
        _playTimer.Elapsed += OnPlayTimerElapsed;
        _playLock = new object();

        Engines =
        [
            new EdgeTts(),
            new AllTalk(ttsDownloadService),
            new ElevenLabs(ttsDownloadService),
            new AzureSpeech(ttsDownloadService),
            new MistralSpeech(ttsDownloadService),
            new Murf(ttsDownloadService),
            new GoogleSpeech(ttsDownloadService),
            new Qwen3TtsCpp(),
            new KokoroTtsCpp(),
            new OmniVoiceTtsCpp(),
        ];

        if (!OperatingSystem.IsMacOS())
        {
            Engines.Insert(0, new Piper(ttsDownloadService));
        }

        LoadSettings();
    }

    private void OnPlayTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        lock (_playLock)
        {
            if (_mpvContext == null)
            {
                IsVoiceTestEnabled = true;
            }
            else
            {
                IsVoiceTestEnabled = _mpvContext.IsPaused;
            }
        }
    }

    private void LoadSettings()
    {
        var lastEngine = Engines.FirstOrDefault(e => e.Name == Se.Settings.Video.TextToSpeech.Engine);
        if (lastEngine != null)
        {
            SelectedEngine = lastEngine;
        }

        if (SelectedEngine != null)
        {
            HasLanguageParameter = SelectedEngine.HasLanguageParameter;
            HasApiKey = SelectedEngine.HasApiKey;
            HasRegion = SelectedEngine.HasRegion;
            HasModel = SelectedEngine.HasModel;
            HasKeyFile = SelectedEngine.HasKeyFile;
        }

        var lastVoice = Voices.FirstOrDefault(v => v.Name == Se.Settings.Video.TextToSpeech.Voice);
        if (lastVoice == null)
        {
            lastVoice = Voices.FirstOrDefault(p => p.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase) ||
                                                        p.Name.Contains("English", StringComparison.OrdinalIgnoreCase));
            SelectedVoice = lastVoice ?? Voices.FirstOrDefault();
        }
        else
        {
            SelectedVoice = lastVoice;
        }

        if (SelectedEngine is AzureSpeech)
        {
            ApiKey = Se.Settings.Video.TextToSpeech.AzureApiKey;
            SelectedRegion = Se.Settings.Video.TextToSpeech.AzureRegion;
        }
        else if (SelectedEngine is ElevenLabs)
        {
            ApiKey = Se.Settings.Video.TextToSpeech.ElevenLabsApiKey;
        }
        else if (SelectedEngine is MistralSpeech)
        {
            ApiKey = Se.Settings.Video.TextToSpeech.MistralApiKey;
        }
        else if (SelectedEngine is Murf)
        {
            ApiKey = Se.Settings.Video.TextToSpeech.MurfApiKey;
        }
        else if (SelectedEngine is GoogleSpeech)
        {
            ApiKey = Se.Settings.Video.TextToSpeech.GoogleApiKey;
            KeyFile = Se.Settings.Video.TextToSpeech.GoogleKeyFile;
        }

        OutputFolder = Se.Settings.Tools.TextToSpeechOutputFolder ?? string.Empty;
    }

    private void SaveSettings()
    {
        Se.Settings.Video.TextToSpeech.Engine = SelectedEngine?.Name ?? string.Empty;
        Se.Settings.Video.TextToSpeech.Voice = SelectedVoice?.Name ?? string.Empty;
        Se.Settings.Tools.TextToSpeechOutputFolder = OutputFolder;
        Se.SaveSettings();
    }

    [RelayCommand]
    public async Task ImportTextFile()
    {
        if (Window == null) return;

        var extensions = new List<string> { "*.txt", "*.doc", "*.docx", "*.pdf" };
        var fileNames = await _fileHelper.PickOpenFiles(Window, "Open text file", "Text files", extensions, string.Empty, new List<string>());
        if (fileNames == null || fileNames.Length == 0) return;

        if (IsBatchMode)
        {
            foreach (var fileName in fileNames)
            {
                try
                {
                    var text = await File.ReadAllTextAsync(fileName, _cancellationToken);
                    BatchItems.Add(new TextToSpeechTextBatchItem(Path.GetFileNameWithoutExtension(fileName), text));
                }
                catch (Exception ex)
                {
                    BatchItems.Add(new TextToSpeechTextBatchItem(Path.GetFileNameWithoutExtension(fileName), string.Empty) { HasError = true, ErrorMessage = ex.Message });
                }
            }
        }
        else
        {
            var fileName = fileNames[0];
            try
            {
                var text = await File.ReadAllTextAsync(fileName, _cancellationToken);
                InputText = text;
                FileName = Path.GetFileNameWithoutExtension(fileName);
            }
            catch (Exception ex)
            {
                await MessageBox.Show(Window, Se.Language.General.Error, $"Error importing file: {ex.Message}", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    [RelayCommand]
    public async Task PasteText()
    {
        if (Window == null) return;

        try
        {
            var clipboardText = await ClipboardHelper.GetTextAsync(Window);
            if (!string.IsNullOrEmpty(clipboardText))
            {
                if (IsBatchMode)
                {
                    BatchItems.Add(new TextToSpeechTextBatchItem($"{BatchItems.Count + 1}", clipboardText));
                }
                else
                {
                    InputText = clipboardText;
                }
            }
        }
        catch (Exception ex)
        {
            await MessageBox.Show(Window, Se.Language.General.Error, $"Error pasting text: {ex.Message}", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    [RelayCommand]
    public void AddBatchItem()
    {
        BatchItems.Add(new TextToSpeechTextBatchItem($"{BatchItems.Count + 1}", string.Empty));
    }

    [RelayCommand]
    public void RemoveBatchItem(TextToSpeechTextBatchItem? item)
    {
        if (item != null)
        {
            BatchItems.Remove(item);
        }
    }

    [RelayCommand]
    public void ClearBatchItems()
    {
        BatchItems.Clear();
    }

    [RelayCommand]
    public async Task BrowseOutputFolder()
    {
        if (Window == null) return;

        var folder = await _folderHelper.PickFolderAsync(Window, Se.Language.General.SelectedAFolderToSaveTo);
        if (!string.IsNullOrEmpty(folder))
        {
            OutputFolder = folder;
        }
    }

    [RelayCommand]
    public async Task GenerateAudio()
    {
        var engine = SelectedEngine;
        var voice = SelectedVoice;
        if (engine == null || voice == null)
        {
            await MessageBox.Show(Window!, Se.Language.General.Error, "Please select an engine and voice.", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!Voices.Contains(voice))
        {
            await MessageBox.Show(Window!, Se.Language.General.Error, "Selected voice is not available for the selected engine. Please select a different voice.", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (string.IsNullOrEmpty(OutputFolder))
        {
            await MessageBox.Show(Window!, Se.Language.General.Error, "Please select an output folder.", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var isInstalled = await IsEngineInstalled(engine);
        if (!isInstalled) return;

        _cancellationTokenSource = new CancellationTokenSource();
        _cancellationToken = _cancellationTokenSource.Token;
        ProgressValue = 0;
        ProgressText = "Starting...";
        IsGenerating = true;
        IsNotGenerating = false;
        ProgressOpacity = 1.0;
        SaveSettings();

        try
        {
            if (IsBatchMode && BatchItems.Count > 0)
            {
                await GenerateBatchAudio(engine, voice, _cancellationToken);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(InputText))
                {
                    await MessageBox.Show(Window!, Se.Language.General.Error, "Please enter some text or import a text file.", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                var baseFileName = string.IsNullOrWhiteSpace(FileName) ? "output" : SanitizeFileName(FileName);
                await GenerateSingleAudio(engine, voice, InputText, baseFileName, _cancellationToken, null);
            }

            ProgressValue = 100;
            ProgressText = "Complete!";

            await MessageBox.Show(Window!, Se.Language.General.Done, $"Audio generated successfully!\n\nOutput: {OutputFolder}", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Cancelled";
        }
        catch (Exception ex)
        {
            await MessageBox.Show(Window!, Se.Language.General.Error, $"Error: {ex.Message}", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            IsGenerating = false;
            IsNotGenerating = true;
            ProgressOpacity = 0;
        }
    }

    private async Task GenerateSingleAudio(ITtsEngine engine, Voice voice, string text, string baseFileName, CancellationToken cancellationToken, TextToSpeechTextBatchItem? batchItem)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        void UpdateProgress(double val, string txt)
        {
            if (batchItem != null)
            {
                batchItem.Progress = val;
                batchItem.ProgressText = txt;
            }
            else
            {
                IndividualProgressValue = val;
                IndividualProgressText = txt;
            }
        }

        UpdateProgress(10, "Generating audio...");

        var sw = Stopwatch.StartNew();
        var result = await engine.Speak(text, _tempFolder, voice, SelectedLanguage, SelectedRegion, SelectedModel, cancellationToken);
        sw.Stop();

        if (result.Error || string.IsNullOrEmpty(result.FileName))
        {
            throw new Exception("Failed to generate audio");
        }

        UpdateProgress(60, $"Converting format... ({sw.Elapsed.TotalSeconds:F1}s)");

        var outputFileName = Path.Combine(OutputFolder, $"{baseFileName}.{SelectedAudioFormat.ToLowerInvariant()}");

        if (SelectedAudioFormat == "MP3")
        {
            await ConvertWavToMp3Async(result.FileName, outputFileName, cancellationToken);
        }
        else
        {
            File.Copy(result.FileName, outputFileName, true);
        }

        File.Delete(result.FileName);

        if (DoSaveTxt)
        {
            UpdateProgress(80, "Saving text file...");
            var txtFileName = Path.Combine(OutputFolder, $"{baseFileName}.txt");
            await File.WriteAllTextAsync(txtFileName, text, cancellationToken);
        }

        UpdateProgress(100, $"Done! ({sw.Elapsed.TotalSeconds:F1}s)");
    }

    private async Task ConvertWavToMp3Async(string inputWav, string outputMp3, CancellationToken cancellationToken)
    {
        var ffmpegLocation = GetFfmpegLocation();
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegLocation,
                Arguments = $"-i \"{inputWav}\" -codec:a libmp3lame -q:a 2 \"{outputMp3}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();
        await process.WaitForExitAsync(cancellationToken);
    }

    private static string GetFfmpegLocation()
    {
        var ffmpegLocation = Configuration.Settings.General.FFmpegLocation;
        if (!Configuration.IsRunningOnWindows && (string.IsNullOrEmpty(ffmpegLocation) || !File.Exists(ffmpegLocation)))
        {
            ffmpegLocation = "ffmpeg";
        }

        return ffmpegLocation;
    }

    private async Task GenerateBatchAudio(ITtsEngine engine, Voice voice, CancellationToken cancellationToken)
    {
        var total = BatchItems.Count;
        var processed = 0;

        foreach (var item in BatchItems)
        {
            if (cancellationToken.IsCancellationRequested) break;

            ProgressText = $"Overall: {processed}/{total}";
            ProgressValue = (double)processed / total * 100.0;

            try
            {
                if (!string.IsNullOrWhiteSpace(item.Text))
                {
                    var safeName = SanitizeFileName(string.IsNullOrWhiteSpace(item.Name) ? $"{processed + 1}" : item.Name);
                    await GenerateSingleAudio(engine, voice, item.Text, safeName, cancellationToken, item);
                    item.IsProcessed = true;
                }
                else
                {
                    item.HasError = true;
                    item.ErrorMessage = "Empty text";
                }
            }
            catch (Exception ex)
            {
                item.HasError = true;
                item.ErrorMessage = ex.Message;
            }

            processed++;
            ProgressValue = (double)processed / total * 100.0;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "output";

        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
        {
            fileName = fileName.Replace(c, '_');
        }
        return fileName.Trim();
    }

    private async Task<bool> IsEngineInstalled(ITtsEngine engine)
    {
        if (Window == null) return false;

        if (engine is Qwen3TtsCpp)
        {
            if (!await engine.IsInstalled(SelectedRegion))
            {
                var answer = await MessageBox.Show(Window, "Download Qwen3 TTS?", $"\"Text to speech\" requires Qwen3 TTS.\n\nDownload now?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer != MessageBoxResult.Yes) return false;

                var dlResult = await _windowService.ShowDialogAsync<DownloadTtsWindow, DownloadTtsViewModel>(Window, vm => vm.StartDownloadQwen3TtsCpp(Qwen3TtsCppDownloadService.WindowsVariantVulkan));
                if (!dlResult.OkPressed) return false;
            }

            var qwen3ModelKey = Qwen3TtsCpp.ResolveModelKey(SelectedModel);
            if (!Qwen3TtsCpp.IsModelsInstalled(qwen3ModelKey))
            {
                var sizeText = qwen3ModelKey == Qwen3TtsCpp.ModelKey17BBase ? "~2.7 GB" : "~1.6 GB";
                var answer = await MessageBox.Show(Window, "Download Qwen3 TTS models?", $"\"Qwen3 TTS\" ({qwen3ModelKey}) requires models ({sizeText}).\n\nDownload models?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer != MessageBoxResult.Yes) return false;

                var dlResult = await _windowService.ShowDialogAsync<DownloadTtsWindow, DownloadTtsViewModel>(Window, vm => vm.StartDownloadQwen3TtsModels(qwen3ModelKey));
                return dlResult.OkPressed && Qwen3TtsCpp.IsModelsInstalled(qwen3ModelKey);
            }
            return true;
        }

        if (engine is KokoroTtsCpp)
        {
            if (!await engine.IsInstalled(SelectedRegion))
            {
                var answer = await MessageBox.Show(Window, "Download Kokoro TTS?", $"\"Text to speech\" requires Kokoro TTS.\n\nDownload now?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer != MessageBoxResult.Yes) return false;

                var dlResult = await _windowService.ShowDialogAsync<DownloadTtsWindow, DownloadTtsViewModel>(Window, vm => vm.StartDownloadKokoroTtsCpp());
                if (!dlResult.OkPressed) return false;
            }

            if (!KokoroTtsCpp.AreModelsInstalled())
            {
                var answer = await MessageBox.Show(Window, "Download Kokoro TTS models?", $"\"Kokoro TTS\" requires models (~380 MB).\n\nDownload models?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer != MessageBoxResult.Yes) return false;

                var dlResult = await _windowService.ShowDialogAsync<DownloadTtsWindow, DownloadTtsViewModel>(Window, vm => vm.StartDownloadKokoroTtsModels());
                return dlResult.OkPressed && KokoroTtsCpp.AreModelsInstalled();
            }
            return true;
        }

        if (engine is OmniVoiceTtsCpp)
        {
            if (!await engine.IsInstalled(SelectedRegion))
            {
                var answer = await MessageBox.Show(Window, "Download OmniVoice TTS?", $"\"Text to speech\" requires OmniVoice TTS.\n\nDownload now?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer != MessageBoxResult.Yes) return false;

                var dlResult = await _windowService.ShowDialogAsync<DownloadTtsWindow, DownloadTtsViewModel>(Window, vm => vm.StartDownloadOmniVoice(OmniVoiceDownloadService.WindowsVariantVulkan));
                if (!dlResult.OkPressed) return false;
            }

            if (!OmniVoiceTtsCpp.IsModelsInstalled())
            {
                var answer = await MessageBox.Show(Window, "Download OmniVoice TTS models?", $"\"OmniVoice TTS\" requires models.\n\nDownload models?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer != MessageBoxResult.Yes) return false;

                var dlResult = await _windowService.ShowDialogAsync<DownloadTtsWindow, DownloadTtsViewModel>(Window, vm => vm.StartDownloadOmniVoiceModels());
                return dlResult.OkPressed && OmniVoiceTtsCpp.IsModelsInstalled();
            }
            return true;
        }

        if (engine is Piper)
        {
            if (!await engine.IsInstalled(SelectedRegion))
            {
                var answer = await MessageBox.Show(Window, Se.Language.General.DownloadX, "Piper", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer != MessageBoxResult.Yes) return false;

                var result = await _windowService.ShowDialogAsync<DownloadTtsWindow, DownloadTtsViewModel>(Window, vm => vm.StartDownloadPiper());
                return await engine.IsInstalled(SelectedRegion);
            }
            return true;
        }

        if (engine is AllTalk)
        {
            var answer = await MessageBox.Show(Window, Se.Language.General.Error, "\"AllTalk\" requires a running local AllTalk web server.\n\nRead more?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == MessageBoxResult.Yes)
            {
                await Window.Launcher.LaunchUriAsync(new Uri("https://github.com/erew123/alltalk_tts"));
            }
            return await engine.IsInstalled(SelectedRegion);
        }

        if (engine is EdgeTts)
        {
            var answer = await MessageBox.Show(Window, Se.Language.General.Error, "\"EdgeTts\" requires the edge-tts CLI tool.\n\nInstall with: pipx install edge-tts\n\nRead more?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == MessageBoxResult.Yes)
            {
                await Window.Launcher.LaunchUriAsync(new Uri("https://github.com/rany2/edge-tts"));
            }
            return await engine.IsInstalled(SelectedRegion);
        }

        if (engine.HasKeyFile)
        {
            if (string.IsNullOrEmpty(KeyFile) || !File.Exists(KeyFile))
            {
                await MessageBox.Show(Window, Se.Language.General.Error, $"\"{engine.Name}\" requires a key file", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            return true;
        }

        if (engine.HasApiKey)
        {
            if (string.IsNullOrEmpty(ApiKey))
            {
                await MessageBox.Show(Window, Se.Language.General.Error, $"\"{engine.Name}\" requires an API key", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            return true;
        }

        return await engine.IsInstalled(SelectedRegion) || Window == null;
    }

    internal void SelectedEngineChanged(object? sender, SelectionChangedEventArgs e)
    {
        var engine = SelectedEngine;
        if (engine == null) return;

        Dispatcher.UIThread.Post(async () =>
        {
            SelectedVoice = null;

            var voices = await engine.GetVoices(SelectedLanguage?.Code ?? string.Empty);
            Voices.Clear();
            foreach (var vo in voices)
            {
                Voices.Add(vo);
            }

            var lastVoice = Voices.FirstOrDefault(v => v.Name == Se.Settings.Video.TextToSpeech.Voice);
            if (lastVoice == null)
            {
                lastVoice = Voices.FirstOrDefault(p => p.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase) ||
                                                       p.Name.Contains("English", StringComparison.OrdinalIgnoreCase));
            }
            SelectedVoice = lastVoice ?? Voices.FirstOrDefault();
            if (SelectedVoice == null) return;

            HasLanguageParameter = engine.HasLanguageParameter;
            HasApiKey = engine.HasApiKey;
            HasRegion = engine.HasRegion;
            HasModel = engine.HasModel;
            HasKeyFile = engine.HasKeyFile;

            if (HasLanguageParameter)
            {
                var languages = await engine.GetLanguages(SelectedVoice, SelectedModel);
                Languages.Clear();
                foreach (var language in languages)
                {
                    Languages.Add(language);
                }
                SelectedLanguage = Languages.FirstOrDefault();
            }

            if (HasRegion)
            {
                var regions = await engine.GetRegions();
                Regions.Clear();
                foreach (var region in regions)
                {
                    Regions.Add(region);
                }
                SelectedRegion = Regions.FirstOrDefault();
            }

            if (HasModel)
            {
                var models = await engine.GetModels();
                Models.Clear();
                foreach (var model in models)
                {
                    Models.Add(model);
                }
                SelectedModel = Models.FirstOrDefault();
            }

            if (SelectedEngine is AzureSpeech)
            {
                ApiKey = Se.Settings.Video.TextToSpeech.AzureApiKey;
                SelectedRegion = Se.Settings.Video.TextToSpeech.AzureRegion;
            }
            else if (SelectedEngine is ElevenLabs)
            {
                ApiKey = Se.Settings.Video.TextToSpeech.ElevenLabsApiKey;
            }
            else if (SelectedEngine is MistralSpeech)
            {
                ApiKey = Se.Settings.Video.TextToSpeech.MistralApiKey;
            }
            else if (SelectedEngine is Murf)
            {
                ApiKey = Se.Settings.Video.TextToSpeech.MurfApiKey;
            }
            else if (SelectedEngine is GoogleSpeech)
            {
                ApiKey = Se.Settings.Video.TextToSpeech.GoogleApiKey;
                KeyFile = Se.Settings.Video.TextToSpeech.GoogleKeyFile;
            }
        });
    }

    [RelayCommand]
    private async Task BrowseKeyFile()
    {
        var fileName = await _fileHelper.PickOpenFile(Window!, "Open key file", "json files", "*.json");
        if (string.IsNullOrEmpty(fileName)) return;
        KeyFile = fileName;
    }

    [RelayCommand]
    private async Task TestVoice()
    {
        var engine = SelectedEngine;
        var voice = SelectedVoice;
        if (engine == null || voice == null || Window == null) return;

        if (!Voices.Contains(voice))
        {
            await MessageBox.Show(Window, Se.Language.General.Error, "Selected voice is not available for the selected engine. Please select a different voice.", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var isInstalled = await IsEngineInstalled(engine);
        if (!isInstalled) return;

        SaveSettings();

        var text = Se.Settings.Video.TextToSpeech.VoiceTestText;
        if (string.IsNullOrEmpty(text))
        {
            text = "This is a test of the text to speech system.";
        }

        try
        {
            IsVoiceTestEnabled = false;
            ProgressText = "Generating preview...";
            ProgressOpacity = 1.0;

            var result = await engine.Speak(text, _tempFolder, voice, SelectedLanguage, SelectedRegion, SelectedModel, _cancellationToken);

            if (_cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!File.Exists(result.FileName))
            {
                await MessageBox.Show(Window, "Test Voice Error", $"Output audio file was not generated.", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ProgressText = "Playing preview...";
            await PlayAudio(result.FileName);

            ProgressText = "Preview complete";
            ProgressOpacity = 0;
        }
        catch (Exception ex)
        {
            await MessageBox.Show(Window, Se.Language.General.Error, $"Test voice error: {ex.Message}", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ProgressOpacity = 0;
        }
        finally
        {
            IsVoiceTestEnabled = true;
        }
    }

    [RelayCommand]
    private async Task OpenModelManager()
    {
        if (Window == null) return;

        await _windowService.ShowDialogAsync<ManageTtsModelsWindow, ManageTtsModelsViewModel>(Window, vm => { });
    }

    private async Task PlayAudio(string fileName)
    {
        lock (_playLock)
        {
            _mpvContext?.Stop();
            _mpvContext?.Dispose();

            _mpvContext = new LibMpvDynamicPlayer();
            _mpvContext.LoadLib();
            var err = _mpvContext.Initialize();
            if (err < 0)
            {
                throw new InvalidOperationException($"Failed to initialize mpv: {_mpvContext.GetErrorString(err)}");
            }
        }
        await _mpvContext.LoadAudio(fileName);
    }

    [RelayCommand]
    private void Ok()
    {
        OkPressed = true;
        Close();
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource.Cancel();
        IsGenerating = false;
        IsNotGenerating = true;
        ProgressOpacity = 0;
    }

    [RelayCommand]
    private void Done()
    {
        SaveSettings();
        _cancellationTokenSource.Cancel();
        IsGenerating = false;
        IsNotGenerating = true;
        ProgressOpacity = 0;
        Close();
    }

    private void Close()
    {
        Window?.Close();
    }

    internal void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Done();
        }
    }

    internal void OnClosing(WindowClosingEventArgs e)
    {
        if (!_isCleaningUp)
        {
            Cleanup();
        }
    }

    private void Cleanup()
    {
        if (_isCleaningUp)
        {
            return;
        }
        _isCleaningUp = true;

        try
        {
            _timer?.Stop();
            _playTimer?.Stop();

            _cancellationTokenSource.Cancel();

            lock (_playLock)
            {
                try
                {
                    _mpvContext?.Stop();
                    _mpvContext?.Dispose();
                }
                catch
                {
                }
                _mpvContext = null;
            }

            try
            {
                if (Directory.Exists(_tempFolder))
                {
                    Directory.Delete(_tempFolder, true);
                }
            }
            catch
            {
            }
        }
        finally
        {
            _isCleaningUp = false;
        }
    }

    internal void OnLoaded(RoutedEventArgs e)
    {
        if (SelectedEngine != null)
        {
            SelectedEngineChanged(null, null!);
        }
    }
}