using System;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.Engines;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public enum PipelineType
{
    Legacy,
    Chunked
}

public static class PipelineSelector
{
    public static PipelineType SelectPipeline(
        TranscriptionMode mode,
        TimeSpan videoDuration,
        ISpeechToTextEngine engine,
        bool translationEnabled,
        int estimatedChunks)
    {
        if (mode == TranscriptionMode.Legacy)
        {
            return PipelineType.Legacy;
        }

        if (mode == TranscriptionMode.Chunked)
        {
            return IsEngineCompatibleWithChunkedPipeline(engine) ? PipelineType.Chunked : PipelineType.Legacy;
        }

        return SelectAutomatic(videoDuration, engine, translationEnabled, estimatedChunks);
    }

    public static bool IsEngineCompatibleWithChunkedPipeline(ISpeechToTextEngine engine)
    {
        return !string.IsNullOrEmpty(engine.GetExecutable());
    }

    private static PipelineType SelectAutomatic(
        TimeSpan videoDuration,
        ISpeechToTextEngine engine,
        bool translationEnabled,
        int estimatedChunks)
    {
        if (!IsEngineCompatibleWithChunkedPipeline(engine))
        {
            return PipelineType.Legacy;
        }

        if (translationEnabled)
        {
            return PipelineType.Chunked;
        }

        if (videoDuration.TotalMinutes >= 30)
        {
            return PipelineType.Chunked;
        }

        return PipelineType.Chunked;
    }

    public static int GetDefaultChunkSeconds(TimeSpan videoDuration, TranscriptionMode mode)
    {
        if (mode == TranscriptionMode.Legacy)
        {
            return 0;
        }

        if (videoDuration.TotalMinutes <= 5)
        {
            return 30;
        }

        if (videoDuration.TotalMinutes <= 30)
        {
            return 45;
        }

        if (videoDuration.TotalMinutes <= 120)
        {
            return 60;
        }

        return 90;
    }

    public static bool ShouldUseCheckpointing(TranscriptionMode mode, TimeSpan videoDuration)
    {
        if (mode == TranscriptionMode.Legacy)
        {
            return false;
        }

        return videoDuration.TotalMinutes >= 10 || mode == TranscriptionMode.Chunked;
    }
}