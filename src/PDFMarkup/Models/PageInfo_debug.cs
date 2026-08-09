namespace PDFMarkup.Models;

/// <summary>
/// PDFページの座標・回転調査に使用するページ情報。
/// </summary>
public sealed class PageInfo
{
    /// <summary>
    /// ページ番号（0始まり）を取得または設定する。
    /// </summary>
    public int PageIndex { get; set; }

    /// <summary>
    /// PDFiumが返すページ幅を取得または設定する。
    /// </summary>
    public double PdfiumWidth { get; set; }

    /// <summary>
    /// PDFiumが返すページ高さを取得または設定する。
    /// </summary>
    public double PdfiumHeight { get; set; }

    /// <summary>
    /// PDFページの /Rotate 値を取得または設定する。
    /// </summary>
    public int Rotation { get; set; }

    /// <summary>
    /// MediaBox左端を取得または設定する。
    /// </summary>
    public double MediaBoxLeft { get; set; }

    /// <summary>
    /// MediaBox下端を取得または設定する。
    /// </summary>
    public double MediaBoxBottom { get; set; }

    /// <summary>
    /// MediaBox右端を取得または設定する。
    /// </summary>
    public double MediaBoxRight { get; set; }

    /// <summary>
    /// MediaBox上端を取得または設定する。
    /// </summary>
    public double MediaBoxTop { get; set; }

    /// <summary>
    /// PDFにCropBoxが明示されているか取得または設定する。
    /// </summary>
    public bool HasCropBox { get; set; }

    /// <summary>
    /// 実際に使用されるCropBox左端を取得または設定する。
    /// </summary>
    public double CropBoxLeft { get; set; }

    /// <summary>
    /// 実際に使用されるCropBox下端を取得または設定する。
    /// </summary>
    public double CropBoxBottom { get; set; }

    /// <summary>
    /// 実際に使用されるCropBox右端を取得または設定する。
    /// </summary>
    public double CropBoxRight { get; set; }

    /// <summary>
    /// 実際に使用されるCropBox上端を取得または設定する。
    /// </summary>
    public double CropBoxTop { get; set; }

    /// <summary>
    /// MediaBox幅を取得する。
    /// </summary>
    public double MediaBoxWidth =>
        MediaBoxRight - MediaBoxLeft;

    /// <summary>
    /// MediaBox高さを取得する。
    /// </summary>
    public double MediaBoxHeight =>
        MediaBoxTop - MediaBoxBottom;

    /// <summary>
    /// CropBox幅を取得する。
    /// </summary>
    public double CropBoxWidth =>
        CropBoxRight - CropBoxLeft;

    /// <summary>
    /// CropBox高さを取得する。
    /// </summary>
    public double CropBoxHeight =>
        CropBoxTop - CropBoxBottom;
}
