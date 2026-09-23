using System;
using System.Collections.Generic;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

namespace UITests.Features.Video.SpeechToText.Pipeline;

public class SubtitleDuplicateDetectorTests
{
    [Fact]
    public void IsDuplicate_SameTextWithinOverlap_ReturnsTrue()
    {
        var detector = new SubtitleDuplicateDetector(0.85, TimeSpan.FromSeconds(3));
        var lastKept = new Paragraph("Hello world", 0, 2000);
        var newSub = new Paragraph("Hello world", 1800, 3800);

        var result = detector.IsDuplicate(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity);

        Assert.True(result);
    }

    [Fact]
    public void IsDuplicate_DifferentTextWithinOverlap_ReturnsFalse()
    {
        var detector = new SubtitleDuplicateDetector(0.85, TimeSpan.FromSeconds(3));
        var lastKept = new Paragraph("Hello world", 0, 2000);
        var newSub = new Paragraph("Hello everyone", 1800, 3800);

        var result = detector.IsDuplicate(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity);

        Assert.False(result);
    }

    [Fact]
    public void IsDuplicate_OutsideOverlapWindow_ReturnsFalse()
    {
        var detector = new SubtitleDuplicateDetector(0.85, TimeSpan.FromSeconds(3));
        var lastKept = new Paragraph("Hello world", 0, 2000);
        var newSub = new Paragraph("Hello world", 5000, 7000);

        var result = detector.IsDuplicate(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity);

        Assert.False(result);
    }

    [Fact]
    public void CalculateSimilarity_IdenticalTexts_ReturnsOne()
    {
        var similarity = SubtitleDuplicateDetector.CalculateSimilarity("Hello world", "Hello world");
        Assert.Equal(1.0, similarity, 0.01);
    }

    [Fact]
    public void CalculateSimilarity_CompletelyDifferent_ReturnsLowValue()
    {
        var similarity = SubtitleDuplicateDetector.CalculateSimilarity("Hello", "Goodbye");
        Assert.True(similarity < 0.5);
    }

    [Fact]
    public void CalculateSimilarity_CaseInsensitive()
    {
        var similarity = SubtitleDuplicateDetector.CalculateSimilarity("HELLO WORLD", "hello world");
        Assert.Equal(1.0, similarity, 0.01);
    }

    [Fact]
    public void CalculateSimilarity_WhitespaceNormalized()
    {
        var similarity = SubtitleDuplicateDetector.CalculateSimilarity("Hello   world", "Hello world");
        Assert.Equal(1.0, similarity, 0.01);
    }

    [Fact]
    public void CalculateSimilarity_SimilarButNotIdentical()
    {
        var similarity = SubtitleDuplicateDetector.CalculateSimilarity("Hello world", "Hello world!");
        Assert.True(similarity > 0.8);
        Assert.True(similarity < 1.0);
    }

    [Fact]
    public void CalculateSimilarity_EmptyString_ReturnsZero()
    {
        Assert.Equal(0, SubtitleDuplicateDetector.CalculateSimilarity("", "Hello"));
        Assert.Equal(0, SubtitleDuplicateDetector.CalculateSimilarity("Hello", ""));
        Assert.Equal(0, SubtitleDuplicateDetector.CalculateSimilarity("", ""));
    }

    [Fact]
    public void CalculateSimilarity_LevenshteinDistance()
    {
        var similarity = SubtitleDuplicateDetector.CalculateSimilarity("kitten", "sitting");
        Assert.True(similarity > 0.4 && similarity < 0.6);
    }

    [Fact]
    public void NormalizeText_TrimsAndLowercases()
    {
        var normalized = SubtitleDuplicateDetector.NormalizeText("  HELLO World  ");
        Assert.Equal("hello world", normalized);
    }

    [Fact]
    public void SimilarityThreshold_Configurable()
    {
        var strictDetector = new SubtitleDuplicateDetector(0.95, TimeSpan.FromSeconds(3));
        var lenientDetector = new SubtitleDuplicateDetector(0.50, TimeSpan.FromSeconds(3));

        var lastKept = new Paragraph("Hello world", 0, 2000);
        var newSub = new Paragraph("Hello world test extra", 1800, 3800);

        Assert.False(strictDetector.IsDuplicate(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity));
        Assert.True(lenientDetector.IsDuplicate(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity));
    }

    [Fact]
    public void OverlapThreshold_Configurable()
    {
        var tightDetector = new SubtitleDuplicateDetector(0.85, TimeSpan.FromMilliseconds(500));
        var wideDetector = new SubtitleDuplicateDetector(0.85, TimeSpan.FromSeconds(10));

        var lastKept = new Paragraph("Hello world", 0, 2000);
        var newSub = new Paragraph("Hello world", 3000, 5000);

        Assert.False(tightDetector.IsDuplicate(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity));
        Assert.True(wideDetector.IsDuplicate(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity));
    }

    [Fact]
    public void IsDuplicate_AlmostIdenticalWithPunctuation()
    {
        var detector = new SubtitleDuplicateDetector(0.85, TimeSpan.FromSeconds(3));
        var lastKept = new Paragraph("Hello world.", 0, 2000);
        var newSub = new Paragraph("Hello world!", 1800, 3800);

        var result = detector.IsDuplicate(newSub, lastKept, SubtitleDuplicateDetector.CalculateSimilarity);

        Assert.True(result);
    }

    [Fact]
    public void CalculateSimilarity_UnicodeTexts()
    {
        var similarity = SubtitleDuplicateDetector.CalculateSimilarity("日本語テスト", "日本語テスト");
        Assert.Equal(1.0, similarity, 0.01);
    }

    [Fact]
    public void CalculateSimilarity_VeryLongSimilarTexts()
    {
        var text1 = "This is a very long subtitle that goes on for quite some time with multiple clauses and complex structure";
        var text2 = "This is a very long subtitle that goes on for quite some time with multiple clauses and complex structure";
        var similarity = SubtitleDuplicateDetector.CalculateSimilarity(text1, text2);
        Assert.Equal(1.0, similarity, 0.01);
    }
}