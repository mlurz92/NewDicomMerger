namespace NewDicomMerger.Helpers;

/// <summary>
/// Sorts strings with embedded numbers in human-natural order.
/// E.g. "slice2" comes before "slice10".
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
            {
                long numX = 0, numY = 0;
                while (ix < x.Length && char.IsDigit(x[ix]))
                    numX = numX * 10 + (x[ix++] - '0');
                while (iy < y.Length && char.IsDigit(y[iy]))
                    numY = numY * 10 + (y[iy++] - '0');
                if (numX != numY) return numX < numY ? -1 : 1;
            }
            else
            {
                int cmp = char.ToLowerInvariant(x[ix]).CompareTo(char.ToLowerInvariant(y[iy]));
                if (cmp != 0) return cmp;
                ix++;
                iy++;
            }
        }
        return x.Length.CompareTo(y.Length);
    }
}
