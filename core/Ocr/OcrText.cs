namespace SnapKit;

public readonly record struct OcrPiece(int Line, string Word, string LineText, int WordsInLine, double X = 0, double Y = 0);

public static class OcrText
{
    public static string Join(IReadOnlyList<OcrPiece> words)
    {
        if (words.Count == 0)
            return "";

        var ordered = words
            .GroupBy(word => word.Line)
            .OrderBy(group => group.Min(word => word.Y))
            .ThenBy(group => group.Min(word => word.X))
            .ThenBy(group => group.Key)
            .SelectMany(group => group.OrderBy(word => word.X))
            .ToList();

        var text = new System.Text.StringBuilder();
        int index = 0;
        while (index < ordered.Count)
        {
            int line = ordered[index].Line;
            int end = index + 1;
            while (end < ordered.Count && ordered[end].Line == line)
                end++;

            if (text.Length > 0)
                text.Append('\n');

            int count = end - index;
            var first = ordered[index];
            if (count == first.WordsInLine && first.WordsInLine > 0 && !string.IsNullOrEmpty(first.LineText))
                text.Append(first.LineText.Trim());
            else
            {
                bool any = false;
                for (int i = index; i < end; i++)
                {
                    if (string.IsNullOrEmpty(ordered[i].Word))
                        continue;
                    if (any)
                        text.Append(' ');
                    text.Append(ordered[i].Word);
                    any = true;
                }
            }

            index = end;
        }

        return text.ToString().Trim();
    }
}
