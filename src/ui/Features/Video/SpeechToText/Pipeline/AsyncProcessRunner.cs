using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class AsyncProcessRunner : IDisposable
{
    private Process? _process;
    private CancellationTokenSource? _cts;
    private readonly StringBuilder _errorOutput;
    private readonly ProcessOutputRouter? _router;
    private bool _disposed;

    public int ExitCode { get; private set; }
    public bool HasExited => _process?.HasExited ?? true;
    public string ErrorOutput => _errorOutput.ToString();

    public AsyncProcessRunner(ProcessOutputRouter? router = null)
    {
        _errorOutput = new StringBuilder();
        _router = router;
    }

    public async Task<int> RunAsync(
        string fileName,
        string arguments,
        CancellationToken ct = default,
        bool captureOutput = false)
    {
        _errorOutput.Clear();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var redirectErrors = captureOutput || _router != null;
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = redirectErrors,
            RedirectStandardOutput = false,
            StandardErrorEncoding = redirectErrors ? Encoding.UTF8 : null,
            StandardOutputEncoding = null,
        };

        _process = new Process { StartInfo = startInfo };

        if (captureOutput || _router != null)
        {
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    _errorOutput.AppendLine(e.Data);
                    _router?.Route(e.Data);
                }
            };
        }

        try
        {
            _process.Start();
        }
        catch (Exception ex)
        {
            _errorOutput.AppendLine($"Failed to start process: {ex.Message}");
            return -1;
        }

        if (_process == null || _process.HasExited)
        {
            _errorOutput.AppendLine("Process failed to start or exited immediately");
            return _process?.ExitCode ?? -1;
        }

        if (captureOutput || _router != null)
        {
            _process.BeginErrorReadLine();
        }

        try
        {
            await _process.WaitForExitAsync(_cts.Token);
            ExitCode = _process?.ExitCode ?? -1;
        }
        catch (OperationCanceledException)
        {
            if (_process != null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
                catch
                {
                }
            }

            throw;
        }

        return ExitCode;
    }

    public void Cancel()
    {
        _cts?.Cancel();

        if (_process != null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Cancel();
        _cts?.Dispose();

        if (_process != null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
            catch
            {
            }
            finally
            {
                _process.Dispose();
            }
        }

        _disposed = true;
    }
}

public static class ProcessRunner
{
    public static async Task<int> RunAsync(
        string fileName,
        string arguments,
        CancellationToken ct = default,
        bool captureErrors = false,
        Action<string>? onError = null,
        int timeoutMs = 0)
    {
        using var runner = new AsyncProcessRunner();

        var timeoutCts = timeoutMs > 0
            ? new CancellationTokenSource(timeoutMs)
            : null;

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            ct,
            timeoutCts?.Token ?? CancellationToken.None);

        try
        {
            var exitCode = await runner.RunAsync(fileName, arguments, linkedCts.Token, captureErrors);

            if (captureErrors && onError != null)
            {
                var errors = runner.ErrorOutput;
                if (!string.IsNullOrEmpty(errors))
                    onError(errors);
            }

            return exitCode;
        }
        finally
        {
            timeoutCts?.Dispose();
            linkedCts.Dispose();
        }
    }

    public static async Task<string?> GetFFmpegDurationAsync(string videoPath, CancellationToken ct = default)
    {
        var args = $"-i \"{videoPath}\" -hide_banner -f null -";

        try
        {
            using var runner = new AsyncProcessRunner();
            await runner.RunAsync("ffmpeg", args, ct, captureOutput: true);

            var errorOutput = runner.ErrorOutput;
            var match = System.Text.RegularExpressions.Regex.Match(
                errorOutput,
                @"Duration: (\d{2}):(\d{2}):(\d{2})\.(\d{2})");

            if (match.Success)
            {
                var hours = int.Parse(match.Groups[1].Value);
                var minutes = int.Parse(match.Groups[2].Value);
                var seconds = int.Parse(match.Groups[3].Value);
                var centiseconds = int.Parse(match.Groups[4].Value);
                return new TimeSpan(0, hours, minutes, seconds, centiseconds * 10).ToString();
            }
        }
        catch
        {
        }

        return null;
    }

    public static async Task<string?> GetFFmpegDurationTimeSpanAsync(string videoPath, CancellationToken ct = default)
    {
        var duration = await GetFFmpegDurationAsync(videoPath, ct);
        return duration;
    }
}