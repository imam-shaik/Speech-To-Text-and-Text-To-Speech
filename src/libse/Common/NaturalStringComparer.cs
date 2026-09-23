using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Nikse.SubtitleEdit.Core.Common
{
    public sealed class NaturalStringComparer : IComparer<string>
    {
        private static readonly Regex NumericPattern = new Regex(@"(\d+)", RegexOptions.Compiled);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string psz1, string psz2);

        public static NaturalStringComparer Native => Instance;
        private static readonly NaturalStringComparer Instance = new NaturalStringComparer();

        public int Compare(string x, string y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            return Configuration.IsRunningOnWindows
                ? StrCmpLogicalW(x, y)
                : CompareUnix(x, y);
        }

        private static int CompareUnix(string x, string y)
        {
            var matchesX = NumericPattern.Matches(x);
            var matchesY = NumericPattern.Matches(y);

            int posX = 0, posY = 0;
            int iX = 0, iY = 0;

            while (true)
            {
                bool isDigitX = iX < matchesX.Count && matchesX[iX].Index == posX;
                bool isDigitY = iY < matchesY.Count && matchesY[iY].Index == posY;

                if (isDigitX && isDigitY)
                {
                    var numX = matchesX[iX].Value;
                    var numY = matchesY[iY].Value;
                    int cmp = string.Compare(numX, numY, StringComparison.Ordinal);
                    if (cmp != 0) return cmp;
                    posX += numX.Length;
                    posY += numY.Length;
                    iX++;
                    iY++;
                }
                else if (isDigitX)
                {
                    return -1;
                }
                else if (isDigitY)
                {
                    return 1;
                }
                else
                {
                    int nextDigitX = iX < matchesX.Count ? matchesX[iX].Index : x.Length;
                    int nextDigitY = iY < matchesY.Count ? matchesY[iY].Index : y.Length;
                    int lenX = nextDigitX - posX;
                    int lenY = nextDigitY - posY;
                    int cmp = string.Compare(x, posX, y, posY, Math.Min(lenX, lenY), StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0) return cmp;
                    if (lenX != lenY) return lenX - lenY;
                    posX = nextDigitX;
                    posY = nextDigitY;
                }

                if (posX >= x.Length && posY >= y.Length)
                    return 0;
                if (posX >= x.Length) return -1;
                if (posY >= y.Length) return 1;
            }
        }
    }
}