using PDFiumSharp;

namespace PDFMarkup;

public sealed class PdfService
{
    public int GetPageCount(string filePath)
    {
        using var document = new PdfDocument(filePath, null);

        return document.Pages.Count;
    }
}