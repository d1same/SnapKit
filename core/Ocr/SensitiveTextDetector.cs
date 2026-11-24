using System.Text.RegularExpressions;

namespace SnapKit;

public enum SensitiveKind
{
    Email,
    IpAddress,
    Phone,
    SecretToken,
    CreditCard,
    Ssn,
    LabeledSecret
}

public readonly record struct SensitiveMatch(SensitiveKind Kind, double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>
/// Offline pattern detector that inspects OCR word bounding boxes to locate sensitive MSP/client data
/// (emails, IP addresses, phone numbers, credit cards, SSNs, API tokens, and labeled passwords/secrets)
/// for one-click Blur or Redact placement.
/// </summary>
public static partial class SensitiveTextDetector
{
    [GeneratedRegex(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}$", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?::\d{1,5}|/\d{1,2})?$", RegexOptions.Compiled)]
    private static partial Regex Ipv4Regex();

    [GeneratedRegex(@"^\d{3}-\d{2}-\d{4}$", RegexOptions.Compiled)]
    private static partial Regex SsnRegex();

    [GeneratedRegex(@"^(?:\+?\d{1,2}[\s.\-]?)?(?:\(\d{3}\)|\d{3})[\s.\-]?\d{3}[\s.\-]?\d{4}$", RegexOptions.Compiled)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^(?:sk|pk|rk|whsec|ghp|gho|ghu|ghs|glpat|xoxb|xoxp|xoxa|AKIA|ASIA|AIza|SG\.|eyJ)[A-Za-z0-9_\-\.=]{8,}$", RegexOptions.Compiled)]
    private static partial Regex KnownTokenPrefixRegex();

    [GeneratedRegex(@"(?:api[_\-]?key|token|secret|password|passwd|pwd|sig|signature|auth)=([^\s&]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex QuerySecretRegex();

    private static readonly HashSet<string> SecretLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password:", "passwd", "passwd:", "pwd", "pwd:", "pass", "pass:",
        "secret", "secret:", "token", "token:", "apikey", "apikey:", "api_key", "api_key:",
        "pin", "pin:", "ssn", "ssn:", "passcode", "passcode:", "key:", "privatekey:"
    };

    public static IReadOnlyList<SensitiveMatch> Find(IReadOnlyList<OcrWordBox> input)
    {
        var words = input
            .Where(w => !string.IsNullOrWhiteSpace(w.Text) && w.Width > 0 && w.Height > 0)
            .ToList();
        if (words.Count == 0)
            return Array.Empty<SensitiveMatch>();

        double medianH = Math.Max(6.0, MedianHeight(words));
        var rows = GroupVisualRows(words, medianH);
        var raw = new List<SensitiveMatch>();

        foreach (var row in rows)
        {
            var matched = new bool[row.Count];

            // Pass 1: single-word sensitive values (emails, IPs, SSNs, tokens, compact phones/cards)
            for (int i = 0; i < row.Count; i++)
            {
                var w = row[i];
                string clean = TrimEdgePunctuation(w.Text);
                if (TryClassifySingleWord(clean, out var kind))
                {
                    raw.Add(Pad(kind, w));
                    matched[i] = true;
                }
            }

            // Pass 2: multi-word sequences on the same row (split phone numbers, 4x4 credit cards)
            for (int i = 0; i < row.Count; i++)
            {
                if (matched[i])
                    continue;

                // 4-group credit card: e.g. "4532" "0192" "8374" "1122"
                if (i + 3 < row.Count && IsAdjacentSequence(row, i, 4, medianH))
                {
                    string joined4 = string.Concat(row[i].Text, row[i + 1].Text, row[i + 2].Text, row[i + 3].Text);
                    if (LooksLikeCreditCard(joined4))
                    {
                        raw.Add(MergeSpan(SensitiveKind.CreditCard, row, i, i + 3));
                        for (int k = i; k <= i + 3; k++) matched[k] = true;
                        i += 3;
                        continue;
                    }
                }

                // 2- or 3-word phone numbers: "(555)" "123-4567" or "555" "123" "4567" or "+1" "555" "123-4567"
                for (int len = 3; len >= 2; len--)
                {
                    if (i + len > row.Count || !IsAdjacentSequence(row, i, len, medianH))
                        continue;
                    string candidate = string.Join(" ", Enumerable.Range(i, len).Select(k => TrimEdgePunctuation(row[k].Text)));
                    if (PhoneRegex().IsMatch(candidate))
                    {
                        raw.Add(MergeSpan(SensitiveKind.Phone, row, i, i + len - 1));
                        for (int k = i; k < i + len; k++) matched[k] = true;
                        i += len - 1;
                        break;
                    }
                }
            }

            // Pass 3: labeled secrets (e.g. "Password:" followed by value on same row)
            for (int i = 0; i < row.Count - 1; i++)
            {
                string lbl = row[i].Text.Trim();
                bool isSecretLabel = SecretLabels.Contains(lbl)
                    || (i > 0 && (row[i].Text.Equals("key:", StringComparison.OrdinalIgnoreCase)
                                  || row[i].Text.Equals("secret:", StringComparison.OrdinalIgnoreCase)
                                  || row[i].Text.Equals("token:", StringComparison.OrdinalIgnoreCase)));
                if (!isSecretLabel)
                    continue;

                var val = row[i + 1];
                double gap = val.X - row[i].Right;
                // Only match if value is reasonably close to label on the same row and not another label
                if (gap >= -2 && gap <= medianH * 14 && !val.Text.EndsWith(':') && !matched[i + 1])
                {
                    raw.Add(Pad(SensitiveKind.LabeledSecret, val));
                    matched[i + 1] = true;
                }
            }
        }

        return MergeOverlapping(raw);
    }

    private static bool TryClassifySingleWord(string clean, out SensitiveKind kind)
    {
        if (clean.Length < 4)
        {
            kind = default;
            return false;
        }
        if (EmailRegex().IsMatch(clean))
        {
            kind = SensitiveKind.Email;
            return true;
        }
        if (Ipv4Regex().IsMatch(clean))
        {
            kind = SensitiveKind.IpAddress;
            return true;
        }
        if (SsnRegex().IsMatch(clean))
        {
            kind = SensitiveKind.Ssn;
            return true;
        }
        if (PhoneRegex().IsMatch(clean))
        {
            kind = SensitiveKind.Phone;
            return true;
        }
        if (LooksLikeCreditCard(clean))
        {
            kind = SensitiveKind.CreditCard;
            return true;
        }
        if (KnownTokenPrefixRegex().IsMatch(clean) || QuerySecretRegex().IsMatch(clean) || LooksLikeHighEntropyToken(clean))
        {
            kind = SensitiveKind.SecretToken;
            return true;
        }
        kind = default;
        return false;
    }

    private static bool LooksLikeHighEntropyToken(string s)
    {
        // Avoid false-positives on normal URLs, file paths, or long hyphenated English phrases.
        if (s.Length < 22 || s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;
        if (s.Contains('\\') || s.Contains('/'))
            return false;

        int upper = 0, lower = 0, digits = 0, other = 0;
        foreach (char ch in s)
        {
            if (char.IsUpper(ch)) upper++;
            else if (char.IsLower(ch)) lower++;
            else if (char.IsDigit(ch)) digits++;
            else if (ch is '_' or '-' or '=' or '.') other++;
            else return false;
        }

        // Must look like a generated hex/base64/token key (not plain CamelCase identifier)
        bool isHex = digits >= 4 && (upper + lower + digits == s.Length) && s.All(c => Uri.IsHexDigit(c));
        bool isMixed = digits >= 4 && (upper >= 2 && lower >= 2);
        return isHex || isMixed;
    }

    private static bool LooksLikeCreditCard(string text)
    {
        var digits = new List<int>(20);
        foreach (char ch in text)
        {
            if (char.IsDigit(ch))
                digits.Add(ch - '0');
            else if (ch is not ('-' or ' '))
                return false;
        }
        if (digits.Count < 13 || digits.Count > 19)
            return false;

        // Luhn checksum prevents random long IDs from triggering false positives.
        int sum = 0;
        bool dbl = false;
        for (int i = digits.Count - 1; i >= 0; i--)
        {
            int d = digits[i];
            if (dbl)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            dbl = !dbl;
        }
        return sum % 10 == 0;
    }

    private static string TrimEdgePunctuation(string text)
    {
        return text.Trim().Trim(',', ';', '<', '>', '[', ']', '"', '\'');
    }

    private static bool IsAdjacentSequence(List<OcrWordBox> row, int start, int count, double medianH)
    {
        double maxGap = Math.Max(medianH * 1.4, 10.0);
        for (int i = start; i < start + count - 1; i++)
        {
            double gap = row[i + 1].X - row[i].Right;
            if (gap < -2 || gap > maxGap)
                return false;
        }
        return true;
    }

    private static SensitiveMatch Pad(SensitiveKind kind, OcrWordBox w)
    {
        double padX = Math.Max(3.0, w.Height * 0.25);
        double padY = Math.Max(2.0, w.Height * 0.15);
        return new SensitiveMatch(kind, Math.Max(0, w.X - padX), Math.Max(0, w.Y - padY), w.Width + padX * 2, w.Height + padY * 2);
    }

    private static SensitiveMatch MergeSpan(SensitiveKind kind, List<OcrWordBox> row, int start, int end)
    {
        double x0 = row[start].X;
        double y0 = row[start].Y;
        double x1 = row[start].Right;
        double y1 = row[start].Bottom;
        for (int i = start + 1; i <= end; i++)
        {
            x0 = Math.Min(x0, row[i].X);
            y0 = Math.Min(y0, row[i].Y);
            x1 = Math.Max(x1, row[i].Right);
            y1 = Math.Max(y1, row[i].Bottom);
        }
        double h = Math.Max(4, y1 - y0);
        double padX = Math.Max(3.0, h * 0.25);
        double padY = Math.Max(2.0, h * 0.15);
        return new SensitiveMatch(kind, Math.Max(0, x0 - padX), Math.Max(0, y0 - padY), (x1 - x0) + padX * 2, h + padY * 2);
    }

    private static List<SensitiveMatch> MergeOverlapping(List<SensitiveMatch> items)
    {
        if (items.Count <= 1)
            return items;

        var sorted = items.OrderBy(m => m.Y).ThenBy(m => m.X).ToList();
        var merged = new List<SensitiveMatch>();
        foreach (var m in sorted)
        {
            int hit = -1;
            for (int i = 0; i < merged.Count; i++)
            {
                var cur = merged[i];
                bool yOverlap = Math.Min(cur.Bottom, m.Bottom) - Math.Max(cur.Y, m.Y) > 0;
                bool xClose = m.X <= cur.Right + 4 && cur.X <= m.Right + 4;
                if (yOverlap && xClose)
                {
                    hit = i;
                    break;
                }
            }
            if (hit < 0)
            {
                merged.Add(m);
            }
            else
            {
                var cur = merged[hit];
                double x0 = Math.Min(cur.X, m.X);
                double y0 = Math.Min(cur.Y, m.Y);
                double x1 = Math.Max(cur.Right, m.Right);
                double y1 = Math.Max(cur.Bottom, m.Bottom);
                merged[hit] = new SensitiveMatch(cur.Kind, x0, y0, x1 - x0, y1 - y0);
            }
        }
        return merged;
    }

    private static List<List<OcrWordBox>> GroupVisualRows(List<OcrWordBox> words, double medianH)
    {
        var sorted = words.OrderBy(w => w.CenterY).ThenBy(w => w.X).ToList();
        double tol = Math.Max(medianH * 0.55, 4.0);
        var rows = new List<List<OcrWordBox>>();
        var centers = new List<double>();

        foreach (var w in sorted)
        {
            int best = -1;
            double bestDist = double.MaxValue;
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                double d = Math.Abs(w.CenterY - centers[i]);
                if (d > medianH * 2.2)
                    break;
                if (d <= tol && d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            if (best >= 0)
            {
                rows[best].Add(w);
                centers[best] = rows[best].Average(x => x.CenterY);
            }
            else
            {
                rows.Add(new List<OcrWordBox> { w });
                centers.Add(w.CenterY);
            }
        }

        foreach (var r in rows)
            r.Sort((a, b) => a.X.CompareTo(b.X));
        return rows;
    }

    private static double MedianHeight(List<OcrWordBox> words)
    {
        var hs = words.Select(w => w.Height).OrderBy(h => h).ToList();
        int m = hs.Count / 2;
        return hs.Count % 2 == 1 ? hs[m] : (hs[m - 1] + hs[m]) * 0.5;
    }
}
