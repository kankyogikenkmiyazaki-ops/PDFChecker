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

    // PDFのサイズを取得
    public (double Width, double Height) GetPageSize(string filePath)
    {
        using var document = new PdfDocument(filePath, null);
        using var page = document.Pages[0];

        return (page.Width, page.Height);
        
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

    public string? GetCharacterAt(
        string filePath,
        double pdfX,
        double pdfY)
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

            for (int index = 0; index < charCount; index++)
            {
                PDFium.FPDFText_GetCharBox(
                    textPage,
                    index,
                    out double left,
                    out double right,
                    out double bottom,
                    out double top);

                const double margin = 1.5;

                bool hit =
                    pdfX >= left - margin &&
                    pdfX <= right + margin &&
                    pdfY >= bottom - margin &&
                    pdfY <= top + margin;

                if (!hit)
                {
                    continue;
                }

                // この1文字だけ取得
                return PDFium.FPDFText_GetText(
                    textPage,
                    index,
                    1);
            }

            return null;
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

    public string? GetTextAt(
        string filePath,
        double pdfX,
        double pdfY,
        bool isRotated270)
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
                return null;
            }

            const double clickMargin = 2.0;

            int hitIndex = -1;

            // クリックした文字のIndexを探す
            for (int index = 0; index < charCount; index++)
            {
                bool success =
                    PDFium.FPDFText_GetCharBox(
                        textPage,
                        index,
                        out double left,
                        out double right,
                        out double bottom,
                        out double top);

                if (!success)
                {
                    continue;
                }

                bool hit =
                    pdfX >= left - clickMargin &&
                    pdfX <= right + clickMargin &&
                    pdfY >= bottom - clickMargin &&
                    pdfY <= top + clickMargin;

                if (hit)
                {
                    hitIndex = index;
                    break;
                }
            }

            if (hitIndex < 0)
            {
                return null;
            }

            bool hitBoxSuccess =
                PDFium.FPDFText_GetCharBox(
                    textPage,
                    hitIndex,
                    out double hitLeft,
                    out double hitRight,
                    out double hitBottom,
                    out double hitTop);

            if (!hitBoxSuccess)
            {
                return null;
            }

            double hitWidth =
                Math.Max(1.0, hitRight - hitLeft);

            double hitHeight =
                Math.Max(1.0, hitTop - hitBottom);

            double hitCenterX =
                (hitLeft + hitRight) / 2.0;

            double hitCenterY =
                (hitBottom + hitTop) / 2.0;

            /*
            * 縦向き・補正なし
            *   同じ行の判定：Y座標
            *   文字の進行方向：X座標
            *
            * 横向き・270度補正
            *   同じ行の判定：X座標
            *   文字の進行方向：Y座標
            */
            double hitLinePosition =
                isRotated270
                    ? hitCenterX
                    : hitCenterY;

            double hitAdvancePosition =
                isRotated270
                    ? hitCenterY
                    : hitCenterX;

            double lineSize =
                isRotated270
                    ? hitWidth
                    : hitHeight;

            double advanceSize =
                isRotated270
                    ? hitHeight
                    : hitWidth;

            // 同じ行とみなす位置の許容値
            double lineTolerance =
                Math.Max(2.0, lineSize * 0.8);

            // 隣接文字の中心間距離の許容値
            double gapTolerance =
                Math.Max(6.0, advanceSize * 2.5);

            int startIndex = hitIndex;
            int endIndex = hitIndex;

            // 左側、または文字列の前方向へ広げる
            double previousAdvancePosition =
                hitAdvancePosition;

            for (int index = hitIndex - 1;
                index >= 0;
                index--)
            {
                string character =
                    PDFium.FPDFText_GetText(
                        textPage,
                        index,
                        1);

                if (character.Contains('\r') ||
                    character.Contains('\n'))
                {
                    break;
                }

                bool success =
                    PDFium.FPDFText_GetCharBox(
                        textPage,
                        index,
                        out double left,
                        out double right,
                        out double bottom,
                        out double top);

                if (!success)
                {
                    break;
                }

                double centerX =
                    (left + right) / 2.0;

                double centerY =
                    (bottom + top) / 2.0;

                double linePosition =
                    isRotated270
                        ? centerX
                        : centerY;

                double advancePosition =
                    isRotated270
                        ? centerY
                        : centerX;

                double lineDifference =
                    Math.Abs(
                        linePosition -
                        hitLinePosition);

                double characterDistance =
                    Math.Abs(
                        advancePosition -
                        previousAdvancePosition);

                if (lineDifference > lineTolerance ||
                    characterDistance > gapTolerance)
                {
                    break;
                }

                startIndex = index;
                previousAdvancePosition =
                    advancePosition;
            }

            // 右側、または文字列の後方向へ広げる
            previousAdvancePosition =
                hitAdvancePosition;

            for (int index = hitIndex + 1;
                index < charCount;
                index++)
            {
                string character =
                    PDFium.FPDFText_GetText(
                        textPage,
                        index,
                        1);

                if (character.Contains('\r') ||
                    character.Contains('\n'))
                {
                    break;
                }

                bool success =
                    PDFium.FPDFText_GetCharBox(
                        textPage,
                        index,
                        out double left,
                        out double right,
                        out double bottom,
                        out double top);

                if (!success)
                {
                    break;
                }

                double centerX =
                    (left + right) / 2.0;

                double centerY =
                    (bottom + top) / 2.0;

                double linePosition =
                    isRotated270
                        ? centerX
                        : centerY;

                double advancePosition =
                    isRotated270
                        ? centerY
                        : centerX;

                double lineDifference =
                    Math.Abs(
                        linePosition -
                        hitLinePosition);

                double characterDistance =
                    Math.Abs(
                        advancePosition -
                        previousAdvancePosition);

                if (lineDifference > lineTolerance ||
                    characterDistance > gapTolerance)
                {
                    break;
                }

                endIndex = index;
                previousAdvancePosition =
                    advancePosition;
            }

            int length =
                endIndex - startIndex + 1;

            string result =
                PDFium.FPDFText_GetText(
                    textPage,
                    startIndex,
                    length);

            string cleanedResult =
                result
                    .Replace("\r", "")
                    .Replace("\n", "")
                    .Trim();

            return string.IsNullOrWhiteSpace(cleanedResult)
                ? null
                : cleanedResult;
        }
        finally
        {
            PDFium.FPDFText_ClosePage(textPage);
        }
    }    
    
}