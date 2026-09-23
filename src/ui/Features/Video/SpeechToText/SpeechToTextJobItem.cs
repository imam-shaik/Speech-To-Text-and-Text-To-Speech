using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText;

public partial class SpeechToTextJobItem : ObservableObject
{
    [ObservableProperty] private string _inputVideoFileName;
    [ObservableProperty] private string _inputVideoFileNameShort;
    [ObservableProperty] private long _size;
    [ObservableProperty] private string _sizeDisplay;
    [ObservableProperty] private string _outputSubtitleFileName;
    [ObservableProperty] private string _status;

    public FfmpegMediaInfo MediaInfo { get; set; }

    public SpeechToTextJobItem(string inputVideoFileName, string status, FfmpegMediaInfo mediaInfo)
    {
        _outputSubtitleFileName = string.Empty;
        InputVideoFileName = inputVideoFileName;
        var fileName = Path.GetFileName(inputVideoFileName);
        var duration = mediaInfo?.Duration;
        var durationDisplay = duration?.TotalMilliseconds > 0
            ? $" ({duration:h\\:mm\\:ss})"
            : string.Empty;

        if (inputVideoFileName.Length + durationDisplay.Length > 75)
        {
            InputVideoFileNameShort = fileName + durationDisplay;
        }
        else
        {
            InputVideoFileNameShort = inputVideoFileName + durationDisplay;
        }

        var fileInfo = new FileInfo(inputVideoFileName);
        Size = fileInfo.Length;

        SizeDisplay = Utilities.FormatBytesToDisplayFileSize(fileInfo.Length);
        Status = status;

        MediaInfo = mediaInfo;
    }
}