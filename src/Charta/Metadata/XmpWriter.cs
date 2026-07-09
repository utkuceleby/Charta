using System.Globalization;
using System.Text;

namespace Charta.Metadata;

/// <summary>
/// Builds the XMP metadata packet (ISO 16684-1) mirroring the Info dictionary. Designed as the
/// attachment point for the pdfaid schema when PDF/A support lands — the packet structure and the
/// catalog wiring stay the same.
/// </summary>
internal static class XmpWriter
{
    public static byte[] Build(DocumentMetadata metadata) => Build(metadata, pdfAConformance: null);

    /// <summary>
    /// Builds the XMP packet. When <paramref name="pdfAConformance"/> is set (e.g. "2B"), the pdfaid
    /// schema is included so validators recognize the PDF/A level.
    /// </summary>
    public static byte[] Build(DocumentMetadata metadata, string? pdfAConformance)
    {
        var sb = new StringBuilder();
        sb.Append("<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n");
        sb.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n");
        sb.Append(" <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n");
        sb.Append("  <rdf:Description rdf:about=\"\"\n");
        sb.Append("    xmlns:dc=\"http://purl.org/dc/elements/1.1/\"\n");
        sb.Append("    xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"\n");
        sb.Append("    xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\"\n");
        sb.Append("    xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\">\n");

        if (pdfAConformance is not null)
        {
            var part = pdfAConformance[..^1];
            var conformance = pdfAConformance[^1..].ToUpperInvariant();
            sb.Append("   <pdfaid:part>").Append(part).Append("</pdfaid:part>\n");
            sb.Append("   <pdfaid:conformance>").Append(conformance).Append("</pdfaid:conformance>\n");
        }

        if (metadata.Title is { } title)
        {
            sb.Append("   <dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">")
                .Append(Escape(title))
                .Append("</rdf:li></rdf:Alt></dc:title>\n");
        }

        if (metadata.Author is { } author)
        {
            sb.Append("   <dc:creator><rdf:Seq><rdf:li>")
                .Append(Escape(author))
                .Append("</rdf:li></rdf:Seq></dc:creator>\n");
        }

        if (metadata.Subject is { } subject)
        {
            sb.Append("   <dc:description><rdf:Alt><rdf:li xml:lang=\"x-default\">")
                .Append(Escape(subject))
                .Append("</rdf:li></rdf:Alt></dc:description>\n");
        }

        if (metadata.Keywords is { } keywords)
        {
            sb.Append("   <pdf:Keywords>").Append(Escape(keywords)).Append("</pdf:Keywords>\n");
        }

        if (metadata.Creator is { } creator)
        {
            sb.Append("   <xmp:CreatorTool>").Append(Escape(creator)).Append("</xmp:CreatorTool>\n");
        }

        if (metadata.CreationDate is { } date)
        {
            sb.Append("   <xmp:CreateDate>")
                .Append(date.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture))
                .Append("</xmp:CreateDate>\n");
        }

        sb.Append("   <pdf:Producer>Charta</pdf:Producer>\n");
        sb.Append("  </rdf:Description>\n");
        sb.Append(" </rdf:RDF>\n");
        sb.Append("</x:xmpmeta>\n");
        sb.Append("<?xpacket end=\"w\"?>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
