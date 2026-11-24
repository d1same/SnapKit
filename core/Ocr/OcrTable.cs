using System.Text;

namespace SnapKit;

/// <summary>
/// A single OCR word with its bounding box in source image pixel coordinates.
/// </summary>
public readonly record struct OcrWordBox(string Text, double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width * 0.5;
    public double CenterY => Y + Height * 0.5;
}

/// <summary>
/// Reconstructed 2D table layout produced from OCR word boxes, ready to paste into Excel or Sheets.
/// </summary>
public sealed class OcrTableResult
{
    public static readonly OcrTableResult Empty = new();

    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = Array.Empty<IReadOnlyList<string>>();

    /// <summary>X coordinates (in source image pixels) separating adjacent detected columns.</summary>
    public IReadOnlyList<double> ColumnSplits { get; init; } = Array.Empty<double>();

    public int RowCount => Rows.Count;
    public int ColumnCount => Rows.Count == 0 ? 0 : Rows[0].Count;

    /// <summary>True when the layout strongly resembles a multi-column table rather than prose.</summary>
    public bool IsLikelyTable { get; init; }

    /// <summary>
    /// Formats the table as Tab-Separated Values (TSV) with CRLF line endings so Ctrl+V in Excel,
    /// Google Sheets, and LibreOffice Calc places every cell into its matching row and column.
    /// </summary>
    public string ToTsv()
    {
        if (Rows.Count == 0)
            return "";

        var sb = new StringBuilder();
        for (int r = 0; r < Rows.Count; r++)
        {
            if (r > 0)
                sb.Append("\r\n");

            var row = Rows[r];
            for (int c = 0; c < row.Count; c++)
            {
                if (c > 0)
                    sb.Append('\t');
                sb.Append(SanitizeTsvCell(row[c]));
            }
        }
        return sb.ToString();
    }

    /// <summary>Formats the table as RFC 4180 CSV.</summary>
    public string ToCsv()
    {
        if (Rows.Count == 0)
            return "";

        var sb = new StringBuilder();
        for (int r = 0; r < Rows.Count; r++)
        {
            if (r > 0)
                sb.Append("\r\n");

            var row = Rows[r];
            for (int c = 0; c < row.Count; c++)
            {
                if (c > 0)
                    sb.Append(',');
                var cell = SanitizeTsvCell(row[c]);
                if (cell.Contains(',') || cell.Contains('"'))
                    sb.Append('"').Append(cell.Replace("\"", "\"\"")).Append('"');
                else
                    sb.Append(cell);
            }
        }
        return sb.ToString();
    }

    private static string SanitizeTsvCell(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return value
            .Replace('\t', ' ')
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }
}

/// <summary>
/// Reconstructs spreadsheet-ready rows and columns from raw OCR word bounding boxes using
/// Y-axis visual row clustering and 2D vertical gutter + left-edge alignment analysis.
/// </summary>
public static class OcrTable
{
    public static OcrTableResult Build(IReadOnlyList<OcrWordBox> input)
    {
        var words = new List<OcrWordBox>(input.Count);
        for (int i = 0; i < input.Count; i++)
        {
            var w = input[i];
            if (!string.IsNullOrWhiteSpace(w.Text) && w.Width > 0 && w.Height > 0)
                words.Add(new OcrWordBox(w.Text.Trim(), w.X, w.Y, w.Width, w.Height));
        }

        if (words.Count == 0)
            return OcrTableResult.Empty;

        double medianH = Math.Max(6.0, Median(words.Select(w => w.Height).ToList()));
        double charW = EstimateCharWidth(words, medianH);

        // 1. Cluster words into visual horizontal rows by vertical center / overlap.
        var rows = ClusterRows(words, medianH);
        if (rows.Count == 0)
            return OcrTableResult.Empty;

        // 2. Estimate normal inter-word spacing inside a phrase/cell.
        double spaceRef = EstimateSpaceRef(rows, charW, medianH);

        // 3. Detect column divider X coordinates.
        var splits = rows.Count == 1
            ? DetectSingleRowSplits(rows[0], spaceRef, medianH)
            : DetectMultiRowSplits(rows, spaceRef, charW, medianH);

        // 4. Bucket every word in every row into its column.
        int colCount = splits.Count + 1;
        var grid = new List<string[]>(rows.Count);
        var colNonEmpty = new bool[colCount];

        foreach (var row in rows)
        {
            var buckets = new List<OcrWordBox>[colCount];
            foreach (var w in row)
            {
                int col = ColumnIndex(w, splits, charW);
                buckets[col] ??= new List<OcrWordBox>();
                buckets[col].Add(w);
            }

            var cells = new string[colCount];
            bool anyCell = false;
            for (int c = 0; c < colCount; c++)
            {
                if (buckets[c] == null || buckets[c].Count == 0)
                {
                    cells[c] = "";
                    continue;
                }
                buckets[c].Sort((a, b) => a.X.CompareTo(b.X));
                cells[c] = string.Join(" ", buckets[c].Select(b => b.Text));
                if (!string.IsNullOrEmpty(cells[c]))
                {
                    colNonEmpty[c] = true;
                    anyCell = true;
                }
            }

            if (anyCell)
                grid.Add(cells);
        }

        if (grid.Count == 0)
            return OcrTableResult.Empty;

        // 5. Compact out any column that ended up completely empty across all rows.
        var keptCols = new List<int>(colCount);
        for (int c = 0; c < colCount; c++)
        {
            if (colNonEmpty[c])
                keptCols.Add(c);
        }

        var finalRows = new List<IReadOnlyList<string>>(grid.Count);
        foreach (var row in grid)
        {
            var compact = new string[keptCols.Count];
            for (int i = 0; i < keptCols.Count; i++)
                compact[i] = row[keptCols[i]];
            finalRows.Add(compact);
        }

        // Keep only the splits that correspond to boundaries between kept columns.
        var finalSplits = new List<double>();
        for (int i = 0; i < keptCols.Count - 1; i++)
        {
            int leftCol = keptCols[i];
            int rightCol = keptCols[i + 1];
            // Midpoint of the splits spanning [leftCol .. rightCol - 1]
            int splitIdx = leftCol + (rightCol - leftCol - 1) / 2;
            if (splitIdx >= 0 && splitIdx < splits.Count)
                finalSplits.Add(splits[splitIdx]);
        }

        int finalColCount = keptCols.Count;
        int finalRowCount = finalRows.Count;
        int multiCellRows = finalRows.Count(r => r.Count(cell => !string.IsNullOrEmpty(cell)) >= 2);
        bool isLikelyTable =
            finalColCount >= 2 &&
            finalRowCount >= 2 &&
            multiCellRows >= 2 &&
            (finalColCount >= 3 || finalRowCount >= 3) &&
            (multiCellRows / (double)finalRowCount) >= 0.55;

        return new OcrTableResult
        {
            Rows = finalRows,
            ColumnSplits = finalSplits,
            IsLikelyTable = isLikelyTable
        };
    }

    private sealed class RowCluster
    {
        public readonly List<OcrWordBox> Words = new();
        public double SumCy;
        public double Top;
        public double Bottom;

        public double AvgCy => SumCy / Words.Count;

        public RowCluster(OcrWordBox first)
        {
            Words.Add(first);
            SumCy = first.CenterY;
            Top = first.Y;
            Bottom = first.Bottom;
        }

        public void Add(OcrWordBox w)
        {
            Words.Add(w);
            SumCy += w.CenterY;
            if (w.Y < Top) Top = w.Y;
            if (w.Bottom > Bottom) Bottom = w.Bottom;
        }
    }

    private static List<List<OcrWordBox>> ClusterRows(List<OcrWordBox> words, double medianH)
    {
        var sorted = words
            .OrderBy(w => w.CenterY)
            .ThenBy(w => w.X)
            .ToList();

        double cyThreshold = Math.Max(medianH * 0.55, 4.0);
        var clusters = new List<RowCluster>();

        foreach (var w in sorted)
        {
            RowCluster? best = null;
            double bestDist = double.MaxValue;

            // Check only recent clusters (sorted by CenterY).
            for (int i = clusters.Count - 1; i >= 0; i--)
            {
                var c = clusters[i];
                double dist = Math.Abs(w.CenterY - c.AvgCy);
                if (dist > medianH * 2.5)
                    break;

                double overlap = Math.Min(c.Bottom, w.Bottom) - Math.Max(c.Top, w.Y);
                double minH = Math.Min(Math.Max(1.0, c.Bottom - c.Top), Math.Max(1.0, w.Height));
                bool sameRow = dist <= cyThreshold || overlap >= minH * 0.45;
                if (sameRow && dist < bestDist)
                {
                    bestDist = dist;
                    best = c;
                }
            }

            if (best != null)
                best.Add(w);
            else
                clusters.Add(new RowCluster(w));
        }

        clusters.Sort((a, b) => a.AvgCy.CompareTo(b.AvgCy));
        var result = new List<List<OcrWordBox>>(clusters.Count);
        foreach (var c in clusters)
        {
            c.Words.Sort((a, b) => a.X.CompareTo(b.X));
            result.Add(c.Words);
        }
        return result;
    }

    private static double EstimateCharWidth(List<OcrWordBox> words, double medianH)
    {
        var ratios = new List<double>();
        foreach (var w in words)
        {
            if (w.Text.Length >= 2)
                ratios.Add(w.Width / w.Text.Length);
        }
        double raw = ratios.Count > 0 ? Median(ratios) : medianH * 0.52;
        return Math.Clamp(raw, medianH * 0.25, medianH * 0.90);
    }

    private static double EstimateSpaceRef(List<List<OcrWordBox>> rows, double charW, double medianH)
    {
        var gaps = new List<double>();
        foreach (var row in rows)
        {
            for (int i = 0; i < row.Count - 1; i++)
            {
                double g = row[i + 1].X - row[i].Right;
                if (g > 0.5)
                    gaps.Add(g);
            }
        }

        double normalSpace;
        if (gaps.Count > 0)
        {
            gaps.Sort();
            // Lower 30th percentile reliably picks intra-cell word spaces even when many gaps are column gutters.
            int idx = (int)Math.Floor((gaps.Count - 1) * 0.30);
            normalSpace = Math.Clamp(gaps[idx], charW * 0.40, medianH * 0.65);
        }
        else
        {
            normalSpace = charW * 0.60;
        }

        return Math.Max(normalSpace, charW * 0.55);
    }

    private static List<double> DetectSingleRowSplits(List<OcrWordBox> row, double spaceRef, double medianH)
    {
        var splits = new List<double>();
        double threshold = Math.Max(spaceRef * 2.4, medianH * 1.35);
        for (int i = 0; i < row.Count - 1; i++)
        {
            double gap = row[i + 1].X - row[i].Right;
            if (gap >= threshold)
                splits.Add((row[i].Right + row[i + 1].X) * 0.5);
        }
        return splits;
    }

    private readonly record struct Gutter(double X0, double X1, int CrossingRows);

    private static List<double> DetectMultiRowSplits(
        List<List<OcrWordBox>> rows,
        double spaceRef,
        double charW,
        double medianH)
    {
        // Build exact interval occupancy sweep across all rows.
        var coords = new List<double>();
        double minX = double.MaxValue;
        double maxX = double.MinValue;
        foreach (var row in rows)
        {
            foreach (var w in row)
            {
                coords.Add(w.X);
                coords.Add(w.Right);
                if (w.X < minX) minX = w.X;
                if (w.Right > maxX) maxX = w.Right;
            }
        }

        coords.Sort();
        var uniq = new List<double>(coords.Count);
        for (int i = 0; i < coords.Count; i++)
        {
            if (uniq.Count == 0 || coords[i] - uniq[^1] > 0.25)
                uniq.Add(coords[i]);
        }

        if (uniq.Count < 2)
            return new List<double>();

        // Compute exact row occupancy for each elementary interval [uniq[i], uniq[i+1]].
        int segCount = uniq.Count - 1;
        var occ = new int[segCount];
        for (int s = 0; s < segCount; s++)
        {
            double mid = (uniq[s] + uniq[s + 1]) * 0.5;
            int count = 0;
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                for (int k = 0; k < row.Count; k++)
                {
                    if (row[k].X <= mid && row[k].Right >= mid)
                    {
                        count++;
                        break;
                    }
                }
            }
            occ[s] = count;
        }

        // Collect maximal zero-crossing gutters strictly inside [minX, maxX].
        var candidates = new List<Gutter>();
        int idx = 0;
        while (idx < segCount)
        {
            if (occ[idx] != 0)
            {
                idx++;
                continue;
            }
            int start = idx;
            while (idx < segCount && occ[idx] == 0)
                idx++;
            double gx0 = uniq[start];
            double gx1 = uniq[idx];
            if (gx0 > minX + 0.5 && gx1 < maxX - 0.5)
                candidates.Add(new Gutter(gx0, gx1, 0));
        }

        // For larger tables (>= 6 rows), also consider 1-row-crossing valleys (e.g. a spanning banner/footer row
        // or one overflowing cell) if they don't overlap an already-found zero gutter.
        if (rows.Count >= 6)
        {
            idx = 0;
            while (idx < segCount)
            {
                if (occ[idx] > 1)
                {
                    idx++;
                    continue;
                }
                int start = idx;
                bool hasOne = false;
                while (idx < segCount && occ[idx] <= 1)
                {
                    if (occ[idx] == 1)
                        hasOne = true;
                    idx++;
                }
                if (!hasOne)
                    continue;
                double vx0 = uniq[start];
                double vx1 = uniq[idx];
                if (vx0 <= minX + 0.5 || vx1 >= maxX - 0.5)
                    continue;
                bool overlapsZero = candidates.Any(g => g.X0 < vx1 && g.X1 > vx0);
                if (!overlapsZero)
                    candidates.Add(new Gutter(vx0, vx1, 1));
            }
        }

        candidates.Sort((a, b) => a.X0.CompareTo(b.X0));

        var splits = new List<double>();
        double wideMinGap = Math.Max(spaceRef * 2.1, medianH * 0.95);
        double narrowMinGap = Math.Max(spaceRef * 1.35, 3.5);
        double medGapThreshold = Math.Max(spaceRef * 2.3, medianH * 1.15);
        double alignMinGap = Math.Max(spaceRef * 1.15, 3.0);

        foreach (var g in candidates)
        {
            double minGap = g.X1 - g.X0;
            var rowGaps = new List<double>();
            var rightStarts = new List<double>();
            var leftEnds = new List<double>();

            foreach (var row in rows)
            {
                OcrWordBox? left = null;
                OcrWordBox? right = null;
                for (int i = 0; i < row.Count; i++)
                {
                    var w = row[i];
                    if (w.Right <= g.X0 + 1.0)
                        left = w;
                    else if (w.X >= g.X1 - 1.0 && right == null)
                    {
                        right = w;
                        break;
                    }
                }

                if (left.HasValue && right.HasValue)
                {
                    rowGaps.Add(right.Value.X - left.Value.Right);
                    leftEnds.Add(left.Value.Right);
                    rightStarts.Add(right.Value.X);
                }
                else if (right.HasValue)
                {
                    rightStarts.Add(right.Value.X);
                }
            }

            double medGap = rowGaps.Count > 0 ? Median(rowGaps) : minGap;
            bool accept = false;

            if (g.CrossingRows == 0)
            {
                // Rule 1: Clearly wider than any normal word space across the entire table height.
                if (minGap >= wideMinGap)
                {
                    accept = true;
                }
                // Rule 2: Clear vertical channel + wide typical gap across rows (handles clipped '...' cells).
                else if (minGap >= narrowMinGap && medGap >= medGapThreshold)
                {
                    accept = true;
                }
                // Rule 3: Strong left-aligned column start with genuinely varying left-column widths
                // (never splits identical repeated prefixes like "Drive Space Change Check").
                else if (rowGaps.Count >= 3 && minGap >= alignMinGap)
                {
                    double rightMed = Median(rightStarts);
                    double tol = Math.Max(3.0, charW * 0.65);
                    int alignedCount = rightStarts.Count(x => Math.Abs(x - rightMed) <= tol);
                    double leftSpan = leftEnds.Max() - leftEnds.Min();
                    if (alignedCount >= Math.Ceiling(rightStarts.Count * 0.70) &&
                        leftSpan >= Math.Max(charW * 2.0, medianH * 1.0))
                    {
                        accept = true;
                    }
                }
            }
            else
            {
                // Stricter acceptance for 1-crossing-row valleys (e.g. a spanning banner row in a 6+ row table).
                if (rowGaps.Count >= Math.Max(4, rows.Count / 2) &&
                    minGap >= Math.Max(spaceRef * 1.8, medianH * 0.9) &&
                    medGap >= Math.Max(spaceRef * 2.6, medianH * 1.4))
                {
                    double rightMed = Median(rightStarts);
                    double tol = Math.Max(3.5, charW * 0.75);
                    int alignedCount = rightStarts.Count(x => Math.Abs(x - rightMed) <= tol);
                    if (alignedCount >= Math.Ceiling(rightStarts.Count * 0.70))
                        accept = true;
                }
            }

            if (accept)
            {
                double splitX = g.CrossingRows == 0
                    ? (g.X0 + g.X1) * 0.5
                    : Math.Max(g.X0, Median(rightStarts) - Math.Min(3.0, minGap * 0.4));
                splits.Add(splitX);
            }
        }

        return splits;
    }

    private static int ColumnIndex(OcrWordBox w, List<double> splits, double charW)
    {
        // Use a point slightly inset from the word's left edge so left-aligned overflowing words
        // stay in the column where they start.
        double anchor = w.X + Math.Min(w.Width * 0.25, charW * 0.5);
        for (int i = 0; i < splits.Count; i++)
        {
            if (anchor < splits[i])
                return i;
        }
        return splits.Count;
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
            return 0;
        var sorted = new List<double>(values);
        sorted.Sort();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) * 0.5;
    }
}
