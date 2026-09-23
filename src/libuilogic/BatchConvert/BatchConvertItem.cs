using CommunityToolkit.Mvvm.ComponentModel;
using Nikse.SubtitleEdit.Core.Common;
using System.IO;

namespace Nikse.SubtitleEdit.UiLogic.BatchConvert;

public enum BatchConvertStatus
{
    Queued,
    Preparing,
    ExtractingAudio,
    Recognizing,
    Translating,
    WritingSubtitle,
    Completed,
    Failed,
    Cancelled,
    Skipped
}

public partial class BatchConvertItem : ObservableObject
{
    [ObservableProperty] private BatchConvertStatus _status;
    public string StatusMessage { get; set; }
    public string FileName { get; set; }
    public string FolderName { get; set; }
    public long Size { get; set; }
    public string DisplaySize { get; set; }
    public string Format { get; set; }
    public Subtitle? Subtitle { get; set; }
    public string OutputFileName { get; set; }
    public string LanguageCode { get; set; }
    public string TrackNumber { get; set; }

    public object? ImageSubtitle { get; set; }

    public int QueuePosition { get; set; }

    public BatchConvertItem()
    {
        FileName = string.Empty;
        FolderName = string.Empty;
        Format = string.Empty;
        Status = BatchConvertStatus.Queued;
        StatusMessage = string.Empty;
        DisplaySize = string.Empty;
        OutputFileName = string.Empty;
        LanguageCode = string.Empty;
        TrackNumber = string.Empty;
    }

    public BatchConvertItem(string fileName, long size, string format, Subtitle? subtitle)
    {
        FileName = fileName;
        FolderName = Path.GetFileName(Path.GetDirectoryName(fileName) ?? string.Empty);
        Size = size;
        Format = format;
        Status = BatchConvertStatus.Queued;
        StatusMessage = string.Empty;
        Subtitle = subtitle;
        DisplaySize = Utilities.FormatBytesToDisplayFileSize(size);
        OutputFileName = string.Empty;
        LanguageCode = string.Empty;
        TrackNumber = string.Empty;
    }
}