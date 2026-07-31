using PDFiumSharp;
using PDFiumSharp.Types;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Drawing;
using System;
using System.IO;
using System.Reflection;

using PdfSharpDocument = PdfSharp.Pdf.PdfDocument;

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

        string outputPath = Path.Combine(
            Path.GetDirectoryName(filePath)!,
            "page1.bmp");

        bitmap.Save(outputPath, 144, 144);

        return outputPath;
    }

    public string AddTestAnnotation(string filePath)
    {
        string directory =
            Path.GetDirectoryName(filePath)!;

        string fileName =
            Path.GetFileNameWithoutExtension(filePath);

        string outputPath = Path.Combine(
            directory,
            $"{fileName}_annotated.pdf");

        using PdfSharpDocument document =
            PdfReader.Open(
                filePath,
                PdfDocumentOpenMode.Modify);

        if (document.PageCount == 0)
        {
            throw new InvalidOperationException(
                "PDFにページがありません。");
        }

        var page = document.Pages[0];

        var annotation = new PdfTextAnnotation
        {
            Title = "PDFChecker",
            Subject = "PoC-02",
            Contents = "PDFCheckerから追加したテスト注釈です。",
            Open = true
        };

        annotation.Rectangle =
            new PdfSharp.Pdf.PdfRectangle(
                new XRect(50, 50, 40, 40));

        page.Annotations.Add(annotation);

        document.Save(outputPath);

        return outputPath;
    }

    public string ReadAnnotations(string filePath)
    {
        using PdfSharpDocument document =
            PdfReader.Open(
                filePath,
                PdfDocumentOpenMode.Import);

        var results = new System.Text.StringBuilder();

        for (
            int pageIndex = 0;
            pageIndex < document.PageCount;
            pageIndex++)
        {
            var page = document.Pages[pageIndex];

            results.AppendLine(
                $"ページ {pageIndex + 1}: " +
                $"注釈数 {page.Annotations.Count}");

            for (
                int annotationIndex = 0;
                annotationIndex < page.Annotations.Count;
                annotationIndex++)
            {
                var annotation =
                    page.Annotations[annotationIndex];

                string subtype =
                    annotation.Elements.GetName("/Subtype");

                string title =
                    annotation.Elements.GetString("/T");

                string contents =
                    annotation.Elements.GetString("/Contents");

                var rectangle =
                    annotation.Rectangle;

                results.AppendLine(
                    $"  注釈 {annotationIndex + 1}");

                results.AppendLine(
                    $"  種類: {subtype}");

                results.AppendLine(
                    $"  作成者: {title}");

                results.AppendLine(
                    $"  コメント: {contents}");

                results.AppendLine(
                    $"  座標: {rectangle}");
            }
        }

        return results.ToString();
    }

    public string ExtractFirstPageText(string filePath)
    {
        using var document =
            new PdfDocument(filePath, null);

        using var page =
            document.Pages[0];

        var textPage =
            PDFium.FPDFText_LoadPage(page.Handle);

        if (textPage.IsNull)
        {
            throw new InvalidOperationException(
                "PDFの文字情報を読み込めませんでした。");
        }

        try
        {
            int charCount =
                PDFium.FPDFText_CountChars(textPage);

            if (charCount <= 0)
            {
                return "文字情報は取得できませんでした。";
            }

            var buffer =
                new ushort[charCount + 1];

            string text =
                PDFium.FPDFText_GetText(
                    textPage,
                    0,
                    charCount);

            if (string.IsNullOrEmpty(text))
            {
                return "文字情報は取得できませんでした。";
            }

            return text;
        }
        finally
        {
            PDFium.FPDFText_ClosePage(textPage);
        }
    }

    public string ExtractFirstPageCharacterPositions(
        string filePath)
    {
        using var document =
            new PdfDocument(filePath, null);

        using var page =
            document.Pages[0];

        var textPage =
            PDFium.FPDFText_LoadPage(page.Handle);

        if (textPage.IsNull)
        {
            throw new InvalidOperationException(
                "PDFの文字情報を読み込めませんでした。");
        }

        try
        {
            int charCount =
                PDFium.FPDFText_CountChars(textPage);

            if (charCount <= 0)
            {
                return "文字情報は取得できませんでした。";
            }

            var results =
                new System.Text.StringBuilder();

            int checkCount =
                Math.Min(charCount, 100);

            results.AppendLine(
                $"文字数: {charCount}");

            results.AppendLine(
                $"座標表示数: {checkCount}");

            results.AppendLine();

            for (
                int index = 0;
                index < checkCount;
                index++)
            {
                PDFium.FPDFText_GetCharBox(
                    textPage,
                    index,
                    out double left,
                    out double right,
                    out double bottom,
                    out double top);

                results.AppendLine(
                    $"{index}: " +
                    $"L={left:F2}, " +
                    $"R={right:F2}, " +
                    $"B={bottom:F2}, " +
                    $"T={top:F2}");
            }

            return results.ToString();
        }
        finally
        {
            PDFium.FPDFText_ClosePage(textPage);
        }
    }

    public string InspectTextPageType()
    {
        Type type =
            typeof(FPDF_TEXTPAGE);

        var result =
            new System.Text.StringBuilder();

        result.AppendLine(
            $"型名: {type.FullName}");

        result.AppendLine(
            $"値型: {type.IsValueType}");

        result.AppendLine(
            $"列挙型: {type.IsEnum}");

        var fields = type.GetFields(
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic);

        foreach (var field in fields)
        {
            result.AppendLine(
                $"フィールド: {field.Name} / " +
                $"{field.FieldType.FullName}");
        }

        return result.ToString();
    }
}