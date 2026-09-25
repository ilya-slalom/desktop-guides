using System.Security.Cryptography;
using System.Text.Json;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: PdfTextSpike <pdf-path> [password]");
    return 2;
}

string path = Path.GetFullPath(args[0]);
using FileStream source = File.OpenRead(path);
string hash = Convert.ToHexString(await SHA256.HashDataAsync(source)).ToLowerInvariant();
source.Position = 0;
using PdfDocument document = args.Length == 2
    ? PdfDocument.Open(source, new ParsingOptions { Password = args[1] })
    : PdfDocument.Open(source);

var pages = document.GetPages()
    .Select(page =>
    {
        string text = ContentOrderTextExtractor.GetText(page);
        return new
        {
            page = page.Number,
            characters = text.Length,
            preview = text.Length <= 200 ? text : text[..200]
        };
    })
    .ToList();

Console.WriteLine(JsonSerializer.Serialize(new
{
    sha256 = hash,
    pageCount = pages.Count,
    pagesWithText = pages.Count(page => page.characters > 0),
    firstPage = pages.FirstOrDefault()
}, new JsonSerializerOptions { WriteIndented = true }));
return 0;
