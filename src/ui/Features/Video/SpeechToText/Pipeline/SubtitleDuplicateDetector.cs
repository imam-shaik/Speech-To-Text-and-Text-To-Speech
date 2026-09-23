using System;
using System.Collections.Generic;
using System.Linq;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class SubtitleDuplicateDetector
{
    private readonly double _similarityThreshold;
    private readonly TimeSpan _overlapThreshold;
    private readonly TimeSpan _boundaryOverlapThreshold;
    private readonly double _boundarySimilarityThreshold;

    public double SimilarityThreshold => _similarityThreshold;
    public TimeSpan OverlapThreshold => _overlapThreshold;

    public SubtitleDuplicateDetector(
        double similarityThreshold = 0.85,
        TimeSpan? overlapThreshold = null,
        TimeSpan? boundaryOverlapThreshold = null,
        double boundarySimilarityThreshold = 0.95)
    {
        _similarityThreshold = similarityThreshold;
        _overlapThreshold = overlapThreshold ?? TimeSpan.FromSeconds(3);
        _boundaryOverlapThreshold = boundaryOverlapThreshold ?? TimeSpan.FromMilliseconds(500);
        _boundarySimilarityThreshold = boundarySimilarityThreshold;
    }

    public bool IsDuplicate(Paragraph newSub, Paragraph lastKept, Func<string, string, double> similarityFunc)
    {
        if (newSub.StartTime.TotalMilliseconds < lastKept.EndTime.TotalMilliseconds + _overlapThreshold.TotalMilliseconds)
        {
            var textSimilarity = similarityFunc(newSub.Text, lastKept.Text);
            if (textSimilarity >= _similarityThreshold)
                return true;
        }
        return false;
    }

    public bool IsDuplicateAtBoundary(Paragraph newSub, Paragraph lastKept, Func<string, string, double> similarityFunc)
    {
        var overlapMs = newSub.StartTime.TotalMilliseconds - lastKept.EndTime.TotalMilliseconds;

        if (overlapMs >= 0)
        {
            if (overlapMs < _boundaryOverlapThreshold.TotalMilliseconds)
            {
                var textSimilarity = similarityFunc(newSub.Text, lastKept.Text);
                return textSimilarity >= _boundarySimilarityThreshold;
            }
            return false;
        }

        var gapMs = Math.Abs(overlapMs);
        if (gapMs < _boundaryOverlapThreshold.TotalMilliseconds)
        {
            var textSimilarity = similarityFunc(newSub.Text, lastKept.Text);
            return textSimilarity >= _boundarySimilarityThreshold;
        }

        return false;
    }

    public static double CalculateSimilarity(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
            return 0;

        var s1 = NormalizeText(text1);
        var s2 = NormalizeText(text2);

        if (s1 == s2)
            return 1.0;

        var distance = LevenshteinDistance(s1, s2);
        var maxLen = Math.Max(s1.Length, s2.Length);
        return maxLen == 0 ? 1.0 : 1.0 - (double)distance / maxLen;
    }

    public static string NormalizeText(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        while (normalized.Contains("  "))
        {
            normalized = normalized.Replace("  ", " ");
        }
        return normalized;
    }

    private static int LevenshteinDistance(string s1, string s2)
    {
        var n = s1.Length;
        var m = s2.Length;
        var d = new int[n + 1, m + 1];

        for (var i = 0; i <= n; i++)
            d[i, 0] = i;
        for (var j = 0; j <= m; j++)
            d[0, j] = j;

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
}