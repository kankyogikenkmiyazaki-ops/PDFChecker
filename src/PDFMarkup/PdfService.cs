using PDFiumSharp;
using PDFiumSharp.Types;
using PDFMarkup.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

using PdfSharpDocument = PdfSharp.Pdf.PdfDocument;

namespace PDFMarkup;

public sealed class PdfService
{
    /// PDFの総ページ数を取得する。
    public int GetPageCount(string filePath)
    {
        using var document =
            new PdfDocument(filePath, null);

        return document.Pages.Count;
    }

    /// PDFページの内部サイズを取得する。
    public (double Width, double Height) GetPageSize(
        string filePath,
        int pageIndex)
    {
        using var document =
            new PdfDocument(filePath, null);

        ValidatePageIndex(
            document,
            pageIndex);

        using var page =
            document.Pages[pageIndex];

        return (
            page.Width,
            page.Height);
    }

    /// 指定されたPDFページを画像として描画する。
    public BitmapImage RenderPage(
        string filePath,
        int pageIndex)
    {
        using var document =
            new PdfDocument(filePath, null);

        ValidatePageIndex(
            document,
            pageIndex);

        using var page =
            document.Pages[pageIndex];

        const double scale = 2.0;

        int width =
            Math.Max(
                1,
                (int)Math.Round(
                    page.Width * scale));

        int height =
            Math.Max(
                1,
                (int)Math.Round(
                    page.Height * scale));

        using var bitmap =
            new PDFiumBitmap(
                width,
                height,
                false);

        bitmap.Fill(
            new FPDF_COLOR(
                255,
                255,
                255,
                255));

        // PoCで正常動作を確認した既定設定で描画する。
        page.Render(bitmap);

        using var stream =
            bitmap.AsBmpStream(
                144,
                144);

        var image =
            new BitmapImage();

        image.BeginInit();

        // Stream破棄後も画像を使用できるよう、読込時に展開する。
        image.CacheOption =
            BitmapCacheOption.OnLoad;

        image.StreamSource =
            stream;

        image.EndInit();
        image.Freeze();

        return image;
    }

    /// ストロークをInk注釈としてPDFへ保存する。
    public void SaveInkAnnotations(
        string sourcePath,
        string outputPath,
        int pageIndex,
        IReadOnlyList<StrokeModel> strokes)
    {
        if (strokes.Count == 0)
        {
            throw new InvalidOperationException(
                "保存するストロークがありません。");
        }

        using PdfSharpDocument document =
            PdfReader.Open(
                sourcePath,
                PdfDocumentOpenMode.Modify);

        if (pageIndex < 0 ||
            pageIndex >= document.PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                $"ページ番号が範囲外です。ページ数: {document.PageCount}");
        }

        var page =
            document.Pages[pageIndex];

        foreach (StrokeModel stroke in strokes)
        {
            if (stroke.PdfPoints.Count < 2)
            {
                continue;
            }

            AddInkAnnotation(
                document,
                page,
                stroke);
        }

        document.Save(outputPath);
    }

    /// 1本のストロークからInk注釈を作成する。
    private static void AddInkAnnotation(
        PdfSharpDocument document,
        PdfSharp.Pdf.PdfPage page,
        StrokeModel stroke)
    {
        double minX =
            stroke.PdfPoints.Min(
                point => point.X);

        double maxX =
            stroke.PdfPoints.Max(
                point => point.X);

        double minY =
            stroke.PdfPoints.Min(
                point => point.Y);

        double maxY =
            stroke.PdfPoints.Max(
                point => point.Y);

        double padding =
            Math.Max(
                2.0,
                stroke.Thickness);

        var annotation =
            new PdfSharp.Pdf.PdfDictionary(
                document);

        annotation.Elements.SetName(
            "/Type",
            "/Annot");

        annotation.Elements.SetName(
            "/Subtype",
            "/Ink");

        double rectX =
            minX - padding;

        double rectY =
            minY - padding;

        double rectWidth =
            maxX - minX + padding * 2;

        double rectHeight =
            maxY - minY + padding * 2;

        annotation.Elements["/Rect"] =
            new PdfSharp.Pdf.PdfRectangle(
                new XRect(
                    rectX,
                    rectY,
                    rectWidth,
                    rectHeight));

        annotation.Elements.SetString(
            "/T",
            "PDFMarkup");

        annotation.Elements.SetString(
            "/Contents",
            "PDFMarkup Ink");

        annotation.Elements["/C"] =
            CreateColorArray(
                document,
                stroke.Color);

        var borderStyle =
            new PdfSharp.Pdf.PdfDictionary(
                document);

        borderStyle.Elements.SetName(
            "/Type",
            "/Border");

        borderStyle.Elements.SetReal(
            "/W",
            stroke.Thickness);

        annotation.Elements["/BS"] =
            borderStyle;

        var strokeArray =
            new PdfSharp.Pdf.PdfArray(
                document);

        foreach (Point point in stroke.PdfPoints)
        {
            strokeArray.Elements.Add(
                new PdfSharp.Pdf.PdfReal(
                    point.X));

            strokeArray.Elements.Add(
                new PdfSharp.Pdf.PdfReal(
                    point.Y));
        }

        var inkList =
            new PdfSharp.Pdf.PdfArray(
                document);

        inkList.Elements.Add(
            strokeArray);

        annotation.Elements["/InkList"] =
            inkList;

        document.Internals.AddObject(
            annotation);

        var annotations =
            page.Elements.GetArray(
                "/Annots");

        if (annotations == null)
        {
            annotations =
                new PdfSharp.Pdf.PdfArray(
                    document);

            page.Elements["/Annots"] =
                annotations;
        }

        annotations.Elements.Add(
            annotation.Reference!);
    }

    /// ストローク色をPDF用のRGB配列へ変換する。
    private static PdfSharp.Pdf.PdfArray CreateColorArray(
        PdfSharpDocument document,
        StrokeColor color)
    {
        var colorArray =
            new PdfSharp.Pdf.PdfArray(
                document);

        (double red, double green, double blue) =
            color switch
            {
                StrokeColor.Blue =>
                    (0.0, 0.0, 1.0),

                StrokeColor.Green =>
                    (0.0, 1.0, 0.0),

                _ =>
                    (1.0, 0.0, 0.0)
            };

        colorArray.Elements.Add(
            new PdfSharp.Pdf.PdfReal(
                red));

        colorArray.Elements.Add(
            new PdfSharp.Pdf.PdfReal(
                green));

        colorArray.Elements.Add(
            new PdfSharp.Pdf.PdfReal(
                blue));

        return colorArray;
    }

    /// ページ番号が有効範囲内か確認する。
    private static void ValidatePageIndex(
        PdfDocument document,
        int pageIndex)
    {
        if (pageIndex < 0 ||
            pageIndex >= document.Pages.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                $"ページ番号が範囲外です。ページ数: {document.Pages.Count}");
        }
    }
}