using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

namespace UITests.Features.Video.SpeechToText.Pipeline;

public class StreamingSrtWriterTests : IDisposable
{
    private string? _tempPath;

    private string GetTempPath() => _tempPath ??= Path.GetTempFileName();

    public void Dispose()
    {
        if (_tempPath != null && File.Exists(_tempPath))
        {
            File.Delete(_tempPath);
        }
    }

    [Fact]
    public void Append_WritesValidSrt()
    {
        var tempPath = GetTempPath();
        using (var writer = new StreamingSrtWriter(tempPath))
        {
            writer.Append(new Paragraph("Hello world", 0, 2000));
            writer.Append(new Paragraph("Second line", 3000, 5000));
        }

        var lines = File.ReadAllLines(tempPath, Encoding.UTF8);
        Assert.Equal(8, lines.Length);
        Assert.Equal("1", lines[0]);
        Assert.Equal("00:00:00,000 --> 00:00:02,000", lines[1]);
        Assert.Equal("Hello world", lines[2]);
        Assert.Equal(string.Empty, lines[3]);
        Assert.Equal("2", lines[4]);
        Assert.Equal("00:00:03,000 --> 00:00:05,000", lines[5]);
        Assert.Equal("Second line", lines[6]);
    }

    [Fact]
    public void Append_TracksEntryCount()
    {
        var tempPath = GetTempPath();
        using (var writer = new StreamingSrtWriter(tempPath))
        {
            Assert.Equal(0, writer.EntryCount);
            writer.Append(new Paragraph("First", 0, 1000));
            Assert.Equal(1, writer.EntryCount);
            writer.Append(new Paragraph("Second", 2000, 3000));
            Assert.Equal(2, writer.EntryCount);
            writer.Append(new Paragraph("Third", 4000, 5000));
            Assert.Equal(3, writer.EntryCount);
        }
    }

    [Fact]
    public void Append_HundredThousandSubtitles_DoesNotOverflowCounter()
    {
        var tempPath = GetTempPath();
        using (var writer = new StreamingSrtWriter(tempPath))
        {
            for (var i = 0; i < 100_000; i++)
            {
                writer.Append(new Paragraph($"Line {i}", i * 1000, (i + 1) * 1000));
            }
            writer.Flush();
            Assert.Equal(100_000, writer.EntryCount);
        }
    }

    [Fact]
    public void Append_UsesUtf8Encoding()
    {
        var tempPath = GetTempPath();
        using (var writer = new StreamingSrtWriter(tempPath))
        {
            writer.Append(new Paragraph("日本語テスト", 0, 1000));
            writer.Append(new Paragraph("العربية", 2000, 3000));
            writer.Append(new Paragraph("Ελληνικά", 4000, 5000));
        }

        var content = File.ReadAllText(tempPath, Encoding.UTF8);
        Assert.Contains("日本語テスト", content);
        Assert.Contains("العربية", content);
        Assert.Contains("Ελληνικά", content);
    }

    [Fact]
    public void Append_DisposesWithoutFlush_DoesNotCorruptFile()
    {
        var tempPath = GetTempPath();
        using (var writer = new StreamingSrtWriter(tempPath))
        {
            writer.Append(new Paragraph("Test1", 0, 1000));
            writer.Append(new Paragraph("Test2", 2000, 3000));
        }

        var lines = File.ReadAllLines(tempPath, Encoding.UTF8);
        Assert.Equal(8, lines.Length);
    }

    [Fact]
    public void Append_AfterDispose_ThrowsObjectDisposedException()
    {
        var tempPath = GetTempPath();
        var writer = new StreamingSrtWriter(tempPath);
        writer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => writer.Append(new Paragraph("Test", 0, 1000)));
    }

    [Fact]
    public void Append_100HourTimestamp_WritesCorrectly()
    {
        var tempPath = GetTempPath();
        using (var writer = new StreamingSrtWriter(tempPath))
        {
            writer.Append(new Paragraph("Test", TimeSpan.FromHours(100).TotalMilliseconds, TimeSpan.FromHours(100).TotalMilliseconds + 1000));
        }

        var lines = File.ReadAllLines(tempPath, Encoding.UTF8);
        Assert.Contains("100:00:00,000 --> 100:00:01,000", lines[1]);
    }

    [Fact]
    public void Append_AppendsMultipleParagraphs()
    {
        var tempPath = GetTempPath();
        using (var writer = new StreamingSrtWriter(tempPath))
        {
            var paragraphs = new List<Paragraph>
            {
                new Paragraph("First", 0, 1000),
                new Paragraph("Second", 2000, 3000),
                new Paragraph("Third", 4000, 5000)
            };
            writer.Append(paragraphs);
            Assert.Equal(3, writer.EntryCount);
        }
    }
}