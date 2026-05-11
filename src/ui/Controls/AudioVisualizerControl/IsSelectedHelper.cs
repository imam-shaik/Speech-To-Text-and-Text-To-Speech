using Nikse.SubtitleEdit.Features.Main;
using System;
using System.Collections.Generic;

namespace Nikse.SubtitleEdit.Controls.AudioVisualizerControl;

public class IsSelectedHelper
{
    private SelectionRange[] _ranges = Array.Empty<SelectionRange>();
    private int _rangeCount;
    private int _lastPosition = int.MaxValue;
    private SelectionRange _nextSelection;

    public void Reset(List<SubtitleLineViewModel> paragraphs, int sampleRate)
    {
        _rangeCount = paragraphs.Count;
        if (_ranges.Length < _rangeCount)
        {
            Array.Resize(ref _ranges, _rangeCount);
        }

        for (var index = 0; index < _rangeCount; index++)
        {
            var p = paragraphs[index];
            var start = (int)Math.Round(p.StartTime.TotalSeconds * sampleRate);
            var end = (int)Math.Round(p.EndTime.TotalSeconds * sampleRate);
            _ranges[index] = new SelectionRange(start, end);
        }

        _lastPosition = int.MaxValue;
        _nextSelection = new SelectionRange(int.MaxValue, int.MaxValue);
    }

    public bool IsSelected(int position)
    {
        if (position < _lastPosition || position > _nextSelection.End)
        {
            FindNextSelection(position);
        }

        _lastPosition = position;

        return position >= _nextSelection.Start && position <= _nextSelection.End;
    }

    private void FindNextSelection(int position)
    {
        _nextSelection = new SelectionRange(int.MaxValue, int.MaxValue);
        for (var index = 0; index < _rangeCount; index++)
        {
            var range = _ranges[index];
            if (range.End >= position && (range.Start < _nextSelection.Start || range.Start == _nextSelection.Start && range.End > _nextSelection.End))
            {
                _nextSelection = range;
            }
        }
    }

    private readonly struct SelectionRange
    {
        public readonly int Start;
        public readonly int End;

        public SelectionRange(int start, int end)
        {
            Start = start;
            End = end;
        }
    }
}
