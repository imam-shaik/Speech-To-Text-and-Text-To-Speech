using System;
using System.Collections.Generic;
using System.Linq;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public static class BilingualMuxer
{
    public static Subtitle Combine(
        Subtitle original,
        Subtitle translation,
        string separator = "\n")
    {
        var result = new Subtitle();
        var count = Math.Min(original.Paragraphs.Count, translation.Paragraphs.Count);

        for (var i = 0; i < count; i++)
        {
            var orig = original.Paragraphs[i];
            var trans = translation.Paragraphs[i];

            var combinedText = string.Join(separator, orig.Text.Trim(), trans.Text.Trim());

            var p = new Paragraph(
                combinedText,
                orig.StartTime.TotalMilliseconds,
                orig.EndTime.TotalMilliseconds);

            result.Paragraphs.Add(p);
        }

        if (original.Paragraphs.Count != translation.Paragraphs.Count)
        {
            var extraOriginal = original.Paragraphs.Count - count;
            var extraTranslation = translation.Paragraphs.Count - count;

            if (extraOriginal > 0)
            {
                for (var i = count; i < original.Paragraphs.Count; i++)
                {
                    var orig = original.Paragraphs[i];
                    var p = new Paragraph(orig.Text, orig.StartTime.TotalMilliseconds, orig.EndTime.TotalMilliseconds);
                    result.Paragraphs.Add(p);
                }
            }

            if (extraTranslation > 0)
            {
                for (var i = count; i < translation.Paragraphs.Count; i++)
                {
                    var trans = translation.Paragraphs[i];
                    var p = new Paragraph(trans.Text, trans.StartTime.TotalMilliseconds, trans.EndTime.TotalMilliseconds);
                    result.Paragraphs.Add(p);
                }
            }
        }

        return result;
    }

    public static Subtitle CombineSimple(
        Subtitle original,
        Subtitle translation,
        string origPrefix = "",
        string transPrefix = "",
        string separator = "\n")
    {
        var result = new Subtitle();
        var count = Math.Min(original.Paragraphs.Count, translation.Paragraphs.Count);

        for (var i = 0; i < count; i++)
        {
            var orig = original.Paragraphs[i];
            var trans = translation.Paragraphs[i];

            var origText = string.IsNullOrEmpty(origPrefix) ? orig.Text : $"{origPrefix}{orig.Text}";
            var transText = string.IsNullOrEmpty(transPrefix) ? trans.Text : $"{transPrefix}{trans.Text}";

            var combinedText = string.Join(separator, origText, transText);

            var p = new Paragraph(
                combinedText,
                orig.StartTime.TotalMilliseconds,
                orig.EndTime.TotalMilliseconds);

            result.Paragraphs.Add(p);
        }

        return result;
    }
}