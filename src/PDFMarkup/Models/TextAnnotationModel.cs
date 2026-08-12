using System.Windows;

namespace PDFMarkup.Models;

/// <summary>
/// PDF上へ配置した文字注釈を保持する。
/// </summary>
public sealed class TextAnnotationModel
{
    /// <summary>
    /// 文字列を取得または設定する。
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// PDF座標系での文字配置位置を取得または設定する。
    /// </summary>
    public Point PdfPosition { get; set; }

    /// <summary>
    /// 文字色を取得または設定する。
    /// </summary>
    public StrokeColor Color { get; set; } = StrokeColor.Red;

    /// <summary>
    /// 不透明度を0～255で取得または設定する。
    /// </summary>
    public byte Opacity { get; set; } = 255;

    /// <summary>
    /// 文字サイズを取得または設定する。
    /// </summary>
    public double FontSize { get; set; } = 16.0;

    /// <summary>
    /// 描画モードを取得または設定する。
    /// </summary>
    public DrawingMode Mode { get; set; } = DrawingMode.Markup;

    /// <summary>
    /// 口径を取得または設定する。
    /// </summary>
    public string Diameter { get; set; } = "未設定";

    /// <summary>
    /// コメントを取得または設定する。
    /// </summary>
    public string Comment { get; set; } = string.Empty;
}
