using System;
using System.IO;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

namespace UITests.Features.Video.SpeechToText.Pipeline;

public class CheckpointManagerTests
{
    [Fact]
    public void Save_And_Load_RoundTrip()
    {
        var tempPath = Path.GetTempFileName();
        var srtPath = Path.ChangeExtension(tempPath, ".srt");
        try
        {
            File.WriteAllText(srtPath, "test");
            var manager = new CheckpointManager(srtPath);

            var checkpoint = new TranscriptionCheckpoint
            {
                VideoPath = "C:\\Videos\\test.mp4",
                EngineId = "whisper-cpp",
                ModelId = "base.en",
                Language = "en",
                VideoDurationTicks = TimeSpan.FromHours(2).Ticks,
                TotalChunks = 240,
                CompletedChunks = 48,
                LastCompletedChunkEndTicks = TimeSpan.FromMinutes(24).Ticks,
                SrtPath = srtPath
            };

            manager.Save(checkpoint);

            var loaded = manager.Load();
            Assert.NotNull(loaded);
            Assert.Equal(checkpoint.VideoPath, loaded.VideoPath);
            Assert.Equal(checkpoint.EngineId, loaded.EngineId);
            Assert.Equal(checkpoint.TotalChunks, loaded.TotalChunks);
            Assert.Equal(checkpoint.CompletedChunks, loaded.CompletedChunks);
            Assert.Equal(checkpoint.LastCompletedChunkEndTicks, loaded.LastCompletedChunkEndTicks);
        }
        finally
        {
            File.Delete(srtPath);
            File.Delete(tempPath);
            File.Delete(Path.ChangeExtension(srtPath, ".checkpoint"));
            File.Delete(Path.ChangeExtension(srtPath, ".checkpoint.bak"));
        }
    }

    [Fact]
    public void HasCheckpoint_ReturnsTrueWhenExists()
    {
        var tempPath = Path.GetTempFileName();
        var srtPath = Path.ChangeExtension(tempPath, ".srt");
        try
        {
            File.WriteAllText(srtPath, "test");
            var manager = new CheckpointManager(srtPath);

            Assert.False(manager.HasCheckpoint());

            manager.Save(new TranscriptionCheckpoint { VideoPath = "test", SrtPath = srtPath });

            Assert.True(manager.HasCheckpoint());
        }
        finally
        {
            File.Delete(srtPath);
            File.Delete(Path.ChangeExtension(srtPath, ".checkpoint"));
            File.Delete(Path.ChangeExtension(srtPath, ".checkpoint.bak"));
        }
    }

    [Fact]
    public void HasCheckpoint_ReturnsFalseWhenDoesNotExist()
    {
        var tempPath = Path.GetTempFileName();
        var srtPath = Path.ChangeExtension(tempPath, ".srt");
        File.WriteAllText(srtPath, "test");
        var manager = new CheckpointManager(srtPath);

        Assert.False(manager.HasCheckpoint());

        File.Delete(srtPath);
    }

    [Fact]
    public void Load_ReturnsNullWhenNoCheckpoint()
    {
        var tempPath = Path.GetTempFileName();
        var srtPath = Path.ChangeExtension(tempPath, ".srt");
        File.WriteAllText(srtPath, "test");
        var manager = new CheckpointManager(srtPath);

        Assert.Null(manager.Load());
    }

    [Fact]
    public void Delete_RemovesCheckpointAndBackup()
    {
        var tempPath = Path.GetTempFileName();
        var srtPath = Path.ChangeExtension(tempPath, ".srt");
        try
        {
            File.WriteAllText(srtPath, "test");
            var manager = new CheckpointManager(srtPath);

            manager.Save(new TranscriptionCheckpoint { VideoPath = "test", SrtPath = srtPath });
            Assert.True(manager.HasCheckpoint());

            manager.Delete();
            Assert.False(manager.HasCheckpoint());
        }
        finally
        {
            File.Delete(srtPath);
            File.Delete(Path.ChangeExtension(srtPath, ".checkpoint"));
            File.Delete(Path.ChangeExtension(srtPath, ".checkpoint.bak"));
        }
    }

    [Fact]
    public void Save_CreatesBackupOfPrevious()
    {
        var tempPath = Path.GetTempFileName();
        var srtPath = Path.ChangeExtension(tempPath, ".srt");
        try
        {
            File.WriteAllText(srtPath, "test");
            var manager = new CheckpointManager(srtPath);
            var checkpointPath = Path.ChangeExtension(srtPath, ".checkpoint");
            var backupPath = Path.ChangeExtension(srtPath, ".checkpoint.bak");

            manager.Save(new TranscriptionCheckpoint { VideoPath = "first", SrtPath = srtPath });
            File.SetLastWriteTime(checkpointPath, DateTime.Now.AddMinutes(-10));

            manager.Save(new TranscriptionCheckpoint { VideoPath = "second", SrtPath = srtPath });

            Assert.True(File.Exists(backupPath));
            var backupContent = File.ReadAllText(backupPath);
            Assert.Contains("first", backupContent);
        }
        finally
        {
            File.Delete(srtPath);
            File.Delete(Path.ChangeExtension(srtPath, ".checkpoint"));
            File.Delete(Path.ChangeExtension(srtPath, ".checkpoint.bak"));
        }
    }

    [Fact]
    public void Load_RecoversFromCorruptedCheckpointUsingBackup()
    {
        var tempPath = Path.GetTempFileName();
        var srtPath = Path.ChangeExtension(tempPath, ".srt");
        try
        {
            File.WriteAllText(srtPath, "test");
            var manager = new CheckpointManager(srtPath);

            manager.Save(new TranscriptionCheckpoint { VideoPath = "first", SrtPath = srtPath });
            manager.Save(new TranscriptionCheckpoint { VideoPath = "second", SrtPath = srtPath });
            manager.Save(new TranscriptionCheckpoint { VideoPath = "good", SrtPath = srtPath });
            manager.Save(new TranscriptionCheckpoint { VideoPath = "newest", SrtPath = srtPath });

            var checkpointPath = Path.ChangeExtension(srtPath, ".checkpoint");
            File.WriteAllText(checkpointPath, "CORRUPTED JSON {{{");

            var loaded = manager.Load();
            Assert.NotNull(loaded);
            Assert.Equal("good", loaded.VideoPath);
        }
        finally
        {
            File.Delete(srtPath);
            File.Delete(Path.ChangeExtension(srtPath, ".checkpoint"));
            File.Delete(Path.ChangeExtension(srtPath, ".checkpoint.bak"));
        }
    }

    [Fact]
    public void CheckpointMetadataOnly_DoesNotContainParagraphs()
    {
        var tempPath = Path.GetTempFileName();
        var srtPath = Path.ChangeExtension(tempPath, ".srt");
        try
        {
            File.WriteAllText(srtPath, "test");
            var manager = new CheckpointManager(srtPath);

            manager.Save(new TranscriptionCheckpoint
            {
                VideoPath = "C:\\test.mp4",
                EngineId = "whisper",
                TotalChunks = 100,
                CompletedChunks = 50,
                SrtPath = srtPath
            });

            var json = File.ReadAllText(Path.ChangeExtension(srtPath, ".checkpoint"));
            Assert.DoesNotContain("paragraphs", json);
            Assert.DoesNotContain("Hello world", json);
        }
        finally
        {
            File.Delete(srtPath);
            File.Delete(Path.ChangeExtension(srtPath, ".checkpoint"));
            File.Delete(Path.ChangeExtension(srtPath, ".checkpoint.bak"));
        }
    }

    [Fact]
    public void Save_100HourTimestamp_Works()
    {
        var tempPath = Path.GetTempFileName();
        var srtPath = Path.ChangeExtension(tempPath, ".srt");
        try
        {
            File.WriteAllText(srtPath, "test");
            var manager = new CheckpointManager(srtPath);

            manager.Save(new TranscriptionCheckpoint
            {
                VideoPath = "C:\\long.mp4",
                VideoDurationTicks = TimeSpan.FromHours(120).Ticks,
                LastCompletedChunkEndTicks = TimeSpan.FromHours(100).Ticks,
                SrtPath = srtPath
            });

            var loaded = manager.Load();
            Assert.NotNull(loaded);
            Assert.Equal(TimeSpan.FromHours(120), loaded.VideoDuration);
            Assert.Equal(TimeSpan.FromHours(100), loaded.LastCompletedChunkEnd);
        }
        finally
        {
            File.Delete(srtPath);
            File.Delete(Path.ChangeExtension(srtPath, ".checkpoint"));
            File.Delete(Path.ChangeExtension(srtPath, ".checkpoint.bak"));
        }
    }

    [Fact]
    public void PercentComplete_CalculatesCorrectly()
    {
        var checkpoint = new TranscriptionCheckpoint
        {
            TotalChunks = 100,
            CompletedChunks = 25
        };

        Assert.Equal(25.0, checkpoint.PercentComplete);
    }

    [Fact]
    public void PercentComplete_ZeroChunks_ReturnsZero()
    {
        var checkpoint = new TranscriptionCheckpoint
        {
            TotalChunks = 0,
            CompletedChunks = 0
        };

        Assert.Equal(0.0, checkpoint.PercentComplete);
    }
}