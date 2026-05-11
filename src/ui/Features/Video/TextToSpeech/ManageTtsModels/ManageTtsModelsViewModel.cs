using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Features.Shared;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Engines;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.ManageTtsModels;

public partial class TtsModelItem : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _engine = string.Empty;
    [ObservableProperty] private string _path = string.Empty;
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private string _size = string.Empty;
}

public partial class ManageTtsModelsViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<TtsModelItem> _models;
    [ObservableProperty] private TtsModelItem? _selectedModel;
    [ObservableProperty] private bool _canDelete;

    public Window? Window { get; set; }
    public bool OkPressed { get; private set; }

    public ManageTtsModelsViewModel()
    {
        _models = new ObservableCollection<TtsModelItem>();
        LoadModels();
    }

    private void LoadModels()
    {
        Models.Clear();

        var qwen3Folder = Qwen3TtsCpp.GetSetModelsFolder();
        var qwen3Installed = Qwen3TtsCpp.IsModelsInstalled(Qwen3TtsCpp.ModelKey06B);
        var qwen317BInstalled = Qwen3TtsCpp.IsModelsInstalled(Qwen3TtsCpp.ModelKey17BBase);

        Models.Add(new TtsModelItem
        {
            Name = "Qwen3 0.6B",
            Engine = "Qwen3 TTS",
            Path = qwen3Folder,
            IsInstalled = qwen3Installed,
            Size = qwen3Installed ? GetFolderSize(qwen3Folder) : "~2.0 GB"
        });

        Models.Add(new TtsModelItem
        {
            Name = "Qwen3 1.7B",
            Engine = "Qwen3 TTS",
            Path = qwen3Folder,
            IsInstalled = qwen317BInstalled,
            Size = qwen317BInstalled ? GetFolderSize(qwen3Folder) : "~2.7 GB"
        });

        var kokoroFolder = KokoroTtsCpp.GetSetModelsFolder();
        var kokoroInstalled = KokoroTtsCpp.AreModelsInstalled();

        Models.Add(new TtsModelItem
        {
            Name = "Kokoro Models",
            Engine = "Kokoro TTS",
            Path = kokoroFolder,
            IsInstalled = kokoroInstalled,
            Size = kokoroInstalled ? GetFolderSize(kokoroFolder) : "~380 MB"
        });

        var omniFolder = OmniVoiceTtsCpp.GetSetModelsFolder();
        var omniInstalled = OmniVoiceTtsCpp.IsModelsInstalled();

        Models.Add(new TtsModelItem
        {
            Name = "OmniVoice Models",
            Engine = "OmniVoice TTS",
            Path = omniFolder,
            IsInstalled = omniInstalled,
            Size = omniInstalled ? GetFolderSize(omniFolder) : "~1.4 GB"
        });

        var chatterboxFolder = ChatterboxTtsCpp.GetSetModelsFolder();
        var chatterboxTurboInstalled = ChatterboxTtsCpp.AreModelsInstalled(ChatterboxTtsCpp.ModelKeyTurbo);
        var chatterboxBaseInstalled = ChatterboxTtsCpp.AreModelsInstalled(ChatterboxTtsCpp.ModelKeyBase);

        Models.Add(new TtsModelItem
        {
            Name = "Chatterbox Turbo",
            Engine = "Chatterbox TTS",
            Path = chatterboxFolder,
            IsInstalled = chatterboxTurboInstalled,
            Size = chatterboxTurboInstalled ? GetFolderSize(chatterboxFolder) : "~1 GB"
        });

        Models.Add(new TtsModelItem
        {
            Name = "Chatterbox Base",
            Engine = "Chatterbox TTS",
            Path = chatterboxFolder,
            IsInstalled = chatterboxBaseInstalled,
            Size = chatterboxBaseInstalled ? GetFolderSize(chatterboxFolder) : "~990 MB"
        });

        var piperFolder = Piper.GetSetPiperFolder();
        if (Directory.Exists(piperFolder))
        {
            var piperFiles = Directory.GetFiles(piperFolder, "*.onnx", SearchOption.AllDirectories);
            if (piperFiles.Length > 0)
            {
                var totalSize = piperFiles.Sum(f => new FileInfo(f).Length);
                Models.Add(new TtsModelItem
                {
                    Name = "Piper Models",
                    Engine = "Piper TTS",
                    Path = piperFolder,
                    IsInstalled = true,
                    Size = FormatSize(totalSize)
                });
            }
        }
    }

    private static string GetFolderSize(string folder)
    {
        if (!Directory.Exists(folder))
            return "0 MB";

        var size = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
        return FormatSize(size);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0) + " KB";
        if (bytes < 1024 * 1024 * 1024) return (bytes / (1024.0 * 1024.0)) + " MB";
        return (bytes / (1024.0 * 1024.0 * 1024.0)) + " GB";
    }

    partial void OnSelectedModelChanged(TtsModelItem? value)
    {
        CanDelete = value?.IsInstalled ?? false;
    }

    [RelayCommand]
    private async Task DeleteSelectedModel()
    {
        if (SelectedModel == null || !SelectedModel.IsInstalled) return;

        var confirmed = await MessageBox.Show(
            Window!,
            "Delete Model",
            $"Are you sure you want to delete '{SelectedModel.Name}'?\n\nThis will remove all files in:\n{SelectedModel.Path}",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirmed != MessageBoxResult.Yes) return;

        try
        {
            if (Directory.Exists(SelectedModel.Path))
            {
                Directory.Delete(SelectedModel.Path, true);
            }

            await MessageBox.Show(Window!, "Success", "Model deleted successfully.", MessageBoxButtons.OK, MessageBoxIcon.Information);
            LoadModels();
        }
        catch (Exception ex)
        {
            await MessageBox.Show(Window!, Se.Language.General.Error, $"Failed to delete model: {ex.Message}", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteAllModels()
    {
        var confirmed = await MessageBox.Show(
            Window!,
            "Delete All Models",
            "Are you sure you want to delete ALL downloaded TTS models?\n\nThis cannot be undone!",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirmed != MessageBoxResult.Yes) return;

        var deleted = 0;
        var failed = 0;

        foreach (var model in Models.Where(m => m.IsInstalled))
        {
            try
            {
                if (Directory.Exists(model.Path))
                {
                    Directory.Delete(model.Path, true);
                    deleted++;
                }
            }
            catch
            {
                failed++;
            }
        }

        var msg = failed == 0
            ? $"Deleted {deleted} model(s) successfully."
            : $"Deleted {deleted} model(s). {failed} failed to delete.";

        await MessageBox.Show(Window!, "Result", msg, MessageBoxButtons.OK, MessageBoxIcon.Information);
        LoadModels();
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
        Close();
    }

    private void Close()
    {
        Dispatcher.UIThread.Post(() => Window?.Close());
    }

    internal void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Window?.Close();
        }
    }

    internal void OnLoaded()
    {
        UiUtil.RestoreWindowPosition(Window);
    }

    internal void OnClosing()
    {
        UiUtil.SaveWindowPosition(Window);
    }
}