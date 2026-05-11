namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.Voices;

public class OmniVoiceVoice
{
    public string Voice { get; set; }
    public string FilePath { get; set; }

    public override string ToString()
    {
        return Voice;
    }

    public OmniVoiceVoice()
    {
        Voice = string.Empty;
        FilePath = string.Empty;
    }

    public OmniVoiceVoice(string voice, string filePath)
    {
        Voice = voice;
        FilePath = filePath;
    }
}
