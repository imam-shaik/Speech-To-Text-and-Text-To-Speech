using System;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

namespace UITests.Features.Video.SpeechToText.Pipeline;

public class ChunkBoundaryDuplicateTests
{
    private readonly SubtitleDuplicateDetector _detector;
    private readonly SubtitleDuplicateDetector _strictDetector;

    public ChunkBoundaryDuplicateTests()
    {
        _detector = new SubtitleDuplicateDetector(0.85, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(500), 0.95);
        _strictDetector = new SubtitleDuplicateDetector(0.85, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(100), 0.98);
    }

    [Fact]
    public void Case1_SamePhrase_SmallPositiveOverlap_RemovesDuplicate()
    {
        var lastKept = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(59500)),
            new TimeCode(TimeSpan.FromMilliseconds(63000)),
            "Hello everyone");

        var newSub = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(62800)),
            new TimeCode(TimeSpan.FromMilliseconds(65500)),
            "Hello everyone");

        var overlap = newSub.StartTime.TotalMilliseconds - lastKept.EndTime.TotalMilliseconds;

        Assert.True(overlap < 0, "newSub starts before lastKept ends");
        Assert.True(Math.Abs(overlap) < 500, $"Overlap magnitude ({Math.Abs(overlap):F0}ms) should be within 500ms");
        Assert.Equal(1.0, SubtitleDuplicateDetector.CalculateSimilarity(newSub.Text, lastKept.Text), 0.01);

        var isDuplicate = _detector.IsDuplicateAtBoundary(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity);
        Assert.True(isDuplicate, "Same phrase at boundary with small overlap should be detected as duplicate");
    }

    [Fact]
    public void Case2_DifferentPhrase_KeepsBoth()
    {
        var lastKept = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(59500)),
            new TimeCode(TimeSpan.FromMilliseconds(60200)),
            "Hello everyone");

        var newSub = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(60300)),
            new TimeCode(TimeSpan.FromMilliseconds(63000)),
            "Welcome back");

        var isDuplicate = _detector.IsDuplicateAtBoundary(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity);
        Assert.False(isDuplicate, "Different phrases at boundary should NOT be duplicate");
    }

    [Fact]
    public void Case3_NearDuplicate_SmallGap_IdenticalText_RemovesDuplicate()
    {
        var lastKept = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(59500)),
            new TimeCode(TimeSpan.FromMilliseconds(60000)),
            "Hello");

        var newSub = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(60100)),
            new TimeCode(TimeSpan.FromMilliseconds(60600)),
            "Hello");

        var overlap = newSub.StartTime.TotalMilliseconds - lastKept.EndTime.TotalMilliseconds;

        Assert.True(Math.Abs(overlap) < 500, $"Gap ({Math.Abs(overlap):F0}ms) should be within 500ms threshold");
        Assert.Equal(1.0, SubtitleDuplicateDetector.CalculateSimilarity(newSub.Text, lastKept.Text), 0.01);

        var isDuplicate = _detector.IsDuplicateAtBoundary(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity);
        Assert.True(isDuplicate, "Near-duplicate with identical text should be detected");
    }

    [Fact]
    public void LargeGap_SameText_NotDuplicate()
    {
        var lastKept = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(59000)),
            new TimeCode(TimeSpan.FromMilliseconds(62000)),
            "Thank you very much");

        var newSub = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(65000)),
            new TimeCode(TimeSpan.FromMilliseconds(68000)),
            "Thank you very much");

        var overlap = newSub.StartTime.TotalMilliseconds - lastKept.EndTime.TotalMilliseconds;

        Assert.True(Math.Abs(overlap) > 500, $"Gap ({Math.Abs(overlap):F0}ms) should exceed 500ms threshold");

        var isDuplicate = _detector.IsDuplicateAtBoundary(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity);
        Assert.False(isDuplicate, "Large gap with same text should NOT be duplicate at boundary");
    }

    [Fact]
    public void ExactOverlap_SameText_IsDuplicate()
    {
        var lastKept = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(60000)),
            new TimeCode(TimeSpan.FromMilliseconds(63000)),
            "Good morning everyone");

        var newSub = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(62600)),
            new TimeCode(TimeSpan.FromMilliseconds(65600)),
            "Good morning everyone");

        var overlap = newSub.StartTime.TotalMilliseconds - lastKept.EndTime.TotalMilliseconds;

        Assert.True(overlap < 0, "newSub starts before lastKept ends");
        Assert.True(Math.Abs(overlap) < 500, $"Overlap magnitude ({Math.Abs(overlap):F0}ms) should be within threshold");
        Assert.Equal(1.0, SubtitleDuplicateDetector.CalculateSimilarity(newSub.Text, lastKept.Text), 0.01);

        var isDuplicate = _detector.IsDuplicateAtBoundary(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity);
        Assert.True(isDuplicate, "Exact overlap with same text should be duplicate");
    }

    [Fact]
    public void WithinChunk_NormalDuplicate_StillDetected()
    {
        var detector = new SubtitleDuplicateDetector(0.85, TimeSpan.FromSeconds(3));

        var lastKept = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(1000)),
            new TimeCode(TimeSpan.FromMilliseconds(3000)),
            "Hello world");

        var newSub = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(2500)),
            new TimeCode(TimeSpan.FromMilliseconds(5000)),
            "Hello world");

        var isDuplicate = detector.IsDuplicate(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity);
        Assert.True(isDuplicate, "Normal duplicate within same chunk should still be detected");
    }

    [Fact]
    public void BoundaryDetection_RequiresSimilarText()
    {
        var lastKept = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(59500)),
            new TimeCode(TimeSpan.FromMilliseconds(63000)),
            "Test phrase");

        var newSub = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(62800)),
            new TimeCode(TimeSpan.FromMilliseconds(66000)),
            "Test phrase");

        var overlap = newSub.StartTime.TotalMilliseconds - lastKept.EndTime.TotalMilliseconds;

        Assert.True(Math.Abs(overlap) < 500, "Gap should be small");

        var normalResult = _detector.IsDuplicate(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity);
        var boundaryResult = _detector.IsDuplicateAtBoundary(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity);

        Assert.True(normalResult, "Normal detection should flag as duplicate");
        Assert.True(boundaryResult, "Boundary detection should also flag as duplicate when text is identical");
    }

    [Fact]
    public void StrictBoundary_RequiresNearExactMatch()
    {
        var lastKept = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(59500)),
            new TimeCode(TimeSpan.FromMilliseconds(63000)),
            "Hello world");

        var newSub = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(62800)),
            new TimeCode(TimeSpan.FromMilliseconds(66000)),
            "Hello world!");

        var overlap = newSub.StartTime.TotalMilliseconds - lastKept.EndTime.TotalMilliseconds;
        var similarity = SubtitleDuplicateDetector.CalculateSimilarity(newSub.Text, lastKept.Text);

        Assert.True(Math.Abs(overlap) < 500, "Gap is small");
        Assert.True(similarity > 0.9 && similarity < 1.0, "Text is similar but not exact");

        var isDuplicate = _strictDetector.IsDuplicateAtBoundary(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity);
        Assert.False(isDuplicate, "Strict boundary should require near-exact match");
    }

    [Fact]
    public void ChunkBoundary_SmallGap_SameText_IsBoundaryDuplicate()
    {
        var lastKept = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(59500)),
            new TimeCode(TimeSpan.FromMilliseconds(62800)),
            "Thank you");

        var newSub = new Paragraph(
            new TimeCode(TimeSpan.FromMilliseconds(62900)),
            new TimeCode(TimeSpan.FromMilliseconds(66000)),
            "Thank you");

        var overlap = newSub.StartTime.TotalMilliseconds - lastKept.EndTime.TotalMilliseconds;

        Assert.True(Math.Abs(overlap) < 500, $"Gap ({Math.Abs(overlap):F0}ms) should be within 500ms threshold");

        var isDuplicate = _detector.IsDuplicateAtBoundary(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity);
        Assert.True(isDuplicate, "Same phrase with small gap at chunk boundary should be boundary duplicate");
    }
}