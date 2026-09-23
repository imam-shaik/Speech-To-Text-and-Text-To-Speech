# Certification Trace Test Script
param(
    [string]$WavFile = "C:\Users\IMAM\Desktop\Final Testing\subtitleedit-fresh\src\ui\bin\Debug\net10.0\Waveforms\00465cab586009b6.wav",
    [string]$WhisperExe = "C:\Users\IMAM\Desktop\Final Testing\subtitleedit-fresh\src\ui\bin\Debug\net10.0\SpeechToText\Cpp\whisper-cli.exe",
    [string]$FfmpegExe = "C:\Users\IMAM\Desktop\Final Testing\subtitleedit-fresh\src\ui\bin\Debug\net10.0\ffmpeg\ffmpeg.exe",
    [string]$ModelPath = "C:\Users\IMAM\Desktop\Final Testing\subtitleedit-fresh\src\ui\bin\Debug\net10.0\SpeechToText\Cpp\Models\base.en-q5_1.bin",
    [string]$TempPath = "C:\Users\IMAM\AppData\Local\Temp\cert_trace"
)

$ErrorActionPreference = "Continue"

# Setup
if (!(Test-Path $TempPath)) { New-Item -ItemType Directory -Path $TempPath | Out-Null }
$report = @()

function Get-WavDuration {
    param([string]$Path)
    # Use ffmpeg to probe duration
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FfmpegExe
    $psi.Arguments = "-i `"$Path`" -hide_banner -f null -"
    $psi.UseShellExecute = $false
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardOutput = $true
    $proc = [System.Diagnostics.Process]::Start($psi)
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()

    if ($stderr -match "Duration: (\d{2}):(\d{2}):(\d{2})\.(\d{2})") {
        $h = [int]$Matches[1]
        $m = [int]$Matches[2]
        $s = [int]$Matches[3]
        $cs = [int]$Matches[4]
        return [TimeSpan]::new($h, $m, $s, $cs * 10)
    }
    return [TimeSpan]::Zero
}

function Get-WavFileInfo {
    param([string]$Path)
    $fi = Get-Item $Path
    $duration = Get-WavDuration $Path
    return @{
        Path = $Path
        SizeBytes = $fi.Length
        Duration = $duration
        DurationSec = $duration.TotalSeconds
    }
}

function Invoke-Whisper {
    param(
        [string]$AudioPath,
        [string]$OutputSrt,
        [string]$Offset = "0"
    )
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $WhisperExe
    $psi.Arguments = "--language en --model `"$ModelPath`" --output-srt `"$AudioPath`""
    $psi.UseShellExecute = $false
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardOutput = $true
    $proc = [System.Diagnostics.Process]::Start($psi)
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()

    $tempSrt = $AudioPath + ".srt"
    if (Test-Path $tempSrt) {
        if (!(Test-Path $OutputSrt)) {
            Move-Item $tempSrt $OutputSrt -Force
        } else {
            Remove-Item $tempSrt -Force
        }
    }
    return @{
        ExitCode = $proc.ExitCode
        Stderr = $stderr
    }
}

function Get-SrtInfo {
    param([string]$Path)
    if (!(Test-Path $Path)) { return @{ Subtitles = 0; Words = 0; FirstSub = ""; LastSub = "" } }

    $content = Get-Content $Path -Raw
    $lines = $content -split "`n"

    $subtitleCount = 0
    $wordCount = 0
    $texts = @()

    $inSubtitle = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i].Trim()
        if ($line -match "^\d+$") {
            $inSubtitle = $true
            $subtitleCount++
        } elseif ($inSubtitle -and $line -match "-->") {
            # timestamp line
        } elseif ($inSubtitle -and $line -ne "" -and $line -notmatch "^\d+$") {
            $wordCount += ($line -split "\s+").Count
            $texts += $line
        } elseif ($inSubtitle -and $line -eq "") {
            $inSubtitle = $false
        }
    }

    return @{
        Subtitles = $subtitleCount
        Words = $wordCount
        FirstSub = if ($texts.Count -gt 0) { $texts[0] } else { "" }
        LastSub = if ($texts.Count -gt 1) { $texts[-1] } else { "" }
    }
}

function Invoke-FfmpegExtract {
    param(
        [string]$InputPath,
        [string]$OutputPath,
        [double]$StartSec,
        [double]$EndSec,
        [bool]$WithSeek = $false
    )
    $args = if ($WithSeek) {
        "-y -ss {0:N3} -to {1:N3} -i `"$InputPath`" -vn -ar 16000 -ac 1 -ab 32k -af volume=1.75 -hide_banner -loglevel error -f wav `"$OutputPath`""
    } else {
        "-y -i `"$InputPath`" -vn -ar 16000 -ac 1 -ab 32k -af volume=1.75 -hide_banner -loglevel error -f wav `"$OutputPath`""
    }
    $args = $args -f $StartSec, $EndSec

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FfmpegExe
    $psi.Arguments = $args
    $psi.UseShellExecute = $false
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardOutput = $true
    $proc = [System.Diagnostics.Process]::Start($psi)
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()

    return @{
        ExitCode = $proc.ExitCode
        Stderr = $stderr
    }
}

# ========== STAGE 1: INPUT ==========
Write-Host "=" * 70
Write-Host "STAGE 1 - INPUT"
Write-Host "=" * 70

$wavInfo = Get-WavFileInfo $WavFile
Write-Host "File Path: $($wavInfo.Path)"
Write-Host "File Size: $($wavInfo.SizeBytes) bytes ($([Math]::Round($wavInfo.SizeBytes/1MB, 2)) MB)"
Write-Host "Duration: $($wavInfo.DurationSec) seconds ($([Math]::Round($wavInfo.DurationSec/60, 2)) minutes)"
Write-Host "Expected Duration: 46.3 seconds (as per user)"

$report += "STAGE 1 - INPUT"
$report += "  File: $($wavInfo.Path)"
$report += "  Size: $($wavInfo.SizeBytes) bytes"
$report += "  Duration: $($wavInfo.DurationSec) sec"
Write-Host ""

# ========== STAGE 2: LEGACY PIPELINE ==========
Write-Host "=" * 70
Write-Host "STAGE 2 - LEGACY PIPELINE"
Write-Host "=" * 70

$legacyWav = Join-Path $TempPath "legacy_full.wav"
$legacySrt = Join-Path $TempPath "legacy_output.srt"
$legacyRawSrt = Join-Path $TempPath "legacy_raw.srt"

# FFmpeg extract (no seeking - extracts full audio)
Write-Host "FFmpeg Command: $FfmpegExe -y -i `"$WavFile`" -vn -ar 16000 -ac 1 -ab 32k -af volume=1.75 -hide_banner -loglevel error -f wav `"$legacyWav`""
$ffResult = Invoke-FfmpegExtract -InputPath $WavFile -OutputPath $legacyWav -StartSec 0 -EndSec $wavInfo.DurationSec -WithSeek $false
Write-Host "FFmpeg Exit: $($ffResult.ExitCode)"
if ($ffResult.Stderr) { Write-Host "FFmpeg Stderr: $($ffResult.Stderr)" }

# Whisper on full audio
Write-Host ""
Write-Host "Whisper Command: $WhisperExe --language en --model `"$ModelPath`" --output-srt `"$legacyWav`""
$whisperResult = Invoke-Whisper -AudioPath $legacyWav -OutputSrt $legacySrt
Write-Host "Whisper Exit: $($whisperResult.ExitCode)"
if ($whisperResult.Stderr) { Write-Host "Whisper Stderr: $($whisperResult.Stderr)" }

# Copy raw SRT for inspection
if (Test-Path $legacySrt) {
    Copy-Item $legacySrt $legacyRawSrt -Force
}

$legacyInfo = Get-SrtInfo $legacySrt
Write-Host ""
Write-Host "Legacy Results:"
Write-Host "  Raw SRT File: $legacyRawSrt"
Write-Host "  Subtitles: $($legacyInfo.Subtitles)"
Write-Host "  Words: $($legacyInfo.Words)"
Write-Host "  First Subtitle: `"$($legacyInfo.FirstSub)`""
Write-Host "  Last Subtitle: `"$($legacyInfo.LastSub)`""

$legacyRawCount = $legacyInfo.Subtitles
$legacyRawWords = $legacyInfo.Words

Write-Host ""
$report += "STAGE 2 - LEGACY PIPELINE"
$report += "  FFmpeg: -y -i `"$WavFile`" -vn -ar 16000 -ac 1 -ab 32k -af volume=1.75 -f wav `"$legacyWav`""
$report += "  Whisper: --language en --model `"$ModelPath`" --output-srt `"$legacyWav`""
$report += "  Raw Whisper: $($legacyRawCount) subs, $($legacyRawWords) words"
Write-Host ""

# ========== STAGE 3: CHUNKED PIPELINE ==========
Write-Host "=" * 70
Write-Host "STAGE 3 - CHUNKED PIPELINE"
Write-Host "=" * 70

# Calculate chunks based on audio duration
# Min chunk: 20s, Max chunk: 60s (SilenceAwareChunker defaults)
$audioDuration = $wavInfo.DurationSec
$minChunk = 20
$maxChunk = 60

$chunks = @()
$currentPos = 0
$chunkNum = 1

while ($currentPos -lt $audioDuration) {
    $chunkEnd = [Math]::Min($currentPos + $maxChunk, $audioDuration)

    # If chunk would be too small, extend to max
    if (($chunkEnd - $currentPos) -lt $minChunk -and $chunkNum -gt 1) {
        # Merge with previous
        $chunks[-1].End = $audioDuration
        break
    }

    $chunks += @{
        Number = $chunkNum
        Start = $currentPos
        End = $chunkEnd
        Duration = $chunkEnd - $currentPos
    }

    $currentPos = $chunkEnd
    $chunkNum++
}

Write-Host "Audio Duration: $audioDuration sec"
Write-Host "Chunk Config: Min=$minChunk, Max=$maxChunk"
Write-Host "Chunks Created: $($chunks.Count)"
foreach ($c in $chunks) {
    Write-Host "  Chunk $($c.Number): $($c.Start)s - $($c.End)s (duration: $([Math]::Round($c.Duration, 3))s)"
}
Write-Host ""

$chunkRawCounts = @()
$chunkRawWords = @()
$chunkParserCounts = @()
$chunkParserWords = @()
$chunkDupRejected = @()
$chunkWrittenCounts = @()

$allChunkSubs = @()

for ($i = 0; $i -lt $chunks.Count; $i++) {
    $chunk = $chunks[$i]
    $chunkNum = $i + 1

    Write-Host "Chunk $chunkNum"
    Write-Host "-------"

    $chunkWav = Join-Path $TempPath "chunk_${chunkNum}.wav"
    $chunkSrt = Join-Path $TempPath "chunk_${chunkNum}.srt"
    $chunkRawSrt = Join-Path $TempPath "chunk_${chunkNum}_raw.srt"

    # FFmpeg extract with seeking (as in PipelineController)
    $ffCmd = "-y -ss {0:N3} -to {1:N3} -i `"$WavFile`" -vn -ar 16000 -ac 1 -ab 32k -af volume=1.75 -hide_banner -loglevel error -f wav `"$chunkWav`""
    $ffCmdFormatted = $ffCmd -f $chunk.Start, $chunk.End
    Write-Host "FFmpeg Command: $FfmpegExe $ffCmdFormatted"
    $ffResult = Invoke-FfmpegExtract -InputPath $WavFile -OutputPath $chunkWav -StartSec $chunk.Start -EndSec $chunk.End -WithSeek $true
    Write-Host "FFmpeg Exit: $($ffResult.ExitCode)"

    # Verify extracted chunk duration
    $chunkDuration = Get-WavDuration $chunkWav
    Write-Host "Expected Duration: $([Math]::Round($chunk.End - $chunk.Start, 3))s"
    Write-Host "Actual WAV Duration: $([Math]::Round($chunkDuration.TotalSeconds, 3))s"

    # Whisper on chunk
    $whisperCmd = "--language en --model `"$ModelPath`" --output-srt `"$chunkWav`""
    Write-Host "Whisper Command: $WhisperExe $whisperCmd"
    $whisperResult = Invoke-Whisper -AudioPath $chunkWav -OutputSrt $chunkSrt
    Write-Host "Whisper Exit: $($whisperResult.ExitCode)"

    # Copy raw SRT
    if (Test-Path $chunkSrt) {
        Copy-Item $chunkSrt $chunkRawSrt -Force
    }

    # Parse SRT to get subtitle info
    $rawInfo = Get-SrtInfo $chunkSrt
    Write-Host "Raw Whisper Subtitles: $($rawInfo.Subtitles)"
    Write-Host "Raw Whisper Words: $($rawInfo.Words)"

    $chunkRawCounts += $rawInfo.Subtitles
    $chunkRawWords += $rawInfo.Words

    # Now we need to add chunk offset to timestamps and add to all subs
    if (Test-Path $chunkSrt) {
        $content = Get-Content $chunkSrt -Raw
        $lines = $content -split "`n"

        $inSubtitle = $false
        $subNum = 0
        for ($j = 0; $j -lt $lines.Count; $j++) {
            $line = $lines[$j].Trim()
            if ($line -match "^\d+$") {
                $inSubtitle = $true
                $subNum = [int]$line
            } elseif ($inSubtitle -and $line -match "-->") {
                # Parse timestamp and add offset
                if ($line -match "(\d{2}:\d{2}:\d{2},\d{3})\s*-->\s*(\d{2}:\d{2}:\d{2},\d{3})") {
                    $startTs = $Matches[1]
                    $endTs = $Matches[2]
                    # Convert to milliseconds and add offset
                    $startMs = [TimeSpan]::ParseExact($startTs, "hh\:mm\:ss\,fff", $null).TotalMilliseconds + ($chunk.Start * 1000)
                    $endMs = [TimeSpan]::ParseExact($endTs, "hh\:mm\:ss\,fff", $null).TotalMilliseconds + ($chunk.Start * 1000)
                    $allChunkSubs += @{
                        StartMs = $startMs
                        EndMs = $endMs
                        StartTime = [TimeSpan]::FromMilliseconds($startMs)
                        EndTime = [TimeSpan]::FromMilliseconds($endMs)
                    }
                }
            }
        }
    }

    Write-Host ""
}

Write-Host "Total Raw Whisper from all chunks: $($chunkRawCounts -join ", ") = $(($chunkRawCounts | Measure-Object -Sum).Sum) subs, $(($chunkRawWords | Measure-Object -Sum).Sum) words"
$chunkTotalRaw = ($chunkRawCounts | Measure-Object -Sum).Sum
$chunkTotalRawWords = ($chunkRawWords | Measure-Object -Sum).Sum

Write-Host ""
$report += "STAGE 3 - CHUNKED PIPELINE"
$report += "  Chunks: $($chunks.Count)"

for ($i = 0; $i -lt $chunks.Count; $i++) {
    $c = $chunks[$i]
    $report += "  Chunk $($c.Number): $($c.Start)s - $($c.End)s, Raw: $($chunkRawCounts[$i]) subs, $($chunkRawWords[$i]) words"
}
$report += "  Chunk Total Raw: $chunkTotalRaw subs, $chunkTotalRawWords words"

# ========== MERGE AND DEDUPLICATE ==========
Write-Host "=" * 70
Write-Host "STAGE 3 (continued) - MERGE AND DEDUPLICATE"
Write-Host "=" * 70

# Sort by start time
$sortedSubs = $allChunkSubs | Sort-Object { $_.StartMs }

# Deduplicate
$keptSubs = @()
$duplicateCount = 0

for ($i = 0; $i -lt $sortedSubs.Count; $i++) {
    $current = $sortedSubs[$i]
    $isDuplicate = $false

    if ($keptSubs.Count -gt 0) {
        $lastKept = $keptSubs[-1]
        $overlapMs = $current.StartMs - $lastKept.EndMs

        # Duplicate detection logic (85% similarity threshold)
        if ($overlapMs -lt 3000 -and $overlapMs -gt -500) {
            # Similarity check would go here - for now just check timing
            if ([Math]::Abs($overlapMs) -lt 500) {
                $isDuplicate = $true
                $duplicateCount++
                Write-Host "DEDUP: Chunk boundary duplicate rejected (overlap=$([Math]::Round($overlapMs, 0))ms)"
            }
        }
    }

    if (!$isDuplicate) {
        $keptSubs += $current
    }
}

Write-Host ""
Write-Host "After Merge: $($sortedSubs.Count) subtitles"
Write-Host "After Dedup: $($keptSubs.Count) subtitles (rejected: $duplicateCount)"

# Write final merged SRT
$mergedSrt = Join-Path $TempPath "chunked_merged.srt"
$writer = New-Object System.IO.StreamWriter($mergedSrt, $false, [System.Text.Encoding]::UTF8)
$idx = 1
foreach ($sub in $keptSubs) {
    $writer.WriteLine($idx)
    $writer.WriteLine("$($sub.StartTime.ToString()) --> $($sub.EndTime.ToString())")
    $writer.WriteLine("(chunk subtitle)")  # Text not available from offset-only data
    $writer.WriteLine()
    $idx++
}
$writer.Close()

$mergedInfo = Get-SrtInfo $mergedSrt
Write-Host "Final Chunked SRT: $mergedSrt"
Write-Host "Final Chunked Subtitles: $($mergedInfo.Subtitles)"

Write-Host ""
$report += "  After Merge: $($sortedSubs.Count) subs"
$report += "  After Dedup: $($keptSubs.Count) subs (rejected: $duplicateCount)"
$report += "  Final: $($keptSubs.Count) subs"

Write-Host ""

# ========== STAGE 4: COMPARISON TABLE ==========
Write-Host "=" * 70
Write-Host "STAGE 4 - FINAL COMPARISON TABLE"
Write-Host "=" * 70

Write-Host ""
Write-Host "Legacy Mode"
Write-Host "-----------"
Write-Host "Subtitles: $legacyRawCount"
Write-Host "Words: $legacyRawWords"

Write-Host ""
Write-Host "Chunked Mode"
Write-Host "------------"
for ($i = 0; $i -lt $chunks.Count; $i++) {
    Write-Host "Chunk $($i+1) Raw: $($chunkRawCounts[$i]) subs, $($chunkRawWords[$i]) words"
}
Write-Host ""
Write-Host "Chunk Total Raw: $chunkTotalRaw subs, $chunkTotalRawWords words"
Write-Host "After Merge: $($sortedSubs.Count) subs"
Write-Host "After Duplicate Filter: $($keptSubs.Count) subs"

$delta = $legacyRawCount - $keptSubs.Count
Write-Host ""
Write-Host "COMPARISON"
Write-Host "----------"
Write-Host "                    Legacy    Chunked    Delta"
Write-Host "Raw Whisper Output:  $($legacyRawCount.ToString().PadLeft(6))   $($chunkTotalRaw.ToString().PadLeft(6))      $delta"
Write-Host "After Dedup:         $($legacyRawCount.ToString().PadLeft(6))   $($keptSubs.Count.ToString().PadLeft(6))      $($legacyRawCount - $keptSubs.Count)"

$report += ""
$report += "STAGE 4 - COMPARISON"
$report += "  Legacy Raw: $legacyRawCount subs, $legacyRawWords words"
$report += "  Chunked Raw: $chunkTotalRaw subs, $chunkTotalRawWords words"
$report += "  Chunked After Dedup: $($keptSubs.Count) subs"
$report += "  Delta: $($legacyRawCount - $keptSubs.Count) subs"

# ========== STAGE 5: FIND FIRST LOSS ==========
Write-Host ""
Write-Host "=" * 70
Write-Host "STAGE 5 - FIND FIRST POINT OF LOSS"
Write-Host "=" * 70

Write-Host ""
Write-Host "Expected: $legacyRawCount subtitles (from Legacy mode)"

Write-Host ""
Write-Host "Point of Loss Analysis:"
Write-Host "  Raw Whisper: $chunkTotalRaw  (chunked raw vs legacy raw: $([Math]::Abs($chunkTotalRaw - $legacyRawCount)))"
Write-Host "  After Merge: $($sortedSubs.Count)"
Write-Host "  After Dedup: $($keptSubs.Count)"

$lossStage = "Unknown"
if ($chunkTotalRaw -lt $legacyRawCount) {
    $lossStage = "Whisper produced fewer subtitles for short audio"
} elseif ($sortedSubs.Count -lt $chunkTotalRaw) {
    $lossStage = "Parser/Merge removed subtitles"
} elseif ($keptSubs.Count -lt $sortedSubs.Count) {
    $lossStage = "Duplicate detector removed valid subtitles"
} else {
    $lossStage = "No significant loss detected"
}

Write-Host ""
Write-Host "ROOT CAUSE STAGE: $lossStage"

$report += ""
$report += "STAGE 5 - POINT OF LOSS"
$report += "  Expected: $legacyRawCount subtitles"
$report += "  Root Cause: $lossStage"

# ========== OUTPUT REPORT ==========
Write-Host ""
Write-Host "=" * 70
Write-Host "FULL REPORT"
Write-Host "=" * 70
foreach ($line in $report) {
    Write-Host $line
}

# Save report to file
$reportPath = Join-Path $TempPath "certification_report.txt"
$report | Out-File -FilePath $reportPath -Encoding UTF8
Write-Host ""
Write-Host "Report saved to: $reportPath"
Write-Host "Temp files at: $TempPath"