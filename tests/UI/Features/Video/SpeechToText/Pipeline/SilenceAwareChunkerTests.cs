using System;
using System.Collections.Generic;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

namespace UITests.Features.Video.SpeechToText.Pipeline;

public class SilenceAwareChunkerTests
{
    [Fact]
    public void CalculateChunkBoundaries_NoSilence_SplitsEvenly()
    {
        var chunker = new SilenceAwareChunker(
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromMilliseconds(300),
            -40.0);

        var videoDuration = TimeSpan.FromMinutes(5);
        var silenceRegions = new List<(TimeSpan start, TimeSpan end)>();

        var chunks = chunker.CalculateChunkBoundaries(videoDuration, silenceRegions);

        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Duration >= TimeSpan.FromSeconds(20), $"Chunk duration {chunk.Duration} is less than minimum");
            Assert.True(chunk.Duration <= TimeSpan.FromSeconds(65), $"Chunk duration {chunk.Duration} exceeds maximum");
        }
    }

    [Fact]
    public void CalculateChunkBoundaries_ShortVideo_LessChunks()
    {
        var chunker = new SilenceAwareChunker();
        var videoDuration = TimeSpan.FromSeconds(45);
        var silenceRegions = new List<(TimeSpan start, TimeSpan end)>();

        var chunks = chunker.CalculateChunkBoundaries(videoDuration, silenceRegions);

        Assert.Single(chunks);
        Assert.Equal(TimeSpan.Zero, chunks[0].Start);
        Assert.Equal(videoDuration, chunks[0].End);
    }

    [Fact]
    public void CalculateChunkBoundaries_EmptyVideo_ReturnsEmpty()
    {
        var chunker = new SilenceAwareChunker();
        var videoDuration = TimeSpan.Zero;
        var silenceRegions = new List<(TimeSpan start, TimeSpan end)>();

        var chunks = chunker.CalculateChunkBoundaries(videoDuration, silenceRegions);

        Assert.Empty(chunks);
    }

    [Fact]
    public void CalculateChunkBoundaries_SilenceAtBoundaries_SplitsAtSilence()
    {
        var chunker = new SilenceAwareChunker(
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromMilliseconds(300),
            -40.0);

        var videoDuration = TimeSpan.FromMinutes(10);
        var silenceRegions = new List<(TimeSpan start, TimeSpan end)>
        {
            (TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(31)),
            (TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(91)),
            (TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(151))
        };

        var chunks = chunker.CalculateChunkBoundaries(videoDuration, silenceRegions);

        Assert.NotEmpty(chunks);
        var firstChunkEnd = chunks[0].End;
        Assert.True(Math.Abs(firstChunkEnd.TotalSeconds - 30) < 2 ||
                    firstChunkEnd < TimeSpan.FromSeconds(35));
    }

    [Fact]
    public void CalculateChunkBoundaries_ContinuousSpeech_NoChunkExceedsMax()
    {
        var chunker = new SilenceAwareChunker(
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromMilliseconds(300),
            -40.0);

        var videoDuration = TimeSpan.FromHours(1);
        var silenceRegions = new List<(TimeSpan start, TimeSpan end)>();

        var chunks = chunker.CalculateChunkBoundaries(videoDuration, silenceRegions);

        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Duration <= TimeSpan.FromSeconds(65),
                $"Chunk duration {chunk.Duration} exceeds max allowed");
        }
    }

    [Fact]
    public void CalculateChunkBoundaries_ChunksCoverFullVideo()
    {
        var chunker = new SilenceAwareChunker();
        var videoDuration = TimeSpan.FromMinutes(30);
        var silenceRegions = new List<(TimeSpan start, TimeSpan end)>();

        var chunks = chunker.CalculateChunkBoundaries(videoDuration, silenceRegions);

        Assert.NotEmpty(chunks);
        Assert.Equal(TimeSpan.Zero, chunks[0].Start);
        Assert.Equal(videoDuration, chunks[^1].End);

        for (var i = 1; i < chunks.Count; i++)
        {
            Assert.Equal(chunks[i].Start, chunks[i - 1].End);
        }
    }

    [Fact]
    public void CalculateChunkBoundaries_NoZeroDurationChunks()
    {
        var chunker = new SilenceAwareChunker();
        var videoDuration = TimeSpan.FromMinutes(5);
        var silenceRegions = new List<(TimeSpan start, TimeSpan end)>();

        var chunks = chunker.CalculateChunkBoundaries(videoDuration, silenceRegions);

        foreach (var chunk in chunks)
        {
            Assert.NotEqual(TimeSpan.Zero, chunk.Duration);
            Assert.True(chunk.Duration > TimeSpan.Zero);
        }
    }

    [Fact]
    public void CalculateChunkBoundaries_VeryLongVideo_ManyChunks()
    {
        var chunker = new SilenceAwareChunker();
        var videoDuration = TimeSpan.FromHours(20);
        var silenceRegions = new List<(TimeSpan start, TimeSpan end)>();

        var chunks = chunker.CalculateChunkBoundaries(videoDuration, silenceRegions);

        Assert.True(chunks.Count > 100, $"Expected many chunks for 20hr video, got {chunks.Count}");
    }

    [Fact]
    public void CalculateChunkBoundaries_100HourVideo_Works()
    {
        var chunker = new SilenceAwareChunker();
        var videoDuration = TimeSpan.FromHours(120);
        var silenceRegions = new List<(TimeSpan start, TimeSpan end)>();

        var chunks = chunker.CalculateChunkBoundaries(videoDuration, silenceRegions);

        Assert.NotEmpty(chunks);
        Assert.Equal(TimeSpan.Zero, chunks[0].Start);
        Assert.Equal(videoDuration, chunks[^1].End);
    }

    [Fact]
    public void CalculateChunkBoundaries_LastChunkMayBeSmaller()
    {
        var chunker = new SilenceAwareChunker(
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromMilliseconds(300),
            -40.0);

        var videoDuration = TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(15);
        var silenceRegions = new List<(TimeSpan start, TimeSpan end)>();

        var chunks = chunker.CalculateChunkBoundaries(videoDuration, silenceRegions);

        Assert.True(chunks[^1].Duration < TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void MinChunkDuration_Respected()
    {
        var chunker = new SilenceAwareChunker(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(120),
            TimeSpan.FromMilliseconds(300),
            -40.0);

        var videoDuration = TimeSpan.FromMinutes(10);
        var silenceRegions = new List<(TimeSpan start, TimeSpan end)>
        {
            (TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(61))
        };

        var chunks = chunker.CalculateChunkBoundaries(videoDuration, silenceRegions);

        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Duration >= TimeSpan.FromSeconds(30) - TimeSpan.FromMilliseconds(100),
                $"Chunk {chunk.Start} -> {chunk.End} is smaller than min");
        }
    }

    [Fact]
    public void Properties_ReturnConfiguredValues()
    {
        var chunker = new SilenceAwareChunker(
            TimeSpan.FromSeconds(25),
            TimeSpan.FromSeconds(55));

        Assert.Equal(TimeSpan.FromSeconds(25), chunker.MinChunkDuration);
        Assert.Equal(TimeSpan.FromSeconds(55), chunker.MaxChunkDuration);
    }
}