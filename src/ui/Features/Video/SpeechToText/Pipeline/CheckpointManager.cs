using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class TranscriptionCheckpoint
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("videoPath")]
    public string VideoPath { get; set; } = string.Empty;

    [JsonPropertyName("engineId")]
    public string EngineId { get; set; } = string.Empty;

    [JsonPropertyName("modelId")]
    public string ModelId { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("videoDurationTicks")]
    public long VideoDurationTicks { get; set; }

    [JsonPropertyName("totalChunks")]
    public int TotalChunks { get; set; }

    [JsonPropertyName("completedChunks")]
    public int CompletedChunks { get; set; }

    [JsonPropertyName("lastCompletedChunkEndTicks")]
    public long LastCompletedChunkEndTicks { get; set; }

    [JsonPropertyName("savedAt")]
    public DateTime SavedAt { get; set; }

    [JsonPropertyName("srtPath")]
    public string SrtPath { get; set; } = string.Empty;

    [JsonIgnore]
    public TimeSpan LastCompletedChunkEnd => TimeSpan.FromTicks(LastCompletedChunkEndTicks);

    [JsonIgnore]
    public TimeSpan VideoDuration => TimeSpan.FromTicks(VideoDurationTicks);

    [JsonPropertyName("translationEnabled")]
    public bool TranslationEnabled { get; set; }

    [JsonPropertyName("targetLanguages")]
    public List<string> TargetLanguages { get; set; } = new();

    [JsonPropertyName("completedTranslationChunks")]
    public Dictionary<string, List<int>> CompletedTranslationChunks { get; set; } = new();

    [JsonPropertyName("generateBilingual")]
    public bool GenerateBilingual { get; set; }

    public double PercentComplete => TotalChunks > 0
        ? (double)CompletedChunks / TotalChunks * 100.0
        : 0;

    public bool IsTranslationCompleteFor(string targetLanguage)
    {
        if (!CompletedTranslationChunks.TryGetValue(targetLanguage, out var chunks))
            return false;

        return chunks.Count >= TotalChunks;
    }

    public void MarkTranslationChunkComplete(string targetLanguage, int chunkIndex)
    {
        if (!CompletedTranslationChunks.ContainsKey(targetLanguage))
            CompletedTranslationChunks[targetLanguage] = new List<int>();

        if (!CompletedTranslationChunks[targetLanguage].Contains(chunkIndex))
            CompletedTranslationChunks[targetLanguage].Add(chunkIndex);
    }
}

public sealed class CheckpointManager
{
    private readonly string _checkpointPath;
    private readonly string _backupPath;
    private TranscriptionCheckpoint? _current;
    private bool _disposed;

    public string CheckpointPath => _checkpointPath;
    public TranscriptionCheckpoint? Current => _current;

    public CheckpointManager(string srtPath)
    {
        var dir = Path.GetDirectoryName(srtPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(srtPath);
        _checkpointPath = Path.Combine(dir, name + ".checkpoint");
        _backupPath = Path.Combine(dir, name + ".checkpoint.bak");
    }

    public bool HasCheckpoint()
    {
        return File.Exists(_checkpointPath);
    }

    public TranscriptionCheckpoint? Load()
    {
        if (!File.Exists(_checkpointPath))
            return null;

        try
        {
            var json = File.ReadAllText(_checkpointPath);
            _current = JsonSerializer.Deserialize<TranscriptionCheckpoint>(json);
            return _current;
        }
        catch
        {
            if (File.Exists(_backupPath))
            {
                try
                {
                    var json = File.ReadAllText(_backupPath);
                    _current = JsonSerializer.Deserialize<TranscriptionCheckpoint>(json);
                    return _current;
                }
                catch
                {
                }
            }
            return null;
        }
    }

    public void Save(TranscriptionCheckpoint checkpoint)
    {
        checkpoint.SavedAt = DateTime.UtcNow;

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(checkpoint, options);

        var tempPath = _checkpointPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, json);

            if (File.Exists(_checkpointPath))
            {
                if (File.Exists(_backupPath))
                    File.Delete(_backupPath);
                File.Move(_checkpointPath, _backupPath);
            }

            File.Move(tempPath, _checkpointPath);
            _current = checkpoint;
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
    }

    public void Delete()
    {
        if (File.Exists(_checkpointPath))
            File.Delete(_checkpointPath);
        if (File.Exists(_backupPath))
            File.Delete(_backupPath);
        _current = null;
    }

    public void DeleteBackup()
    {
        if (File.Exists(_backupPath))
            File.Delete(_backupPath);
    }
}