using PDFiumSharp;
using PDFiumSharp.Types;
using System;

namespace PdfDisplayTest;

public sealed class PdfService
{
    public int GetPageCount(string filePath)
    {
        using var document = new PdfDocument(filePath, null);

        return document.Pages.Count;
    }

    public string RenderFirstPageToBmp(string filePath)
    {
        using var document = new PdfDocument(filePath, null);
        using var page = document.Pages[0];

        const double scale = 2.0;

        int width = Math.Max(
            1,
            (int)Math.Round(page.Width * scale));

        int height = Math.Max(
            1,
            (int)Math.Round(page.Height * scale));

        using var bitmap = new PDFiumBitmap(
            width,
            height,
            false);

        bitmap.Fill(
            new FPDF_COLOR(255, 255, 255, 255));

        // 向きや描画フラグは既定値を使用
        page.Render(bitmap);

        string outputPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(filePath)!,
            "page1.bmp");

        bitmap.Save(outputPath, 144, 144);

        return outputPath;
    }
}