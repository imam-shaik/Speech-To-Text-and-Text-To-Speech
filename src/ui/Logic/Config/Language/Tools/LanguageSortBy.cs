namespace Nikse.SubtitleEdit.Logic.Config.Language.Tools;

public class LanguageSortBy
{
    public string Title { get; set; }
    public string SortOrder { get; set; }
    public string SortByNumber { get; set; }
    public string SortByStartTime { get; set; }
    public string SortByEndTime { get; set; }

    public LanguageSortBy()
    {
        Title = "Sort subtitles";
        SortOrder = "Sort order";
        SortByNumber = "Number";
        SortByStartTime = "Start time";
        SortByEndTime = "End time";
    }
}