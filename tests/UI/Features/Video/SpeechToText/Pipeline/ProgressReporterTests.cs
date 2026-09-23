using System;
using System.Threading;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

namespace UITests.Features.Video.SpeechToText.Pipeline;

public class ProgressReporterTests
{
    [Fact]
    public void Start_SetsInitialState()
    {
        var reporter = new ProgressReporter();
        reporter.Start(100);

        var progress = reporter.GetProgress();
        Assert.Equal(0, progress.CurrentChunk);
        Assert.Equal(100, progress.TotalChunks);
        Assert.Equal(TranscriptionStage.Initializing, progress.Stage);
    }

    [Fact]
    public void SetStage_UpdatesStage()
    {
        var reporter = new ProgressReporter();
        reporter.Start(100);
        reporter.SetStage(TranscriptionStage.Transcribing);

        var progress = reporter.GetProgress();
        Assert.Equal(TranscriptionStage.Transcribing, progress.Stage);
    }

    [Fact]
    public void UpdateChunk_IncrementsChunk()
    {
        var reporter = new ProgressReporter();
        reporter.Start(100);
        reporter.UpdateChunk(50, 1000);

        var progress = reporter.GetProgress();
        Assert.Equal(50, progress.CurrentChunk);
        Assert.Equal(1000, progress.SubtitleCount);
    }

    [Fact]
    public void IncrementSubtitleCount_IncreasesCount()
    {
        var reporter = new ProgressReporter();
        reporter.Start(100);
        reporter.IncrementSubtitleCount();
        reporter.IncrementSubtitleCount();
        reporter.IncrementSubtitleCount(5);

        var progress = reporter.GetProgress();
        Assert.Equal(7, progress.SubtitleCount);
    }

    [Fact]
    public void PercentComplete_NeverExceeds100()
    {
        var reporter = new ProgressReporter();
        reporter.Start(100);
        reporter.UpdateChunk(150, 0);

        var progress = reporter.GetProgress();
        Assert.True(progress.OverallPercent <= 100.0);
    }

    [Fact]
    public void PercentComplete_NeverBelowZero()
    {
        var reporter = new ProgressReporter();
        reporter.Start(100);
        reporter.UpdateChunk(-5, 0);

        var progress = reporter.GetProgress();
        Assert.True(progress.OverallPercent >= 0.0);
    }

    [Fact]
    public void GetProgress_IncludesSpeedCalculation()
    {
        var reporter = new ProgressReporter();
        reporter.Start(100);
        reporter.UpdateChunk(10, 100);

        var progress = reporter.GetProgress();
        Assert.True(progress.SpeedMultiplier >= 0);
    }

    [Fact]
    public void Stop_SetsStageToComplete()
    {
        var reporter = new ProgressReporter();
        reporter.Start(100);
        reporter.UpdateChunk(100, 5000);
        reporter.Stop();

        var progress = reporter.GetProgress();
        Assert.Equal(TranscriptionStage.Complete, progress.Stage);
    }

    [Fact]
    public void Cancel_SetsStageToCancelled()
    {
        var reporter = new ProgressReporter();
        reporter.Start(100);
        reporter.Cancel();

        var progress = reporter.GetProgress();
        Assert.Equal(TranscriptionStage.Cancelled, progress.Stage);
    }

    [Fact]
    public void Fail_SetsStageToFailed()
    {
        var reporter = new ProgressReporter();
        reporter.Start(100);
        reporter.Fail();

        var progress = reporter.GetProgress();
        Assert.Equal(TranscriptionStage.Failed, progress.Stage);
    }

    [Fact]
    public void GetShortSummary_ContainsChunkInfo()
    {
        var reporter = new ProgressReporter();
        reporter.Start(100);
        reporter.UpdateChunk(50, 1000);

        var summary = reporter.GetShortSummary();
        Assert.Contains("50/", summary);
        Assert.Contains("1000", summary);
    }

    [Fact]
    public void GetDetailedReport_ContainsAllSections()
    {
        var reporter = new ProgressReporter();
        reporter.Start(100);
        reporter.SetStage(TranscriptionStage.Transcribing);
        reporter.UpdateChunk(50, 1000, "Preview text");

        var report = reporter.GetProgress();
        Assert.Equal(50, reporter.CurrentChunk);
        Assert.Equal(100, reporter.TotalChunks);
        Assert.Equal(1000, reporter.SubtitleCount);
        Assert.Equal(TranscriptionStage.Transcribing, reporter.Stage);
    }

    [Fact]
    public void EstimatedFinishTime_IsInFuture_WhenProcessing()
    {
        var reporter = new ProgressReporter();
        reporter.Start(100);
        reporter.UpdateChunk(50, 500);

        var progress = reporter.GetProgress();
        Assert.True(progress.EstimatedFinishTime != default);
    }

    [Fact]
    public void EstimatedRemaining_IsZero_WhenAtZeroPercent()
    {
        var reporter = new ProgressReporter();
        reporter.Start(100);
        reporter.UpdateChunk(0, 0);

        var progress = reporter.GetProgress();
        Assert.Equal(TimeSpan.Zero, progress.EstimatedRemaining);
    }
}