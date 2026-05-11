using Nikse.SubtitleEdit.Core.AudioToText;
using System;
using System.Collections.Generic;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Engines;

public class ForcedAlignerOption
{
    public const string BuiltInChoice = "built-in";
    public const string CanaryCtcChoice = "canary-ctc";
    public const string Qwen3Choice = "qwen3-forced";

    public string BaseDisplay { get; }
    public string Display { get; set; }
    public string Choice { get; }
    public string FileName { get; }
    public string Url { get; }
    public string Size { get; }

    public bool IsBuiltIn => Choice == BuiltInChoice;

    public ForcedAlignerOption(string display, string choice, string fileName, string url, string size)
    {
        BaseDisplay = display;
        Display = display;
        Choice = choice;
        FileName = fileName;
        Url = url;
        Size = size;
    }

    public override string ToString() => Display;

    public WhisperModel ToWhisperModel() => new()
    {
        Name = FileName,
        Size = Size,
        Urls = string.IsNullOrEmpty(Url) ? Array.Empty<string>() : new[] { Url },
    };

    public static ForcedAlignerOption BuiltIn() =>
        new("Built-in aligner", BuiltInChoice, string.Empty, string.Empty, string.Empty);

    public static ForcedAlignerOption CanaryCtc() =>
        new("Canary CTC aligner",
            CanaryCtcChoice,
            "canary-ctc-aligner-q8_0.gguf",
            "https://huggingface.co/cstr/canary-ctc-aligner-GGUF/resolve/main/canary-ctc-aligner-q8_0.gguf",
            "650 MB");

    public static ForcedAlignerOption Qwen3() =>
        new("Qwen3 forced aligner",
            Qwen3Choice,
            "qwen3-forced-aligner-0.6b-q8_0.gguf",
            "https://huggingface.co/cstr/qwen3-forced-aligner-0.6b-GGUF/resolve/main/qwen3-forced-aligner-0.6b-q8_0.gguf",
            "986 MB");

    public static IReadOnlyList<ForcedAlignerOption> All() => new[]
    {
        BuiltIn(),
        CanaryCtc(),
        Qwen3(),
    };

    public static ForcedAlignerOption FromChoice(string? choice)
    {
        return choice switch
        {
            CanaryCtcChoice => CanaryCtc(),
            Qwen3Choice => Qwen3(),
            _ => BuiltIn(),
        };
    }
}
