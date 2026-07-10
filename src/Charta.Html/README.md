# Charta.Html

HTML/CSS **subset** rendering for [Charta](https://www.nuget.org/packages/Charta) — turn a fragment of
HTML into a laid-out PDF. Parsing is done by [AngleSharp](https://anglesharp.io/); the CSS cascade and
the layout are Charta's own. There is no browser and no native code.

```csharp
using Charta;
using Charta.Html;

Document.Create(doc => doc.Page(page =>
{
    page.Margin(2, Unit.Centimeter);
    page.Content().Html("""
        <style>h1 { color: #003366 } .lead { color: #555 }</style>
        <h1>Hello</h1>
        <p class="lead">A <b>bold</b> word, an <i>italic</i> one, and a
        <a href="https://example.com">link</a>.</p>
        <ul><li>first</li><li>second</li></ul>
        """);
})).GeneratePdf("page.pdf");
```

## What it maps

| Area | Supported |
|---|---|
| **Block flow** | `h1`–`h6`, `p`, `div`, `section`/`article`/`header`/`footer`/`main`/`nav`/`figure`, `blockquote`, `pre`, `hr` |
| **Inline** | `b`/`strong`, `i`/`em`, `u`, `s`/`del`, `span`, `a`, `sub`, `sup`, `small`, `code`, `br` |
| **Lists** | `ul`, `ol`, `li` with disc/circle/square/decimal markers |
| **Tables** | `table`, `thead`, `tbody`, `tr`, `th`, `td`, including `colspan`/`rowspan` |
| **Images** | `img` from `data:` URIs or file paths (block-level) |
| **Flexbox** | `display: flex` with `flex-direction`, `flex`/`flex-grow`, and `gap` — mapped to a row or column |
| **Selectors** | type, `.class`, `#id`, `*`, and comma groups — from `<style>` blocks and inline `style` |
| **Properties** | `display`, `color`, `background-color`, `font-size/family/weight/style`, `text-align`, `text-decoration`, `text-transform`, `white-space` (`pre`), `letter-spacing`, `line-height`, `vertical-align`, `width`, `margin`, `padding`, `border`, `list-style-type`, and the flex properties above |

## What it does not

No combinators (`>`, `+`, descendant), pseudo-classes, at-rules (`@media`, `@font-face`), grid,
floats, positioning, percentage widths, flex alignment/justification, or the `font` shorthand. **These never throw** — each distinct
unsupported feature is reported once through `HtmlRenderOptions.OnUnsupported` and skipped:

```csharp
page.Content().Html(markup, new HtmlRenderOptions
{
    BaseFontFamily = "Arial",
    OnUnsupported = message => Console.WriteLine(message),
});
```

Register the fonts you use with `FontManager.RegisterFont` for deterministic, server-side output.

MIT licensed, forever.
