using Charta.Examples;

// Generates every example into ./output. Examples use fonts installed on this machine
// (Arial with a Segoe UI fallback); on servers, register fonts explicitly instead —
// see FontManager.RegisterFont.

var outputDirectory = args.Length > 0 ? args[0] : "output";
Directory.CreateDirectory(outputDirectory);

Invoice.Generate(Path.Combine(outputDirectory, "invoice.pdf"));
Report.Generate(Path.Combine(outputDirectory, "report.pdf"));
HtmlPage.Generate(Path.Combine(outputDirectory, "html-page.pdf"));

Console.WriteLine($"Examples written to {Path.GetFullPath(outputDirectory)}");
