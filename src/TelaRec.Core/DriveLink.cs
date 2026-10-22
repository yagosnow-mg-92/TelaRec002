using System.Text.RegularExpressions;

namespace TelaRec.Core;

public static class DriveLink
{
    private static readonly Regex Folders = new(@"/folders/([A-Za-z0-9_-]{10,})", RegexOptions.Compiled);
    private static readonly Regex IdParam = new(@"[?&]id=([A-Za-z0-9_-]{10,})", RegexOptions.Compiled);
    private static readonly Regex Raw = new(@"^[A-Za-z0-9_-]{10,}$", RegexOptions.Compiled);

    /// <summary>Extrai o ID da pasta de um link do Drive (ou aceita o ID puro).</summary>
    public static string? ParseFolderId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        var m = Folders.Match(s);
        if (m.Success) return m.Groups[1].Value;
        m = IdParam.Match(s);
        if (m.Success) return m.Groups[1].Value;
        return Raw.IsMatch(s) ? s : null;
    }

    public static bool IsScriptUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        url.Trim().StartsWith("https://script.google.com/", StringComparison.OrdinalIgnoreCase) &&
        url.Contains("/exec", StringComparison.OrdinalIgnoreCase);
}
