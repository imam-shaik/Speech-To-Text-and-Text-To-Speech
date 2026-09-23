# Real Speech Certification Test for Chunked Pipeline Subtitle Loss
# This script tests subtitle generation with real audio using Legacy vs Chunked modes

param(
    [string]$AudioPath = "src\ui\bin\Debug\net10.0\Waveforms\00465cab586009b6.wav",
    [string]$WhisperExe = "whisper",
    [string]$ModelPath = "",
    [string]$OutputFolder = "$env:TEMP\whisper_cert_test"
)

$ErrorActionPreference = "Continue"

function Get-WavDuration {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $sampleRate = [BitConverter]::ToInt32($bytes[24..27], 0)
    $channels = [BitConverter]::ToInt16($bytes[22..23], 0)
    $bitsPerSample = [BitConverter]::ToInt16($bytes[34..35], 0)
    $dataSize = [BitConverter]::ToInt32($bytes[40..43], 0)
    $durationSec = $dataSize / ($sampleRate * $channels * $bitsPerSample / 8)
    return [math]::Round($durationSec, 1)
}

function Count-Subtitles {
    param([string]$SrtPath)
    if (-not (Test-Path $SrtPath)) { return 0 }
    $content = Get-Content $SrtPath -Raw
    $matches = [regex]::Matches($content, '^\d+$', [System.Text.RegularExpressions.RegexOptions]::Multiline)
    return $matches.Count
}

function Count-Words {
    param([string]$SrtPath)
    if (-not (Test-Path $SrtPath)) { return 0 }
    $content = Get-Content $SrtPath -Raw
    $text = [regex]::Replace($content, '(?m)^\d+\r?\n.*?-->.+?\r?\n', '')
    $text = [regex]::Replace($text, '\r?\n', ' ')
    ($text -split '\s+').Where({$_ -ne ''}).Count
}

function Run-Whisper {
    param(
        [string]$Exe,
        [string]$InputWav,
        [string]$OutputSrt,
        [string]$Model,
        [string]$Language = "en",
        [string]$ExtraArgs = ""
    )

    $cmd = "--language $language --model `"$model`" --output-srt $ExtraArgs `"$InputWav`""
    Write-Host "    CMD: $cmd"

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Exe
    $psi.Arguments = $cmd
    $psi.UseShellExecute = $false
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardOutput = $true

    $proc = [System.Diagnostics.Process]::Start($psi)
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()

    $tempSrt = $InputWav + ".srt"
    if (Test-Path $tempSrt) {
        Copy-Item $tempSrt $OutputSrt -Force
        Remove-Item $tempSrt -Force
    }

    return @{
        ExitCode = $proc.ExitCode
        Stderr = $stderr
    }
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "REAL SPEECH CERTIFICATION TEST" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Setup
if (-not (Test-Path $AudioPath)) {
    Write-Host "ERROR: Audio file not found: $AudioPath" -ForegroundColor Red
    Write-Host "Searching for WAV files..."
    Get-ChildItem "src\ui\bin\Debug\net10.0\Waveforms" -Filter "*.wav" | Select-Object -First 5 | ForEach-Object {
        Write-Host "  Found: $($_.FullName)"
    }
    exit 1
}

$audioFile = Get-Item $AudioPath
$durationSec = Get-WavDuration $AudioPath

Write-Host "Audio Used: $AudioPath"
Write-Host "Duration: $durationSec sec"
Write-Host "File Size: $([math]::Round($audioFile.Length/1KB, 1)) KB"
Write-Host "Real Audio: YES"
Write-Host ""

# Create output folder
$testOutput = Join-Path $OutputFolder "test_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null

# Check for whisper
$whisperCmd = Get-Command whisper -ErrorAction SilentlyContinue
if (-not $whisperCmd) {
    Write-Host "ERROR: whisper.exe not found in PATH" -ForegroundColor Red
    Write-Host "Please ensure whisper is installed and in PATH"
    exit 1
}
Write-Host "Whisper found: $($whisperCmd.Source)"
Write-Host ""

# Check model
if ([string]::IsNullOrEmpty($ModelPath)) {
    $defaultModels = @(
        "$env:USERPROFILE\.cache\whisper\base.en.pt",
        "$env:USERPROFILE\.cache\whisper\small.en.pt"
    )
    foreach ($m in $defaultModels) {
        if (Test-Path $m) {
            $ModelPath = $m
            break
        }
    }
}

if ([string]::IsNullOrEmpty($ModelPath) -or -not (Test-Path $ModelPath)) {
    Write-Host "WARNING: Model path not specified or not found: $ModelPath" -ForegroundColor Yellow
    Write-Host "Test will run with whatever model is configured in whisper"
    $ModelPath = "(configured default)"
}
Write-Host "Model: $ModelPath"
Write-Host ""

# ========================================
# TEST 1: LEGACY MODE (Full Audio)
# ========================================
Write-Host "========================================" -ForegroundColor Green
Write-Host "LEGACY MODE (Full Audio)" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

$legacySrt = Join-Path $testOutput "legacy_full.srt"
$legacyRaw = Join-Path $testOutput "legacy_raw.srt"

Write-Host "Running whisper on full audio..."
$result = Run-Whisper -Exe $WhisperExe -InputWav $AudioPath -OutputSrt $legacySrt -Model $ModelPath

if ($result.ExitCode -ne 0) {
    Write-Host "  WARNING: Whisper exited with code $($result.ExitCode)" -ForegroundColor Yellow
    if ($result.Stderr) {
        Write-Host "  Stderr: $($result.Stderr.Substring(0, [Math]::Min(200, $result.Stderr.Length)))"
    }
}

$legacySubs = Count-Subtitles $legacySrt
$legacyWords = Count-Words $legacySrt
Write-Host "Legacy Subtitles: $legacySubs"
Write-Host "Legacy Words: $legacyWords"
Write-Host ""

# ========================================
# TEST 2: CHUNKED MODE (2 x 30s chunks)
# ========================================
Write-Host "========================================" -ForegroundColor Yellow
Write-Host "CHUNKED MODE (2 x 30s chunks)" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow

$chunkSize = 30
$numChunks = [Math]::Ceiling($durationSec / $chunkSize)
$chunkSrtFiles = @()

for ($i = 0; $i -lt $numChunks; $i++) {
    $startSec = $i * $chunkSize
    $endSec = [Math]::Min(($i + 1) * $chunkSize, $durationSec)
    $chunkWav = Join-Path $testOutput "chunk_${i}_${startSec}_${endSec}.wav"
    $chunkSrt = Join-Path $testOutput "chunk_${i}_${startSec}_${endSec}.srt"

    Write-Host "Chunk $($i+1): ${startSec}s - ${endSec}s"

    # Extract chunk audio using ffmpeg
    $ffmpegArgs = "-y -ss $startSec -to $endSec -i `"$AudioPath`" -vn -ar 16000 -ac 1 -ab 32k -af volume=1.75 -hide_banner -loglevel error -f wav `"$chunkWav`""
    Write-Host "  FFmpeg: $ffmpegArgs"

    $ffmpegPsi = New-Object System.Diagnostics.ProcessStartInfo
    $ffmpegPsi.FileName = "ffmpeg"
    $ffmpegPsi.Arguments = $ffmpegArgs
    $ffmpegPsi.UseShellExecute = $false
    $ffmpegPsi.RedirectStandardError = $true
    $ffmpegPsi.RedirectStandardOutput = $true
    $ffmpegProc = [System.Diagnostics.Process]::Start($ffmpegPsi)
    $ffmpegStderr = $ffmpegProc.StandardError.ReadToEnd()
    $ffmpegProc.WaitForExit()

    if ($ffmpegProc.ExitCode -ne 0) {
        Write-Host "  ERROR: FFmpeg failed with $($ffmpegProc.ExitCode)" -ForegroundColor Red
        Write-Host "  Stderr: $ffmpegStderr"
        continue
    }

    # Run whisper on chunk
    Write-Host "  Running whisper..."
    $chunkResult = Run-Whisper -Exe $WhisperExe -InputWav $chunkWav -OutputSrt $chunkSrt -Model $ModelPath
    $chunkSubs = Count-Subtitles $chunkSrt
    Write-Host "  Chunk $($i+1) Raw Whisper: $chunkSubs subs"

    $chunkSrtFiles += @{Path=$chunkSrt; Start=$startSec; End=$endSec; Subs=$chunkSubs}

    # Cleanup chunk WAV
    Remove-Item $chunkWav -Force -ErrorAction SilentlyContinue
}

# Merge chunks
Write-Host ""
Write-Host "Merging chunks..."

$mergedSrt = Join-Path $testOutput "merged_chunks.srt"
$mergedContent = ""

$subtitleIndex = 1
for ($i = 0; $i -lt $chunkSrtFiles.Count; $i++) {
    $chunk = $chunkSrtFiles[$i]
    $offsetMs = $chunk.Start * 1000

    if (Test-Path $chunk.Path) {
        $chunkContent = Get-Content $chunk.Path -Raw
        $entries = $chunkContent -split "`n`n"

        foreach ($entry in $entries) {
            if ($entry -match '(\d+)\s*\n(\d{2}:\d{2}:\d{2},\d{3})\s*-->\s*(\d{2}:\d{2}:\d{2},\d{3})\s*\n(.+)') {
                $index = $matches[1]
                $startTime = $matches[2]
                $endTime = $matches[3]
                $text = $matches[4].Trim()

                # Parse and adjust timestamps
                $startParts = $startTime -replace ',', '.' -split ':'
                $startMs = [int]$startParts[0] * 3600000 + [int]$startParts[1] * 60000 + [int]([float]$startParts[2] * 1000)
                $endParts = $endTime -replace ',', '.' -split ':'
                $endMs = [int]$endParts[0] * 3600000 + [int]$endParts[1] * 60000 + [int]([float]$endParts[2] * 1000)

                $newStartMs = $startMs + $offsetMs
                $newEndMs = $endMs + $offsetMs

                $newStartTime = "{0:00}:{1:00}:{2:00},{3:000}" -f [int]($newStartMs / 3600000), [int](($newStartMs % 3600000) / 60000), [int](($newStartMs % 60000) / 1000), [int]($newStartMs % 1000)
                $newEndTime = "{0:00}:{1:00}:{2:00},{3:000}" -f [int]($newEndMs / 3600000), [int](($newEndMs % 3600000) / 60000), [int](($newEndMs % 60000) / 1000), [int]($newEndMs % 1000)

                $mergedContent += "$subtitleIndex`n$newStartTime --> $newEndTime`n$text`n`n"
                $subtitleIndex++
            }
        }
    }
}

$mergedContent | Set-Content $mergedSrt -Encoding UTF8

$mergedSubs = Count-Subtitles $mergedSrt
$mergedWords = Count-Words $mergedSrt
Write-Host "Merged: $mergedSubs subtitles, $mergedWords words"
Write-Host ""

# ========================================
# COMPARE RESULTS
# ========================================
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RESULTS COMPARISON" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

Write-Host "LEGACY MODE:"
Write-Host "  Subtitles: $legacySubs"
Write-Host "  Words: $legacyWords"
Write-Host ""
Write-Host "CHUNKED MODE (2 x 30s):"
Write-Host "  Chunk outputs:"
foreach ($chunk in $chunkSrtFiles) {
    Write-Host "    Chunk [$($chunk.Start)s - $($chunk.End)s]: $($chunk.Subs) subs"
}
Write-Host "  Raw total: $($chunkSrtFiles | ForEach-Object { $_.Subs } | Measure-Object -Sum).Sum subs"
Write-Host "  After merge: $mergedSubs subtitles"
Write-Host "  Words: $mergedWords"
Write-Host ""

$diffSubs = $legacySubs - $mergedSubs
$diffWords = $legacyWords - $mergedWords
$diffPercent = if ($legacySubs -gt 0) { [Math]::Abs($diffSubs / $legacySubs * 100) } else { 0 }

Write-Host "DIFFERENCE:"
Write-Host "  Subtitles: $diffSubs ($($diffPercent.ToString('F1'))%)" -ForegroundColor $(if ($diffSubs -ne 0) { "Yellow" } else { "Green" })
Write-Host "  Words: $diffWords" -ForegroundColor $(if ($diffWords -ne 0) { "Yellow" } else { "Green" })

if ($diffSubs -ne 0) {
    Write-Host ""
    Write-Host "POTENTIAL ROOT CAUSES:" -ForegroundColor Yellow
    Write-Host "  1. Whisper produces different output for shorter audio segments"
    Write-Host "  2. Chunk boundaries cause subtitle count variance"
    Write-Host "  3. FFmpeg extraction at boundaries loses audio context"
    Write-Host "  4. SRT parsing/merging logic has issues"
}

# ========================================
# SAVE DETAILED REPORT
# ========================================
$reportPath = Join-Path $testOutput "CERTIFICATION_REPORT.txt"

$report = @"
REAL SPEECH CERTIFICATION TEST
===============================

Audio Used: $AudioPath
Duration: $durationSec sec
Real Audio: YES

LEGACY MODE
-----------
Subtitles: $legacySubs
Words: $legacyWords

CHUNKED MODE (2 x 30s)
--------------------
"@

foreach ($chunk in $chunkSrtFiles) {
    $report += "Chunk [$($chunk.Start)s - $($chunk.End)s]: $($chunk.Subs) subs`n"
}

$rawTotal = ($chunkSrtFiles | ForEach-Object { $_.Subs } | Measure-Object -Sum).Sum
$report += @"
Raw Whisper total: $rawTotal subs
Merged: $mergedSubs subs
Final: $mergedSubs subs

COMMAND COMPARISON
------------------
Legacy: whisper --language en --model "$ModelPath" --output-srt "$AudioPath"
"@

for ($i = 0; $i -lt $chunkSrtFiles.Count; $i++) {
    $chunk = $chunkSrtFiles[$i]
    $report += "Chunk$($i+1): whisper --language en --model "$ModelPath" --output-srt chunk_$i.wav (offset: $($chunk.Start)s)`n"
}

$report += @"

DIFFERENCE
----------
Subtitles: $diffSubs ($($diffPercent.ToString('F1'))%)
Words: $diffWords

ROOT CAUSE: $(if ($diffSubs -ne 0) { "See investigation needed" } else { "None - outputs match" })

CHUNK SIZE TEST
--------------
Full (no chunking): $legacySubs subs

"@

$report | Set-Content $reportPath -Encoding UTF8

Write-Host ""
Write-Host "Report saved to: $reportPath" -ForegroundColor Green
Write-Host "Test output folder: $testOutput"
Write-Host "========================================"