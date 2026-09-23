namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Engines;

public sealed class SpeechToTextEngineCapabilities
{
    public bool SupportsTranslateDuringTranscription { get; init; } = true;
    public bool SupportsAutoTranslate { get; init; } = true;
    public bool SupportsBilingualOutput { get; init; } = true;
    public bool SupportsResumeFromCheckpoint { get; init; } = true;
    public bool SupportsChunkedTranscription { get; init; } = true;
    public bool SupportsLegacyTranscription { get; init; } = true;
    public bool SupportsAutomaticMode { get; init; } = true;
    public bool SupportsBackendSelection { get; init; } = false;
    public bool SupportsForcedAlignerSelection { get; init; } = false;
    public bool SupportsSceneAwareSplitting { get; init; } = true;
    public bool IsOffline { get; init; } = true;
    public bool IsFastStartup { get; init; } = false;
    public bool SupportsCustomCommandLine { get; init; } = true;
    public string Description { get; init; } = string.Empty;
}