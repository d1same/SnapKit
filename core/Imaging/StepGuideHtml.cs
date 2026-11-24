using System.Net;
using System.Text;

namespace SnapKit;

public readonly record struct StepGuideExportItem(int Number, string Caption, byte[] PngBytes, int Width, int Height);

/// <summary>
/// Builds clean, self-contained HTML documents and Windows CF_HTML clipboard payloads for
/// Step-by-Step Client Guides so they paste cleanly into Outlook, Word, Notion, and PSA ticket portals.
/// </summary>
public static class StepGuideHtml
{
    public static string BuildDocument(string title, IReadOnlyList<StepGuideExportItem> steps, bool darkTheme = false)
    {
        string bg = darkTheme ? "#151515" : "#FFFFFF";
        string cardBg = darkTheme ? "#222222" : "#F8FAFC";
        string border = darkTheme ? "#2E2E2E" : "#E2E8F0";
        string ink = darkTheme ? "#F2F2F2" : "#0F172A";
        string muted = darkTheme ? "#8A8A8A" : "#64748B";
        string safeTitle = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? "Step-by-Step Guide" : title.Trim());

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>{safeTitle}</title>");
        sb.AppendLine("</head>");
        sb.AppendLine($"<body style=\"margin:0;padding:24px;background:{bg};color:{ink};font-family:'Segoe UI',Arial,sans-serif;\">");
        sb.AppendLine("<div style=\"max-width:860px;margin:0 auto;\">");
        sb.AppendLine($"<h1 style=\"margin:0 0 6px 0;font-size:22px;font-weight:700;color:{ink};\">{safeTitle}</h1>");
        sb.AppendLine($"<p style=\"margin:0 0 20px 0;font-size:12px;color:{muted};\">{steps.Count} {(steps.Count == 1 ? "step" : "steps")}</p>");

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            string cap = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(step.Caption) ? $"Step {step.Number}" : step.Caption.Trim());
            string b64 = Convert.ToBase64String(step.PngBytes);
            sb.AppendLine($"<div style=\"margin:0 0 18px 0;padding:14px 16px;background:{cardBg};border:1px solid {border};border-radius:10px;\">");
            sb.AppendLine($"<div style=\"margin:0 0 10px 0;font-size:15px;font-weight:600;color:{ink};\">" +
                          $"<span style=\"display:inline-block;width:24px;height:24px;line-height:24px;text-align:center;border-radius:12px;background:#FF3B30;color:#FFFFFF;font-size:13px;font-weight:700;margin-right:10px;\">{step.Number}</span>" +
                          $"{cap}</div>");
            sb.AppendLine($"<img src=\"data:image/png;base64,{b64}\" width=\"{Math.Min(820, Math.Max(1, step.Width))}\" style=\"max-width:100%;height:auto;display:block;border-radius:6px;border:1px solid {border};\" alt=\"Step {step.Number}\" />");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }

    public static string BuildPlainText(string title, IReadOnlyList<StepGuideExportItem> steps)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title))
            sb.AppendLine(title.Trim()).AppendLine();
        foreach (var step in steps)
        {
            string cap = string.IsNullOrWhiteSpace(step.Caption) ? $"Step {step.Number}" : step.Caption.Trim();
            sb.Append(step.Number).Append(". ").AppendLine(cap);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Wraps an HTML fragment in the exact Windows CF_HTML clipboard header (UTF-8 byte offsets).
    /// </summary>
    public static string BuildClipboardCfHtml(string title, IReadOnlyList<StepGuideExportItem> steps, bool darkTheme = false)
    {
        var body = new StringBuilder();
        string ink = darkTheme ? "#F2F2F2" : "#0F172A";
        string border = darkTheme ? "#2E2E2E" : "#E2E8F0";
        string safeTitle = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? "Step-by-Step Guide" : title.Trim());

        body.Append($"<div style=\"font-family:'Segoe UI',Arial,sans-serif;color:{ink};\">");
        body.Append($"<h2 style=\"margin:0 0 12px 0;font-size:18px;\">{safeTitle}</h2>");
        foreach (var step in steps)
        {
            string cap = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(step.Caption) ? $"Step {step.Number}" : step.Caption.Trim());
            string b64 = Convert.ToBase64String(step.PngBytes);
            body.Append($"<p style=\"margin:14px 0 6px 0;font-size:14px;font-weight:600;\"><b>{step.Number}.</b> {cap}</p>");
            body.Append($"<p style=\"margin:0 0 12px 0;\"><img src=\"data:image/png;base64,{b64}\" width=\"{Math.Min(780, Math.Max(1, step.Width))}\" style=\"max-width:100%;height:auto;border:1px solid {border};\" /></p>");
        }
        body.Append("</div>");

        const string headerTemplate =
            "Version:0.9\r\n" +
            "StartHTML:{0:D10}\r\n" +
            "EndHTML:{1:D10}\r\n" +
            "StartFragment:{2:D10}\r\n" +
            "EndFragment:{3:D10}\r\n";

        const string prefix = "<html><body><!--StartFragment-->";
        const string suffix = "<!--EndFragment--></body></html>";

        string fragment = body.ToString();
        int headerLen = Encoding.UTF8.GetByteCount(string.Format(headerTemplate, 0, 0, 0, 0));
        int prefixLen = Encoding.UTF8.GetByteCount(prefix);
        int fragLen = Encoding.UTF8.GetByteCount(fragment);
        int suffixLen = Encoding.UTF8.GetByteCount(suffix);

        int startHtml = headerLen;
        int startFragment = headerLen + prefixLen;
        int endFragment = startFragment + fragLen;
        int endHtml = endFragment + suffixLen;

        return string.Format(headerTemplate, startHtml, endHtml, startFragment, endFragment) + prefix + fragment + suffix;
    }
}
