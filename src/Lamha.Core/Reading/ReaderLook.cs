using System.Text.Json;
using System.Text.Json.Serialization;
using Lamha.Core.Abstractions;

namespace Lamha.Core.Reading;

/// <summary>
/// The reader's Aa (docs/design/reader.html): the book's face, size, line spacing, line width and page colour, the same
/// for every book. Kept apart from <c>LamhaSettings</c>, under its own key, because the reader is a process of its own
/// (decision 0022): the tray's Lamha saves its whole copy of the settings, which would undo the reader's changes.
/// </summary>
/// <param name="Font">"sitka" (Windows' reading face), "georgia" or "segoe".</param>
/// <param name="Size">The text size, 14–28 in steps of 2.</param>
/// <param name="LineSpacing">1.5, 1.7 or 2.0 times the size.</param>
/// <param name="Width">The column: "narrow" (520), "normal" (620) or "wide" (760).</param>
/// <param name="Page">The page's colour: "lamha" (the window's own), "paper" or "black".</param>
public sealed record ReaderLook(string Font = "sitka", int Size = 18, double LineSpacing = 1.7, string Width = "normal", string Page = "lamha")
{
    public const int SmallestSize = 14;
    public const int LargestSize = 28;

    public static readonly IReadOnlyList<string> Fonts = ["sitka", "georgia", "segoe"];
    public static readonly IReadOnlyList<double> Spacings = [1.5, 1.7, 2.0];
    public static readonly IReadOnlyList<string> Widths = ["narrow", "normal", "wide"];
    public static readonly IReadOnlyList<string> Pages = ["lamha", "paper", "black"];

    /// <summary>The column's width at 100 %.</summary>
    public double ColumnWidth => Width switch
    {
        "narrow" => 520,
        "wide" => 760,
        _ => 620,
    };

    /// <summary>A size bigger or smaller by one step (A+ and A−), within 14–28.</summary>
    public ReaderLook Resized(int steps) => this with { Size = Math.Clamp(Size + (steps * 2), SmallestSize, LargestSize) };

    /// <summary>Anything unknown or out of range (an older or damaged save) as its default.</summary>
    public ReaderLook Checked()
    {
        var size = Math.Clamp(Size - (Size % 2), SmallestSize, LargestSize);
        return new ReaderLook(
            Fonts.Contains(Font) ? Font : "sitka",
            size,
            Spacings.Contains(LineSpacing) ? LineSpacing : 1.7,
            Widths.Contains(Width) ? Width : "normal",
            Pages.Contains(Page) ? Page : "lamha");
    }
}

/// <summary>The reader's look in the state store ("reader.look").</summary>
public sealed class ReaderLookStore(IStateStore state)
{
    private const string Key = "reader.look";

    public ReaderLook Get()
    {
        try
        {
            return state.Get(Key) is { } json ? (JsonSerializer.Deserialize(json, ReaderLookJson.Default.ReaderLook) ?? new ReaderLook()).Checked() : new ReaderLook();
        }
        catch (JsonException)
        {
            return new ReaderLook();
        }
    }

    public ReaderLook Save(ReaderLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        var checkedLook = look.Checked();
        state.Set(Key, JsonSerializer.Serialize(checkedLook, ReaderLookJson.Default.ReaderLook));
        return checkedLook;
    }
}

/// <summary>The look as JSON, without reflection (NativeAOT).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ReaderLook))]
internal sealed partial class ReaderLookJson : JsonSerializerContext;
