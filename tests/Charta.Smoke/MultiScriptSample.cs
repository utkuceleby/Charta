namespace Charta.Smoke;

/// <summary>
/// Multi-script showcase using system fonts (never golden — output depends on the machine's fonts).
/// One block per script; Arabic/Hebrew intentionally included to exercise the loud-degradation path.
/// </summary>
internal static class MultiScriptSample
{
    public static Document? Build()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var fonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        var arial = Path.Combine(fonts, "arial.ttf");
        if (!File.Exists(arial))
        {
            return null;
        }

        FontManager.RegisterFontFile(arial);
        var gothic = Path.Combine(fonts, "msgothic.ttc");
        var hasCjk = File.Exists(gothic);
        if (hasCjk)
        {
            FontManager.RegisterFontFile(gothic);
        }

        return Document.Create(doc =>
        {
            doc.Metadata(m => m.Title("Çok dilli örnek — многоязычный образец"));
            doc.Page(page =>
            {
                page.Margin(50);
                page.Content().Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Text("Türkçe: Pijamalı hasta yağız şoföre çabucak güvendi. ĞÜŞÖÇİ ğüşöçı").FontFamily("Arial");
                    col.Item().Text("Русский: Съешь ещё этих мягких французских булок, да выпей же чаю").FontFamily("Arial");
                    col.Item().Text("Ελληνικά: Ξεσκεπάζω την ψυχοφθόρα βδελυγμία").FontFamily("Arial");
                    col.Item().Text("Polski: Zażółć gęślą jaźń").FontFamily("Arial");
                    col.Item().Text("العربية: مرحبا بالعالم").FontFamily("Arial");
                    col.Item().Text("עברית: שלום עולם").FontFamily("Arial");
                    if (hasCjk)
                    {
                        col.Item().Text("日本語:日本語のテキストは正しく折り返されます").FontFamily("MS Gothic");
                    }
                });
            });
        });
    }
}
