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

    // チェック10色
    Yellow,
    Orange,
    Pink,
    LightBlue,
    LightGreen,
    Purple,
    Brown,
    Gray,
    Cyan,
    Magenta
}
