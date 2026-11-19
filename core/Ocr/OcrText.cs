namespace SnapKit;

public readonly record struct OcrPiece(int Line, string Word, string LineText, int WordsInLine);

public static class OcrText
{
    public static string Join(IReadOnlyList<OcrPiece> words)
    {
        if (words.Count == 0)
            return "";

        var text = new System.Text.StringBuilder();
        int index = 0;
        while (index < words.Count)
        {
            int line = words[index].Line;
            int end = index + 1;
            while (end < words.Count && words[end].Line == line)
                end++;

            if (text.Length > 0)
                text.Append('\n');

            int count = end - index;
            var first = words[index];
            if (count == first.WordsInLine && first.WordsInLine > 0 && !string.IsNullOrEmpty(first.LineText))
                text.Append(first.LineText.Trim());
            else
            {
                bool any = false;
                for (int i = index; i < end; i++)
                {
                    if (string.IsNullOrEmpty(words[i].Word))
                        continue;
                    if (any)
                        text.Append(' ');
                    text.Append(words[i].Word);
                    any = true;
                }
            }

            index = end;
        }

        return text.ToString().Trim();
    }
}
