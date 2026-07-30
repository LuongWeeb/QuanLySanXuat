using QuestPDF.Drawing;

namespace WmsMes.Web.Services;

public static class PdfFontRegistration
{
    public const string FontFamilyName = "WmsMes Noto Sans";
    public const string RelativeFontPath = "Assets/Fonts/NotoSans-Variable.ttf";

    public static void RegisterFromAppBaseDirectory()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, RelativeFontPath);
        using var fontStream = File.OpenRead(fontPath);
        FontManager.RegisterFontWithCustomName(FontFamilyName, fontStream);
    }
}
