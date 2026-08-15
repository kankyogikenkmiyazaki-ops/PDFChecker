using PDFiumSharp;
using PDFiumSharp.Types;
using PDFMarkup.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

using PdfSharpDocument = PdfSharp.Pdf.PdfDocument;

namespace PDFMarkup.Services;

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

    /// 現在ページのPDFiumサイズ・MediaBox・CropBox・回転情報を取得する。
    /// 座標ずれ調査用として、PDFiumとPDFsharpの両方からページ情報を読む。
    public PageInfo GetPageInfo(
        string filePath,
        int pageIndex)
    {
        double pdfiumWidth;
        double pdfiumHeight;

        // 実際の画面描画に使用しているPDFium側のページサイズを取得する。
        using (var pdfiumDocument =
            new PdfDocument(filePath, null))
        {
            ValidatePageIndex(
                pdfiumDocument,
                pageIndex);

            using var pdfiumPage =
                pdfiumDocument.Pages[pageIndex];

            pdfiumWidth =
                pdfiumPage.Width;

            pdfiumHeight =
                pdfiumPage.Height;
        }

        // PDF辞書に保存されているBoxと/RotateはPDFsharp側から取得する。
        using PdfSharpDocument pdfSharpDocument =
            PdfReader.Open(
                filePath,
                PdfDocumentOpenMode.Import);

        if (pageIndex < 0 ||
            pageIndex >= pdfSharpDocument.PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                $"ページ番号が範囲外です。ページ数: {pdfSharpDocument.PageCount}");
        }

        PdfSharp.Pdf.PdfPage pdfSharpPage =
            pdfSharpDocument.Pages[pageIndex];

        PdfSharp.Pdf.PdfRectangle mediaBox =
            pdfSharpPage.MediaBoxReadOnly;

        // CropBoxが未指定の場合は、PDFsharpのEffectiveCropBoxで
        // MediaBox等から継承・補完された実際の表示領域を取得する。
        PdfSharp.Pdf.PdfRectangle cropBox =
            pdfSharpPage.EffectiveCropBoxReadOnly;

        return new PageInfo
        {
            PageIndex = pageIndex,
            PdfiumWidth = pdfiumWidth,
            PdfiumHeight = pdfiumHeight,
            Rotation = pdfSharpPage.Rotate,

            MediaBoxLeft = mediaBox.X1,
            MediaBoxBottom = mediaBox.Y1,
            MediaBoxRight = mediaBox.X2,
            MediaBoxTop = mediaBox.Y2,

            HasCropBox = pdfSharpPage.HasCropBox,
            CropBoxLeft = cropBox.X1,
            CropBoxBottom = cropBox.Y1,
            CropBoxRight = cropBox.X2,
            CropBoxTop = cropBox.Y2
        };
    }

    /// 指定されたPDFページを一覧表示用のサムネイル画像として描画する。
    public BitmapImage RenderThumbnail(
        string filePath,
        int pageIndex,
        int maximumPixelWidth)
    {
        using var document =
            new PdfDocument(filePath, null);

        ValidatePageIndex(
            document,
            pageIndex);

        using var page =
            document.Pages[pageIndex];

        int width =
            Math.Max(
                1,
                maximumPixelWidth);

        double aspectRatio =
            page.Height / page.Width;

        int height =
            Math.Max(
                1,
                (int)Math.Round(
                    width * aspectRatio));

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

        page.Render(bitmap);

        using var stream =
            bitmap.AsBmpStream(
                96,
                96);

        var image =
            new BitmapImage();

        image.BeginInit();
        image.CacheOption =
            BitmapCacheOption.OnLoad;
        image.StreamSource =
            stream;
        image.EndInit();
        image.Freeze();

        return image;
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

    /// ページ別の線注釈と文字注釈をPDFへ一括保存する。
    /// 線はInk、文字はFreeTextの標準注釈として保存する。
    public void SavePdfMarkupAnnotations(
        string sourcePath,
        string outputPath,
        IReadOnlyDictionary<int, IReadOnlyList<StrokeModel>> pageStrokes,
        IReadOnlyDictionary<int, IReadOnlyList<TextAnnotationModel>> pageTextAnnotations)
    {
        string normalizedSourcePath =
            Path.GetFullPath(sourcePath);

        string normalizedOutputPath =
            Path.GetFullPath(outputPath);

        bool isOverwrite =
            string.Equals(
                normalizedSourcePath,
                normalizedOutputPath,
                StringComparison.OrdinalIgnoreCase);

        string actualOutputPath =
            isOverwrite
                ? CreateTemporarySavePath(normalizedSourcePath)
                : normalizedOutputPath;

        try
        {
            using (PdfSharpDocument document =
                PdfReader.Open(
                    normalizedSourcePath,
                    PdfDocumentOpenMode.Modify))
            {

        // 元PDFにすでに保存されているPDFMarkup注釈を一度取り除く。
        // これを行わないと、非表示にした注釈や削除した注釈が元PDF側に残る。
        RemoveExistingPdfMarkupAnnotations(document);

        IEnumerable<int> pageIndexes =
            pageStrokes.Keys
                .Concat(
                    pageTextAnnotations.Keys)
                .Distinct()
                .OrderBy(
                    pageIndex => pageIndex);

        foreach (int pageIndex in pageIndexes)
        {
            if (pageIndex < 0 ||
                pageIndex >= document.PageCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pageStrokes),
                    $"ページ番号が範囲外です。ページ番号: {pageIndex + 1} / ページ数: {document.PageCount}");
            }

            PdfSharp.Pdf.PdfPage page =
                document.Pages[pageIndex];

            if (pageStrokes.TryGetValue(
                    pageIndex,
                    out IReadOnlyList<StrokeModel>? strokes))
            {
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
            }

            if (pageTextAnnotations.TryGetValue(
                    pageIndex,
                    out IReadOnlyList<TextAnnotationModel>? textAnnotations))
            {
                foreach (TextAnnotationModel annotation in textAnnotations)
                {
                    if (string.IsNullOrWhiteSpace(
                            annotation.Text))
                    {
                        continue;
                    }

                    AddFreeTextAnnotation(
                        document,
                        page,
                        annotation);
                }
            }
        }

                // 全ページへの追加が完了してから、一度だけファイルへ保存する。
                document.Save(actualOutputPath);
            }

            if (isOverwrite)
            {
                // 元PDFを直接書き換えず、一時ファイルの保存成功後に置換する。
                // 保存途中で失敗しても元PDFを残せるようにする。
                File.Replace(
                    actualOutputPath,
                    normalizedSourcePath,
                    null);
            }
        }
        finally
        {
            if (isOverwrite &&
                File.Exists(actualOutputPath))
            {
                File.Delete(actualOutputPath);
            }
        }
    }

    /// 上書き保存用に、元PDFと同じフォルダーへ一時ファイル名を作成する。
    private static string CreateTemporarySavePath(
        string sourcePath)
    {
        string directory =
            Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException(
                "PDFの保存先フォルダーを取得できません。");

        string fileName =
            Path.GetFileNameWithoutExtension(sourcePath);

        return Path.Combine(
            directory,
            $".{fileName}.{Guid.NewGuid():N}.tmp.pdf");
    }


    /// PDF内に既に存在するPDFMarkup作成注釈を全ページから取り除く。
    /// Acrobatなど、ほかのアプリで作成された注釈は残す。
    private static void RemoveExistingPdfMarkupAnnotations(
        PdfSharpDocument document)
    {
        foreach (PdfSharp.Pdf.PdfPage page in document.Pages)
        {
            PdfSharp.Pdf.PdfArray? annotations =
                page.Elements.GetArray(
                    "/Annots");

            if (annotations == null)
            {
                continue;
            }

            // 削除で後続インデックスがずれないよう、末尾から確認する。
            for (int index = annotations.Elements.Count - 1;
                index >= 0;
                index--)
            {
                PdfSharp.Pdf.PdfDictionary? annotation =
                    ResolveAnnotationDictionary(
                        annotations.Elements[index]);

                if (annotation == null ||
                    !IsPdfMarkupAnnotation(annotation))
                {
                    continue;
                }

                annotations.Elements.RemoveAt(index);
            }
        }
    }

    /// 注釈配列の要素から実体の注釈Dictionaryを取得する。
    private static PdfSharp.Pdf.PdfDictionary? ResolveAnnotationDictionary(
        PdfSharp.Pdf.PdfItem item)
    {
        return item switch
        {
            PdfSharp.Pdf.Advanced.PdfReference reference =>
                reference.Value as PdfSharp.Pdf.PdfDictionary,

            PdfSharp.Pdf.PdfDictionary dictionary =>
                dictionary,

            _ =>
                null
        };
    }

    /// PDFMarkupが作成した注釈か判定する。
    private static bool IsPdfMarkupAnnotation(
        PdfSharp.Pdf.PdfDictionary annotation)
    {
        string author =
            annotation.Elements.GetString(
                "/T");

        if (string.Equals(
                author,
                "PDFMarkup",
                StringComparison.Ordinal))
        {
            return true;
        }

        // 初期開発版との互換用。独自キーまたは旧Contents形式でも判定する。
        string metadata =
            annotation.Elements.GetString(
                "/PDFMarkupData");

        if (!string.IsNullOrWhiteSpace(metadata))
        {
            return true;
        }

        string contents =
            annotation.Elements.GetString(
                "/Contents");

        return contents.StartsWith(
            "PDFMarkup|",
            StringComparison.Ordinal);
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

        // Acrobatのコメント一覧でも内容を確認できるよう、
        // モード・口径・コメントを表示用文字列としてContentsへ保存する。
        annotation.Elements.SetString(
            "/Contents",
            CreateAnnotationContents(stroke));

        // PDFMarkup固有情報は独自キーへJSONで保存し、コメント本文と分離する。
        annotation.Elements.SetString(
            "/PDFMarkupData",
            CreateAnnotationMetadataJson(stroke));

        // PDF注釈の不透明度は0.0～1.0で保存する。
        annotation.Elements.SetReal(
            "/CA",
            stroke.Opacity / 255.0);

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

    /// 1件の文字注釈からFreeText注釈を作成する。
    private static void AddFreeTextAnnotation(
        PdfSharpDocument document,
        PdfSharp.Pdf.PdfPage page,
        TextAnnotationModel textAnnotation)
    {
        double fontSize =
            Math.Max(
                1.0,
                textAnnotation.FontSize);

        double estimatedWidth =
            Math.Max(
                fontSize,
                textAnnotation.Text.Length *
                fontSize *
                0.65);

        double estimatedHeight =
            Math.Max(
                fontSize,
                fontSize * 1.4);

        // TextAnnotationModel.PdfPositionは画面上の文字左上位置をPDF座標で保持する。
        // PDFのRectは左下・右上なので、文字高さ分だけ下へ広げる。
        double rectLeft =
            textAnnotation.PdfPosition.X;

        double rectTop =
            textAnnotation.PdfPosition.Y;

        double rectBottom =
            rectTop - estimatedHeight;

        var annotation =
            new PdfSharp.Pdf.PdfDictionary(
                document);

        annotation.Elements.SetName(
            "/Type",
            "/Annot");

        annotation.Elements.SetName(
            "/Subtype",
            "/FreeText");

        annotation.Elements["/Rect"] =
            new PdfSharp.Pdf.PdfRectangle(
                new XRect(
                    rectLeft,
                    rectBottom,
                    estimatedWidth,
                    estimatedHeight));

        annotation.Elements.SetString(
            "/T",
            "PDFMarkup");

        // FreeTextではContentsが実際に表示される文字列になる。
        annotation.Elements.SetString(
            "/Contents",
            textAnnotation.Text);

        annotation.Elements.SetString(
            "/PDFMarkupData",
            CreateTextAnnotationMetadataJson(
                textAnnotation));

        annotation.Elements.SetReal(
            "/CA",
            textAnnotation.Opacity / 255.0);

        annotation.Elements["/C"] =
            CreateColorArray(
                document,
                textAnnotation.Color);

        (byte red, byte green, byte blue) =
            GetRgb(
                textAnnotation.Color);

        string defaultAppearance =
            string.Format(
                CultureInfo.InvariantCulture,
                "/Helv {0:0.###} Tf {1:0.######} {2:0.######} {3:0.######} rg",
                fontSize,
                red / 255.0,
                green / 255.0,
                blue / 255.0);

        annotation.Elements.SetString(
            "/DA",
            defaultAppearance);

        // 左寄せ。
        annotation.Elements.SetInteger(
            "/Q",
            0);

        // 印刷時にも注釈を表示する。
        annotation.Elements.SetInteger(
            "/F",
            4);

        var borderStyle =
            new PdfSharp.Pdf.PdfDictionary(
                document);

        borderStyle.Elements.SetName(
            "/Type",
            "/Border");

        borderStyle.Elements.SetReal(
            "/W",
            0);

        annotation.Elements["/BS"] =
            borderStyle;

        document.Internals.AddObject(
            annotation);

        PdfSharp.Pdf.PdfArray? annotations =
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

        (byte red, byte green, byte blue) =
            GetRgb(color);

        colorArray.Elements.Add(
            new PdfSharp.Pdf.PdfReal(
                red / 255.0));

        colorArray.Elements.Add(
            new PdfSharp.Pdf.PdfReal(
                green / 255.0));

        colorArray.Elements.Add(
            new PdfSharp.Pdf.PdfReal(
                blue / 255.0));

        return colorArray;
    }

    /// StrokeColorに対応するRGB値を取得する。
    private static (byte Red, byte Green, byte Blue) GetRgb(
        StrokeColor color)
    {
        return color switch
        {
            StrokeColor.Blue => (0, 80, 220),
            StrokeColor.Green => (0, 150, 70),
            StrokeColor.Yellow => (255, 230, 0),
            StrokeColor.Orange => (255, 145, 0),
            StrokeColor.Pink => (255, 105, 180),
            StrokeColor.LightBlue => (80, 190, 255),
            StrokeColor.LightGreen => (100, 220, 120),
            StrokeColor.Purple => (150, 80, 210),
            StrokeColor.Brown => (150, 90, 40),
            StrokeColor.Gray => (120, 120, 120),
            StrokeColor.Cyan => (0, 210, 210),
            StrokeColor.Magenta => (220, 0, 180),
            _ => (220, 0, 0)
        };
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

    /// 現在ページのInk注釈の生データを調査用文字列として取得する。
    /// 先頭から最大5件まで、Rect・InkList・AP有無・先頭末尾座標を表示する。
    public string GetInkAnnotationDebugInfo(
        string filePath,
        int pageIndex,
        int maximumAnnotations = 5)
    {
        using PdfSharpDocument document =
            PdfReader.Open(
                filePath,
                PdfDocumentOpenMode.Import);

        if (pageIndex < 0 ||
            pageIndex >= document.PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                $"ページ番号が範囲外です。ページ数: {document.PageCount}");
        }

        var page =
            document.Pages[pageIndex];

        var annotations =
            page.Elements.GetArray(
                "/Annots");

        if (annotations == null)
        {
            return
                $"Page: {pageIndex + 1}\n" +
                "Annotations: 0\n" +
                "Ink annotations: 0";
        }

        int totalAnnotationCount =
            annotations.Elements.Count;

        int inkAnnotationCount = 0;
        int outputCount = 0;

        var lines =
            new List<string>
            {
                $"Page: {pageIndex + 1}",
                $"Annotations: {totalAnnotationCount}",
                string.Empty
            };

        for (int annotationIndex = 0;
            annotationIndex < annotations.Elements.Count;
            annotationIndex++)
        {
            var item =
                annotations.Elements[annotationIndex];

            PdfSharp.Pdf.PdfDictionary? annotation =
                item switch
                {
                    PdfSharp.Pdf.Advanced.PdfReference reference =>
                        reference.Value as PdfSharp.Pdf.PdfDictionary,

                    PdfSharp.Pdf.PdfDictionary dictionary =>
                        dictionary,

                    _ =>
                        null
                };

            if (annotation == null)
            {
                continue;
            }

            string subtype =
                annotation.Elements.GetName(
                    "/Subtype");

            if (subtype != "/Ink")
            {
                continue;
            }

            inkAnnotationCount++;

            if (outputCount >=
                Math.Max(
                    1,
                    maximumAnnotations))
            {
                continue;
            }

            outputCount++;

            string rectText =
                annotation.Elements["/Rect"]?.ToString()
                ?? "(none)";

            bool hasAppearance =
                annotation.Elements["/AP"] != null;

            string contents =
                annotation.Elements.GetString(
                    "/Contents");

            var inkList =
                annotation.Elements.GetArray(
                    "/InkList");

            int strokeCount =
                inkList?.Elements.Count ?? 0;

            int pointCount = 0;
            string firstPoint = "(none)";
            string lastPoint = "(none)";

            if (inkList != null)
            {
                bool firstPointFound = false;

                foreach (var strokeItem in inkList.Elements)
                {
                    if (strokeItem is not PdfSharp.Pdf.PdfArray pointArray)
                    {
                        continue;
                    }

                    int currentPointCount =
                        pointArray.Elements.Count / 2;

                    pointCount +=
                        currentPointCount;

                    if (!firstPointFound &&
                        pointArray.Elements.Count >= 2)
                    {
                        double firstX =
                            pointArray.Elements.GetReal(0);

                        double firstY =
                            pointArray.Elements.GetReal(1);

                        firstPoint =
                            $"({firstX:0.###}, {firstY:0.###})";

                        firstPointFound = true;
                    }

                    if (pointArray.Elements.Count >= 2)
                    {
                        int lastXIndex =
                            pointArray.Elements.Count - 2;

                        int lastYIndex =
                            pointArray.Elements.Count - 1;

                        double lastX =
                            pointArray.Elements.GetReal(
                                lastXIndex);

                        double lastY =
                            pointArray.Elements.GetReal(
                                lastYIndex);

                        lastPoint =
                            $"({lastX:0.###}, {lastY:0.###})";
                    }
                }
            }

            lines.Add(
                $"[Ink #{inkAnnotationCount} / Annot index {annotationIndex}]");

            lines.Add(
                $"Rect: {rectText}");

            lines.Add(
                $"InkList strokes: {strokeCount}");

            lines.Add(
                $"Points: {pointCount}");

            lines.Add(
                $"First: {firstPoint}");

            lines.Add(
                $"Last : {lastPoint}");

            lines.Add(
                $"AP: {(hasAppearance ? "Yes" : "No")}");

            if (!string.IsNullOrWhiteSpace(contents))
            {
                string compactContents =
                    contents
                        .Replace(
                            "\r",
                            " ")
                        .Replace(
                            "\n",
                            " ");

                if (compactContents.Length > 80)
                {
                    compactContents =
                        compactContents[..80] +
                        "...";
                }

                lines.Add(
                    $"Contents: {compactContents}");
            }

            lines.Add(
                string.Empty);
        }

        lines.Insert(
            2,
            $"Ink annotations: {inkAnnotationCount}");

        if (inkAnnotationCount >
            outputCount)
        {
            lines.Add(
                $"※先頭 {outputCount} 件のみ表示");
        }

        return string.Join(
            Environment.NewLine,
            lines);
    }

    /// PDFからInk注釈を読み込む。
    public List<StrokeModel> LoadInkAnnotations(
        string filePath,
        int pageIndex)
    {
        using PdfSharpDocument document =
            PdfReader.Open(
                filePath,
                PdfDocumentOpenMode.Import);

        if (pageIndex < 0 ||
            pageIndex >= document.PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                $"ページ番号が範囲外です。ページ数: {document.PageCount}");
        }

        var page =
            document.Pages[pageIndex];

        var strokes =
            new List<StrokeModel>();

        var annotations =
            page.Elements.GetArray(
                "/Annots");

        if (annotations == null)
        {
            return strokes;
        }

        foreach (var item in annotations.Elements)
        {
            PdfSharp.Pdf.PdfDictionary? annotation =
                item switch
                {
                    PdfSharp.Pdf.Advanced.PdfReference reference =>
                        reference.Value as PdfSharp.Pdf.PdfDictionary,

                    PdfSharp.Pdf.PdfDictionary dictionary =>
                        dictionary,

                    _ =>
                        null
                };

            if (annotation == null)
            {
                continue;
            }

            string subtype =
                annotation.Elements.GetName(
                    "/Subtype");

            if (subtype != "/Ink")
            {
                continue;
            }

            var inkList =
                annotation.Elements.GetArray(
                    "/InkList");

            if (inkList == null)
            {
                continue;
            }

            StrokeColor color =
                ReadStrokeColor(annotation);

            double thickness =
                ReadStrokeThickness(annotation);

            DrawingMode mode =
                ReadDrawingMode(annotation);

            byte opacity =
                ReadStrokeOpacity(annotation);

            string diameter =
                ReadDiameter(annotation);

            string comment =
                ReadAnnotationComment(annotation);

            foreach (var strokeItem in inkList.Elements)
            {
                if (strokeItem is not PdfSharp.Pdf.PdfArray pointArray)
                {
                    continue;
                }

                var stroke =
                    new StrokeModel
                    {
                        Mode = mode,
                        Color = color,
                        Thickness = thickness,
                        Opacity = opacity,
                        Diameter = diameter,
                        Comment = comment
                    };

                for (int index = 0;
                    index + 1 < pointArray.Elements.Count;
                    index += 2)
                {
                    double x =
                        pointArray.Elements.GetReal(index);

                    double y =
                        pointArray.Elements.GetReal(index + 1);

                    stroke.PdfPoints.Add(
                        new Point(x, y));
                }

                if (stroke.PdfPoints.Count >= 2)
                {
                    strokes.Add(stroke);
                }
            }
        }

        return strokes;
    }

    /// Ink注釈の色を読み込む。
    private static StrokeColor ReadStrokeColor(
        PdfSharp.Pdf.PdfDictionary annotation)
    {
        AnnotationMetadata? metadata =
            ReadAnnotationMetadata(annotation);

        if (Enum.TryParse(
            metadata?.Color,
            true,
            out StrokeColor jsonColor))
        {
            return jsonColor;
        }

        // 旧形式のContentsメタデータも読み込めるようにする。
        string contents =
            annotation.Elements.GetString(
                "/Contents");

        StrokeColor? metadataColor =
            ReadEnumMetadata<StrokeColor>(
                contents,
                "Color");

        if (metadataColor.HasValue)
        {
            return metadataColor.Value;
        }

        var colorArray =
            annotation.Elements.GetArray(
                "/C");

        if (colorArray == null ||
            colorArray.Elements.Count < 3)
        {
            return StrokeColor.Red;
        }

        byte red =
            ToColorByte(
                colorArray.Elements.GetReal(0));

        byte green =
            ToColorByte(
                colorArray.Elements.GetReal(1));

        byte blue =
            ToColorByte(
                colorArray.Elements.GetReal(2));

        return Enum.GetValues<StrokeColor>()
            .OrderBy(color =>
            {
                (byte knownRed, byte knownGreen, byte knownBlue) =
                    GetRgb(color);

                int redDifference = knownRed - red;
                int greenDifference = knownGreen - green;
                int blueDifference = knownBlue - blue;

                return redDifference * redDifference +
                       greenDifference * greenDifference +
                       blueDifference * blueDifference;
            })
            .First();
    }

    /// Ink注釈の描画モードを読み込む。
    private static DrawingMode ReadDrawingMode(
        PdfSharp.Pdf.PdfDictionary annotation)
    {
        AnnotationMetadata? metadata =
            ReadAnnotationMetadata(annotation);

        if (Enum.TryParse(
            metadata?.Mode,
            true,
            out DrawingMode jsonMode))
        {
            return jsonMode;
        }

        // 旧形式のContentsメタデータも読み込めるようにする。
        string contents =
            annotation.Elements.GetString(
                "/Contents");

        DrawingMode? mode =
            ReadEnumMetadata<DrawingMode>(
                contents,
                "Mode");

        return mode ?? DrawingMode.Markup;
    }

    /// Ink注釈の不透明度を読み込む。
    private static byte ReadStrokeOpacity(
        PdfSharp.Pdf.PdfDictionary annotation)
    {
        double opacity =
            annotation.Elements.GetReal(
                "/CA");

        if (opacity <= 0)
        {
            return 255;
        }

        return (byte)Math.Round(
            Math.Clamp(opacity, 0.0, 1.0) * 255.0);
    }

    /// ストローク情報をPDF保存用JSONへ変換する。
    private static string CreateAnnotationMetadataJson(
        StrokeModel stroke)
    {
        var metadata =
            new AnnotationMetadata
            {
                Mode = stroke.Mode.ToString(),
                Color = stroke.Color.ToString(),
                Opacity = stroke.Opacity,
                Diameter = string.IsNullOrWhiteSpace(stroke.Diameter)
                    ? "未設定"
                    : stroke.Diameter
            };

        return JsonSerializer.Serialize(metadata);
    }

    /// PDFMarkup固有のJSON情報を読み込む。
    private static AnnotationMetadata? ReadAnnotationMetadata(
        PdfSharp.Pdf.PdfDictionary annotation)
    {
        string json =
            annotation.Elements.GetString(
                "/PDFMarkupData");

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AnnotationMetadata>(json);
        }
        catch (JsonException)
        {
            // 壊れた独自情報があっても、PDF注釈自体の読込は継続する。
            return null;
        }
    }

    /// Ink注釈へ保存された口径を読み込む。
    private static string ReadDiameter(
        PdfSharp.Pdf.PdfDictionary annotation)
    {
        string? diameter =
            ReadAnnotationMetadata(annotation)?.Diameter;

        return string.IsNullOrWhiteSpace(diameter)
            ? "未設定"
            : diameter;
    }

    /// Acrobatのコメント一覧へ表示する注釈本文を作成する。
    private static string CreateAnnotationContents(
        StrokeModel stroke)
    {
        string modeText =
            stroke.Mode == DrawingMode.Check
                ? "チェック"
                : "朱書き";

        string diameter =
            string.IsNullOrWhiteSpace(stroke.Diameter)
                ? "未設定"
                : stroke.Diameter.Trim();

        string comment =
            stroke.Comment?.Trim() ?? string.Empty;

        return string.IsNullOrWhiteSpace(comment)
            ? $"{modeText}｜口径:{diameter}"
            : $"{modeText}｜口径:{diameter}｜コメント:{comment}";
    }

    /// Ink注釈の通常コメントを読み込む。
    private static string ReadAnnotationComment(
        PdfSharp.Pdf.PdfDictionary annotation)
    {
        string contents =
            annotation.Elements.GetString(
                "/Contents");

        // 旧版の内部メタデータはユーザーコメントとして扱わない。
        if (contents.StartsWith(
            "PDFMarkup|",
            StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        const string commentMarker = "｜コメント:";

        int commentIndex =
            contents.IndexOf(
                commentMarker,
                StringComparison.Ordinal);

        if (commentIndex >= 0)
        {
            return contents[
                (commentIndex + commentMarker.Length)..];
        }

        // 新形式でコメントが空の場合は、モード・口径だけが入っている。
        if (contents.StartsWith(
                "朱書き｜口径:",
                StringComparison.Ordinal) ||
            contents.StartsWith(
                "チェック｜口径:",
                StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // 旧版ではContentsにコメント本文だけを保存していた。
        return contents;
    }

    /// 文字注釈のPDFMarkup固有情報をJSONへ変換する。
    private static string CreateTextAnnotationMetadataJson(
        TextAnnotationModel annotation)
    {
        var metadata =
            new TextAnnotationMetadata
            {
                Kind = "Text",
                Mode = annotation.Mode.ToString(),
                Color = annotation.Color.ToString(),
                Opacity = annotation.Opacity,
                Diameter = string.IsNullOrWhiteSpace(
                    annotation.Diameter)
                        ? "未設定"
                        : annotation.Diameter,
                Comment = annotation.Comment ?? string.Empty,
                FontSize = annotation.FontSize
            };

        return JsonSerializer.Serialize(
            metadata);
    }

    /// FreeText注釈から文字注釈を読み込む。
    public List<TextAnnotationModel> LoadTextAnnotations(
        string filePath,
        int pageIndex)
    {
        using PdfSharpDocument document =
            PdfReader.Open(
                filePath,
                PdfDocumentOpenMode.Import);

        if (pageIndex < 0 ||
            pageIndex >= document.PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                $"ページ番号が範囲外です。ページ数: {document.PageCount}");
        }

        PdfSharp.Pdf.PdfPage page =
            document.Pages[pageIndex];

        var result =
            new List<TextAnnotationModel>();

        PdfSharp.Pdf.PdfArray? annotations =
            page.Elements.GetArray(
                "/Annots");

        if (annotations == null)
        {
            return result;
        }

        foreach (PdfSharp.Pdf.PdfItem item in annotations.Elements)
        {
            PdfSharp.Pdf.PdfDictionary? annotation =
                ResolveAnnotationDictionary(
                    item);

            if (annotation == null ||
                annotation.Elements.GetName(
                    "/Subtype") != "/FreeText")
            {
                continue;
            }

            string text =
                annotation.Elements.GetString(
                    "/Contents");

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            PdfSharp.Pdf.PdfRectangle rect =
                annotation.Elements.GetRectangle(
                    "/Rect",
                    false);

            if (rect.IsZero)
            {
                continue;
            }

            TextAnnotationMetadata? metadata =
                ReadTextAnnotationMetadata(
                    annotation);

            StrokeColor color =
                ReadStrokeColor(
                    annotation);

            byte opacity =
                ReadStrokeOpacity(
                    annotation);

            DrawingMode mode =
                DrawingMode.Markup;

            if (Enum.TryParse(
                    metadata?.Mode,
                    true,
                    out DrawingMode metadataMode))
            {
                mode =
                    metadataMode;
            }

            double fontSize =
                metadata?.FontSize > 0
                    ? metadata.FontSize
                    : ReadFreeTextFontSize(
                        annotation);

            var textAnnotation =
                new TextAnnotationModel
                {
                    Text = text,
                    PdfPosition =
                        new Point(
                            rect.X1,
                            rect.Y2),
                    Color = color,
                    Opacity = opacity,
                    FontSize = fontSize,
                    Mode = mode,
                    Diameter =
                        string.IsNullOrWhiteSpace(
                            metadata?.Diameter)
                                ? "未設定"
                                : metadata!.Diameter,
                    Comment =
                        metadata?.Comment ?? string.Empty
                };

            result.Add(
                textAnnotation);
        }

        return result;
    }

    /// 文字注釈の独自JSON情報を読み込む。
    private static TextAnnotationMetadata? ReadTextAnnotationMetadata(
        PdfSharp.Pdf.PdfDictionary annotation)
    {
        string json =
            annotation.Elements.GetString(
                "/PDFMarkupData");

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            TextAnnotationMetadata? metadata =
                JsonSerializer.Deserialize<TextAnnotationMetadata>(
                    json);

            return string.Equals(
                    metadata?.Kind,
                    "Text",
                    StringComparison.OrdinalIgnoreCase)
                ? metadata
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// FreeTextのDefault Appearanceから文字サイズを読み込む。
    private static double ReadFreeTextFontSize(
        PdfSharp.Pdf.PdfDictionary annotation)
    {
        string defaultAppearance =
            annotation.Elements.GetString(
                "/DA");

        if (string.IsNullOrWhiteSpace(
                defaultAppearance))
        {
            return 16.0;
        }

        string[] parts =
            defaultAppearance.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

        for (int index = 1;
            index < parts.Length;
            index++)
        {
            if (!string.Equals(
                    parts[index],
                    "Tf",
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (double.TryParse(
                    parts[index - 1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double fontSize) &&
                fontSize > 0)
            {
                return fontSize;
            }
        }

        return 16.0;
    }

    /// FreeText注釈用のPDFMarkup固有JSON構造。
    private sealed class TextAnnotationMetadata
    {
        public string Kind { get; set; } = "Text";

        public string Mode { get; set; } = DrawingMode.Markup.ToString();

        public string Color { get; set; } = StrokeColor.Red.ToString();

        public byte Opacity { get; set; } = 255;

        public string Diameter { get; set; } = "未設定";

        public string Comment { get; set; } = string.Empty;

        public double FontSize { get; set; } = 16.0;
    }

    /// PDFMarkup固有情報のJSON構造。
    private sealed class AnnotationMetadata
    {
        public string Mode { get; set; } = DrawingMode.Markup.ToString();

        public string Color { get; set; } = StrokeColor.Red.ToString();

        public byte Opacity { get; set; } = 255;

        public string Diameter { get; set; } = "未設定";
    }

    /// Contentsへ保存した旧形式の列挙値を読み込む。
    private static TEnum? ReadEnumMetadata<TEnum>(
        string contents,
        string key)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(contents))
        {
            return null;
        }

        string prefix =
            $"{key}=";

        string? value =
            contents
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(part =>
                    part.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))?
                .Substring(prefix.Length);

        return Enum.TryParse(
            value,
            true,
            out TEnum result)
                ? result
                : null;
    }

    /// PDFの0.0～1.0の色値をbyteへ変換する。
    private static byte ToColorByte(
        double value)
    {
        return (byte)Math.Round(
            Math.Clamp(value, 0.0, 1.0) * 255.0);
    }

    /// Ink注釈の線幅を読み込む。
    private static double ReadStrokeThickness(
        PdfSharp.Pdf.PdfDictionary annotation)
    {
        var borderStyle =
            annotation.Elements.GetDictionary(
                "/BS");

        if (borderStyle == null)
        {
            return 3.0;
        }

        double thickness =
            borderStyle.Elements.GetReal(
                "/W");

        return thickness > 0
            ? thickness
            : 3.0;
    }

}
