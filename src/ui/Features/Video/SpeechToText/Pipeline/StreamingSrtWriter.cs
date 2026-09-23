using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Pipeline;

public sealed class StreamingSrtWriter : IDisposable
{
    private readonly string _srtPath;
    private readonly FileStream _fileStream;
    private readonly StreamWriter _writer;
    private int _entryCount;
    private bool _disposed;
    private readonly object _writeLock = new();

    public int EntryCount => _entryCount;
    public string FilePath => _srtPath;

    public StreamingSrtWriter(string srtPath)
    {
        _srtPath = srtPath;
        _fileStream = new FileStream(srtPath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.None);
        _writer = new StreamWriter(_fileStream, Encoding.UTF8);
    }

    public void Append(Paragraph paragraph)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(StreamingSrtWriter));

        lock (_writeLock)
        {
            _entryCount++;
            var sb = new StringBuilder();
            sb.AppendLine(_entryCount.ToString());
            sb.AppendLine(paragraph.StartTime.ToString() + " --> " + paragraph.EndTime.ToString());
            sb.AppendLine(paragraph.Text);
            sb.AppendLine();
            _writer.Write(sb.ToString());
        }
    }

    public void Append(IEnumerable<Paragraph> paragraphs)
    {
        foreach (var p in paragraphs)
        {
            Append(p);
        }
    }

    public void Flush()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(StreamingSrtWriter));

        lock (_writeLock)
        {
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_writeLock)
        {
            if (_disposed) return;
            _writer.Flush();
            _writer.Dispose();
            _fileStream.Dispose();
            _disposed = true;
        }
    }
}