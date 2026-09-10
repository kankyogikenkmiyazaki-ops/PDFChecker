using System.Collections.Generic;
using System.Windows;

namespace PDFMarkup.Models;

/// <summary>
/// PDF上へ描画した1本のストロークを保持する。
/// </summary>
public sealed class StrokeModel
{
    /// <summary>
    /// PDF座標系で記録した描画点を取得する。
    /// </summary>
    public List<Point> PdfPoints { get; } = new();

    /// <summary>
    /// 線の太さを取得または設定する。
    /// </summary>
    public double Thickness { get; set; } = 1.0;

    /// <summary>
    /// 線の色を取得または設定する。
    /// </summary>
    public StrokeColor Color { get; set; } = StrokeColor.Red;

    /// <summary>
    /// 線の不透明度を0～255で取得または設定する。
    /// </summary>
    public byte Opacity { get; set; } = 255;

    /// <summary>
    /// 描画時のモードを取得または設定する。
    /// </summary>
    public DrawingMode Mode { get; set; } = DrawingMode.Markup;

    /// <summary>
    /// 描画時に選択されていた口径を取得または設定する。
    /// </summary>
    public string Diameter { get; set; } = "未設定";

    /// <summary>
    /// 描画時に入力されていたコメントを取得または設定する。
    /// </summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>
    /// 注釈選択に使用するPDF座標系の範囲を取得する。
    /// </summary>
    public Rect SelectionBounds { get; private set; } = Rect.Empty;

    /// <summary>
    /// 描画点から注釈選択範囲を再計算する。
    /// </summary>
    public void RecalculateSelectionBounds()
    {
        if (PdfPoints.Count == 0)
        {
            SelectionBounds = Rect.Empty;
            return;
        }

        double minX = PdfPoints[0].X;
        double maxX = PdfPoints[0].X;
        double minY = PdfPoints[0].Y;
        double maxY = PdfPoints[0].Y;

        foreach (Point point in PdfPoints)
        {
            minX = System.Math.Min(minX, point.X);
            maxX = System.Math.Max(maxX, point.X);
            minY = System.Math.Min(minY, point.Y);
            maxY = System.Math.Max(maxY, point.Y);
        }

        // 線そのものより少し広い範囲を持たせ、細い線も選択しやすくする。
        double padding = System.Math.Max(4.0, Thickness * 1.5);

        SelectionBounds = new Rect(
            minX - padding,
            minY - padding,
            maxX - minX + padding * 2,
            maxY - minY + padding * 2);
    }
}

/// <summary>
/// 描画モードを表す。
/// </summary>
public enum DrawingMode
{
    Markup,
    Check
}

/// <summary>
/// Ver1で使用する描画色を表す。
/// </summary>
public enum StrokeColor
{
    // 朱書き3色
    Red,
    Blue,
    Green,

    // チェック色
    Yellow,
    CheckRed,
    Orange,
    Pink,
    LightBlue,
    LightGreen,
    Purple,
    Brown,
    Gray,
    Cyan,
    Magenta,
    Turquoise,
    Lime,
    Coral,
    Indigo,
    Olive
}

/// <summary>
/// 画面表示・描画・PDF保存で共通使用する色定義。
/// </summary>
public static class StrokeColorDefinition
{
    public static (byte Red, byte Green, byte Blue) GetRgb(
        StrokeColor color)
    {
        return color switch
        {
            StrokeColor.Red => (198, 40, 40),
            StrokeColor.CheckRed => (229, 57, 53),
            StrokeColor.Blue => (21, 101, 192),
            StrokeColor.Green => (46, 125, 50),
            StrokeColor.Yellow => (255, 224, 0),
            StrokeColor.Orange => (251, 140, 0),
            StrokeColor.Purple => (142, 36, 170),
            StrokeColor.Pink => (236, 64, 122),
            StrokeColor.LightBlue => (66, 165, 245),
            StrokeColor.LightGreen => (156, 204, 101),
            StrokeColor.Brown => (121, 85, 72),
            StrokeColor.Cyan => (0, 172, 193),
            StrokeColor.Turquoise => (0, 137, 123),
            StrokeColor.Lime => (192, 202, 51),
            StrokeColor.Coral => (255, 112, 67),
            StrokeColor.Indigo => (57, 73, 171),
            StrokeColor.Olive => (130, 119, 23),

            // 既存PDFの読込と口径の自動グレー表示用。
            StrokeColor.Gray => (120, 120, 120),
            StrokeColor.Magenta => (220, 0, 180),
            _ => (198, 40, 40)
        };
    }
}
