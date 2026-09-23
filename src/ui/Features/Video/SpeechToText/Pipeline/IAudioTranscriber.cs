using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public interface IAudioExtractor
{
    Task ExtractChunkAsync(TimeSpan start, TimeSpan end, string outputPath, CancellationToken ct = default);
    Task<TimeSpan> GetVideoDurationAsync(CancellationToken ct = default);
}

public interface IAudioTranscriber
{
    Task<SubtitleEdit.Core.Common.Subtitle> TranscribeChunkAsync(
        string audioPath,
        TimeSpan offset,
        CancellationToken ct = default);

    string EngineId { get; }
    string ModelId { get; }
    string Language { get; }
}

public interface ISubtitleParser
{
    SubtitleEdit.Core.Common.Subtitle ParseFromFile(string srtPath, TimeSpan offset);
    SubtitleEdit.Core.Common.Subtitle ParseFromOutput(string output, TimeSpan offset);
}