using System.Security.Cryptography;
using Charta.Cos;

// AOT/JIT parity smoke tool: writes the M0 documents and prints their SHA-256 hashes.
// CI publishes this with NativeAOT, runs both variants, and compares the hashes byte for byte.
// Also serves as the golden-file generator: pass a target directory to (re)write the golden PDFs.

var targetDirectory = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
Directory.CreateDirectory(targetDirectory);

Emit("hello-classic.pdf", new PdfWriterOptions { XrefMode = XrefMode.Classic, CompressStreams = false });
Emit("hello-xrefstream.pdf", new PdfWriterOptions { XrefMode = XrefMode.Stream, CompressStreams = false });
Emit("hello-compressed.pdf", new PdfWriterOptions { XrefMode = XrefMode.Stream, CompressStreams = true });

void Emit(string fileName, PdfWriterOptions options)
{
    var path = Path.Combine(targetDirectory, fileName);
    using (var file = File.Create(path))
    {
        HelloPdf.Write(file, options);
    }

    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    Console.WriteLine($"{fileName} {hash}");
}
