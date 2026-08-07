using PDFiumSharp;
using PDFiumSharp.Types;
using PDFMarkup.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

    /// ページ別のストロークをInk注釈としてPDFへ一括保存する。
    public void SaveInkAnnotations(
        string sourcePath,
        string outputPath,
        IReadOnlyDictionary<int, IReadOnlyList<StrokeModel>> pageStrokes)
    {
        if (pageStrokes.Count == 0 ||
            !pageStrokes.Values.Any(strokes => strokes.Count > 0))
        {
            throw new InvalidOperationException(
                "保存するストロークがありません。");
        }

        using PdfSharpDocument document =
            PdfReader.Open(
                sourcePath,
                PdfDocumentOpenMode.Modify);

        // 元PDFにすでに保存されているPDFMarkup注釈を一度取り除く。
        // これを行わないと、非表示にした注釈が元PDF側に残ったままになる。
        RemoveExistingPdfMarkupAnnotations(document);

        foreach ((int pageIndex, IReadOnlyList<StrokeModel> strokes)
            in pageStrokes.OrderBy(entry => entry.Key))
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

        // 全ページへの追加が完了してから、一度だけファイルへ保存する。
        document.Save(outputPath);
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