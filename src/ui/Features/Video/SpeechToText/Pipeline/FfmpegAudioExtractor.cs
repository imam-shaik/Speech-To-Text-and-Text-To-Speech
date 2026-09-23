using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Logic.Config;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class FfmpegAudioExtractor : IAudioExtractor
{
    private readonly string _videoPath;
    private readonly int _audioTrackIndex;
    private readonly ProcessOutputRouter? _router;
    private static readonly Regex DurationRegex = new(@"Duration: (\d{2}):(\d{2}):(\d{2})\.(\d{2})", RegexOptions.Compiled);

    private static readonly string DebugFolder = Path.Combine(
        Se.DataFolder, "Logs", "WhisperDebug");

    public FfmpegAudioExtractor(string videoPath, int audioTrackIndex = 0, ProcessOutputRouter? router = null)
    {
        _videoPath = videoPath;
        _audioTrackIndex = audioTrackIndex;
        _router = router;
    }

    private static void Log(string message)
    {
        try
        {
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [FFMPEG] {message}";
            System.Diagnostics.Debug.WriteLine(logEntry);
            File.AppendAllText(Path.Combine(DebugFolder, "ffmpeg_extractor.log"), logEntry + Environment.NewLine);
        }
        catch
        {
        }
    }

    public async Task ExtractChunkAsync(TimeSpan start, TimeSpan end, string outputPath, CancellationToken ct = default)
    {
        var audioTrackArg = _audioTrackIndex > 0 ? $"-map 0:a:{_audioTrackIndex}" : "-vn";
        var args = $"-y -ss {start.TotalSeconds:0.000} -to {end.TotalSeconds:0.000} -i \"{_videoPath}\" {audioTrackArg} -ar 16000 -ac 1 -ab 32k -af volume=1.75 -hide_banner -loglevel error -f wav \"{outputPath}\"";

        Log($"EXTRACT CHUNK: {start} -> {end}");
        Log($"Expected duration: {(end - start).TotalSeconds:F3}s");
        Log($"Args: {args}");

        using var runner = new AsyncProcessRunner(_router);
        var exitCode = await runner.RunAsync("ffmpeg", args, ct, captureOutput: true);

        Log($"FFmpeg exit: {exitCode}");

        if (File.Exists(outputPath))
        {
            var fi = new FileInfo(outputPath);
            Log($"WAV size: {fi.Length:N0} bytes");

            try
            {
                var probeArgs = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{outputPath}\"";
                using var probeRunner = new AsyncProcessRunner(_router);
                var probeExitCode = probeRunner.RunAsync("ffprobe", probeArgs, ct, captureOutput: true).Result;

                if (probeExitCode == 0)
                {
                    var output = probeRunner.ErrorOutput.Trim();
                    if (double.TryParse(output, out var durationSecs))
                    {
                        var actualDuration = TimeSpan.FromSeconds(durationSecs);
                        var expectedDuration = end - start;
                        var durationDiff = actualDuration - expectedDuration;
                        Log($"WAV actual duration: {actualDuration}");
                        Log($"Duration diff: {durationDiff.TotalSeconds:F3}s ({(durationDiff / expectedDuration * 100):F1}%)");
                    }
                }
                else
                {
                    Log($"ffprobe failed with exit code {probeExitCode}");
                }
            }
            catch (Exception ex)
            {
                Log($"Could not read WAV duration: {ex.Message}");
            }
        }
        else
        {
            Log("ERROR: WAV file was NOT created!");
        }

        if (!string.IsNullOrWhiteSpace(runner.ErrorOutput))
        {
            Log($"Stderr: {runner.ErrorOutput}");
        }

        if (exitCode != 0 && !ct.IsCancellationRequested)
        {
            var error = runner.ErrorOutput;
            throw new InvalidOperationException($"FFmpeg failed with exit code {exitCode}: {error}");
        }
    }

    public async Task<TimeSpan> GetVideoDurationAsync(CancellationToken ct = default)
    {
        var args = $"-i \"{_videoPath}\" -hide_banner -f null -";

        using var runner = new AsyncProcessRunner(_router);
        await runner.RunAsync("ffmpeg", args, ct, captureOutput: true);

        var match = DurationRegex.Match(runner.ErrorOutput);
        if (match.Success)
        {
            var hours = int.Parse(match.Groups[1].Value);
            var minutes = int.Parse(match.Groups[2].Value);
            var seconds = int.Parse(match.Groups[3].Value);
            var centiseconds = int.Parse(match.Groups[4].Value);
            return new TimeSpan(0, hours, minutes, seconds, centiseconds * 10);
        }

        return TimeSpan.Zero;
    }
}