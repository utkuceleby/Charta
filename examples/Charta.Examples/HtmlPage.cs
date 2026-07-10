using Charta.Html;

namespace Charta.Examples;

/// <summary>Renders a styled HTML fragment to PDF with the Charta.Html add-on.</summary>
public static class HtmlPage
{
    private const string Markup = """
        <style>
          body { font-family: Arial; color: #222; line-height: 1.4 }
          h1 { color: #003366; font-size: 26px }
          h2 { color: #003366; font-size: 16px; border-bottom: 1px solid #cccccc }
          .lead { font-size: 13px; color: #555 }
          table { width: 100% }
          th { background: #003366; color: white; text-align: left }
          td, th { padding: 5px }
          .num { text-align: right }
          .muted { color: #888; font-size: 10px }
        </style>

        <h1>Charta.Html</h1>
        <p class="lead">A subset of <b>HTML</b> and <i>CSS</i>, rendered to PDF by
        Charta's own cascade and layout engine &mdash; no browser, no native code.</p>

        <h2>What it maps</h2>
        <ul>
          <li>Block flow: headings, paragraphs, <code>div</code>, blockquote, <code>pre</code></li>
          <li>Inline styling: bold, italic, <u>underline</u>, <s>strike</s>,
              color, size, super<sup>2</sup> and sub<sub>3</sub>, links</li>
          <li>Lists, tables with <b>colspan</b>/<b>rowspan</b>, rules, and images</li>
        </ul>

        <h2>An example table</h2>
        <table>
          <thead><tr><th>Item</th><th class="num">Qty</th><th class="num">Total</th></tr></thead>
          <tbody>
            <tr><td>Design consultation</td><td class="num">12</td><td class="num">1,020.00</td></tr>
            <tr><td>Implementation sprint</td><td class="num">3</td><td class="num">4,350.00</td></tr>
            <tr><td colspan="2"><b>Grand total</b></td><td class="num"><b>5,370.00</b></td></tr>
          </tbody>
        </table>

        <hr>
        <p class="muted">Anything outside the supported subset is reported as a diagnostic, never thrown.</p>
        """;

    public static void Generate(string path)
    {
        var unsupported = new List<string>();
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimeter);
            page.Content().Html(Markup, new HtmlRenderOptions
            {
                BaseFontFamily = "Arial",
                OnUnsupported = unsupported.Add,
            });
        })).GeneratePdf(path);

        foreach (var message in unsupported)
        {
            Console.WriteLine($"  html: unsupported — {message}");
        }
    }
}
