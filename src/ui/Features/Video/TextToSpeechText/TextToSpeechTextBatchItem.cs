using CommunityToolkit.Mvvm.ComponentModel;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeechText;

public partial class TextToSpeechTextBatchItem : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private string? _outputAudioFileName;
    [ObservableProperty] private bool _isProcessed;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = string.Empty;

    public TextToSpeechTextBatchItem()
    {
    }

    public TextToSpeechTextBatchItem(string name, string text)
    {
        Name = name;
        Text = text;
    }
}