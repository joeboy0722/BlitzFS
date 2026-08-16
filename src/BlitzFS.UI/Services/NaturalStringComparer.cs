using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BlitzFS.UI.Services
{
    /// <summary>
    /// Windows 原生自然語言數字排序比對器 (例如: 1, 2, 10 而非 1, 10, 2)
    /// </summary>
    public sealed class NaturalStringComparer : IComparer<string>
    {
        public static NaturalStringComparer Instance { get; } = new();

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int StrCmpLogicalW(string psz1, string psz2);

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            try
            {
                return StrCmpLogicalW(x, y);
            }
            catch
            {
                return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
