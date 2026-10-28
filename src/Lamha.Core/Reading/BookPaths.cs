namespace Lamha.Core.Reading;

/// <summary>Paths inside a book: a link resolved from the file that holds it, as a browser would.</summary>
internal static class BookPaths
{
    /// <summary>
    /// "../images/map.jpg" from "OEBPS/text/ch1.xhtml" → "OEBPS/images/map.jpg"; "#note-3" → the same file and "note-3".
    /// %-escapes are decoded (manifests write "my%20book.xhtml"); ".." never climbs above the book's root.
    /// </summary>
    public static (string Path, string Anchor) Resolve(string from, string href)
    {
        var hash = href.IndexOf('#', StringComparison.Ordinal);
        var anchor = hash >= 0 ? Uri.UnescapeDataString(href[(hash + 1)..]) : "";
        var target = Uri.UnescapeDataString(hash >= 0 ? href[..hash] : href);
        if (target.Length == 0)
        {
            return (from, anchor);
        }

        var parts = new List<string>();
        if (!target.StartsWith('/'))
        {
            parts.AddRange(from.Split('/')[..^1]);
        }

        foreach (var piece in target.Split('/'))
        {
            if (piece == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            else if (piece.Length > 0 && piece != ".")
            {
                parts.Add(piece);
            }
        }

        return (string.Join('/', parts), anchor);
    }

    /// <summary>A web or mail address, kept as it is, rather than a file of the book.</summary>
    public static bool IsExternal(string href)
    {
        var colon = href.IndexOf(':', StringComparison.Ordinal);
        var slash = href.IndexOf('/', StringComparison.Ordinal);
        return colon > 1 && (slash < 0 || colon < slash);
    }
}
