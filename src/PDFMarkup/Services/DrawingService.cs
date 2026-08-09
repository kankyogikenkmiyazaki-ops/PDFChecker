using System;
using System.Windows;

namespace PDFMarkup.Services;

/// <summary>
/// 描画処理を管理するサービス。
/// </summary>
public sealed class DrawingService
{
    // 8方向スナップの吸着許容角度。
    private const double SnapAngleToleranceDegrees = 7.5;

    // A3長辺のPDFポイント値（420mm）。
    // A4はA3と同じ1.0倍とし、それより大きいページだけ自動拡大する。
    private const double A3LongSidePdfPoints = 1190.55;

    // A3基準の矢印サイズ（PDFポイント）。
    // PDF上の実寸として保持するため、描画時のズーム倍率には左右されない。
    private const double BaseArrowLengthPdfPoints = 16.0;
    private const double BaseArrowHalfWidthPdfPoints = 7.0;


    /// 指定された点が8方向に近い場合、最寄りの45度方向へ吸着させる。
    public Point SnapToEightDirections(
        Point startPoint,
        Point currentPoint)
    {
        double deltaX =
            currentPoint.X - startPoint.X;

        double deltaY =
            currentPoint.Y - startPoint.Y;

        double distance =
            Math.Sqrt(
                deltaX * deltaX +
                deltaY * deltaY);

        if (distance < 0.0001)
        {
            return currentPoint;
        }

        double angleDegrees =
            Math.Atan2(
                deltaY,
                deltaX) *
            180.0 /
            Math.PI;

        if (angleDegrees < 0)
        {
            angleDegrees += 360.0;
        }

        double snappedAngleDegrees =
            Math.Round(
                angleDegrees / 45.0) *
            45.0;

        if (snappedAngleDegrees >= 360.0)
        {
            snappedAngleDegrees = 0.0;
        }

        double angleDifference =
            Math.Abs(
                NormalizeAngleDifference(
                    angleDegrees -
                    snappedAngleDegrees));

        if (angleDifference >
            SnapAngleToleranceDegrees)
        {
            return currentPoint;
        }

        double snappedAngleRadians =
            snappedAngleDegrees *
            Math.PI /
            180.0;

        return new Point(
            startPoint.X +
            Math.Cos(snappedAngleRadians) *
            distance,

            startPoint.Y +
            Math.Sin(snappedAngleRadians) *
            distance);
    }

    /// Canvas座標をPDF座標へ変換する。
    public Point ConvertCanvasPointToPdfPoint(
        Point canvasPoint,
        double canvasWidth,
        double canvasHeight,
        double pdfPageWidth,
        double pdfPageHeight)
    {
        if (canvasWidth <= 0 ||
            canvasHeight <= 0 ||
            pdfPageWidth <= 0 ||
            pdfPageHeight <= 0)
        {
            return new Point();
        }

        double normalizedX =
            canvasPoint.X / canvasWidth;

        double normalizedY =
            canvasPoint.Y / canvasHeight;

        double pdfX =
            normalizedX * pdfPageWidth;

        // WPFは左上原点、PDFは左下原点のためY座標を反転する。
        double pdfY =
            pdfPageHeight -
            normalizedY * pdfPageHeight;

        return new Point(
            pdfX,
            pdfY);
    }

    /// PDF座標をCanvas座標へ変換する。
    public Point ConvertPdfPointToCanvasPoint(
        Point pdfPoint,
        double canvasWidth,
        double canvasHeight,
        double pdfPageWidth,
        double pdfPageHeight)
    {
        if (canvasWidth <= 0 ||
            canvasHeight <= 0 ||
            pdfPageWidth <= 0 ||
            pdfPageHeight <= 0)
        {
            return new Point();
        }

        double normalizedX =
            pdfPoint.X / pdfPageWidth;

        // PDFの左下原点から、WPFの左上原点へ戻す。
        double normalizedY =
            1.0 -
            pdfPoint.Y / pdfPageHeight;

        return new Point(
            normalizedX * canvasWidth,
            normalizedY * canvasHeight);
    }

    /// 指定された座標がCanvas内か確認する。
    public bool IsCanvasPointInside(
        Point point,
        double canvasWidth,
        double canvasHeight)
    {
        return point.X >= 0 &&
               point.X <= canvasWidth &&
               point.Y >= 0 &&
               point.Y <= canvasHeight;
    }

    /// Canvas外の座標をCanvas範囲内へ補正する。
    public Point ClampCanvasPoint(
        Point point,
        double canvasWidth,
        double canvasHeight)
    {
        double x =
            Math.Clamp(
                point.X,
                0,
                canvasWidth);

        double y =
            Math.Clamp(
                point.Y,
                0,
                canvasHeight);

        return new Point(x, y);
    }

    /// 現在ページの用紙サイズから、A3基準の矢印自動倍率を取得する。
    /// A4以下は1.0倍、A2は約1.4倍、A1は約2.0倍、A0は約2.8倍となる。
    public double GetArrowPaperScale(
        double pdfPageWidth,
        double pdfPageHeight)
    {
        if (pdfPageWidth <= 0 ||
            pdfPageHeight <= 0)
        {
            return 1.0;
        }

        double longSide =
            Math.Max(
                pdfPageWidth,
                pdfPageHeight);

        return Math.Max(
            1.0,
            longSide / A3LongSidePdfPoints);
    }

    /// PDFポイントで定義した矢印サイズを、現在のCanvas表示サイズへ変換する。
    /// ズーム倍率にかかわらず、PDF保存後の矢印実寸が一定になるよう計算する。
    public (double Length, double HalfWidth) GetArrowSizeInCanvas(
        double pdfPageWidth,
        double pdfPageHeight,
        double canvasWidth,
        double canvasHeight,
        double arrowUserScale)
    {
        double canvasScaleX =
            pdfPageWidth > 0
                ? canvasWidth / pdfPageWidth
                : 1.0;

        double canvasScaleY =
            pdfPageHeight > 0
                ? canvasHeight / pdfPageHeight
                : canvasScaleX;

        // PDFとCanvasは同じ縦横比で表示しているため、平均倍率を使用する。
        double canvasScale =
            Math.Max(
                0.0001,
                (canvasScaleX + canvasScaleY) / 2.0);

        double paperScale =
            GetArrowPaperScale(
                pdfPageWidth,
                pdfPageHeight);

        double totalScale =
            paperScale *
            arrowUserScale;

        double arrowLengthPdf =
            BaseArrowLengthPdfPoints *
            totalScale;

        double arrowHalfWidthPdf =
            BaseArrowHalfWidthPdfPoints *
            totalScale;

        return (
            arrowLengthPdf * canvasScale,
            arrowHalfWidthPdf * canvasScale);
    }

    /// 現在ページの実寸基準サイズで、始点側の矢羽根端点を計算する。
    public (Point Point1, Point Point2) GetStartArrowHeadPoints(
        Point startPoint,
        Point endPoint,
        double pdfPageWidth,
        double pdfPageHeight,
        double canvasWidth,
        double canvasHeight,
        double arrowUserScale)
    {
        double deltaX =
            endPoint.X - startPoint.X;

        double deltaY =
            endPoint.Y - startPoint.Y;

        double distance =
            Math.Sqrt(
                deltaX * deltaX +
                deltaY * deltaY);

        if (distance < 0.0001)
        {
            return (
                startPoint,
                startPoint);
        }

        double unitX =
            deltaX / distance;

        double unitY =
            deltaY / distance;

        double perpendicularX =
            -unitY;

        double perpendicularY =
            unitX;

        (double arrowLength, double arrowHalfWidth) =
            GetArrowSizeInCanvas(
                pdfPageWidth,
                pdfPageHeight,
                canvasWidth,
                canvasHeight,
                arrowUserScale);

        Point basePoint =
            new Point(
                startPoint.X +
                unitX * arrowLength,
                startPoint.Y +
                unitY * arrowLength);

        Point point1 =
            new Point(
                basePoint.X +
                perpendicularX * arrowHalfWidth,
                basePoint.Y +
                perpendicularY * arrowHalfWidth);

        Point point2 =
            new Point(
                basePoint.X -
                perpendicularX * arrowHalfWidth,
                basePoint.Y -
                perpendicularY * arrowHalfWidth);

        return (
            point1,
            point2);
    }

    /// 角度差を-180度～180度の範囲へ正規化する。
    private static double NormalizeAngleDifference(
        double angleDegrees)
    {
        while (angleDegrees > 180.0)
        {
            angleDegrees -= 360.0;
        }

        while (angleDegrees < -180.0)
        {
            angleDegrees += 360.0;
        }

        return angleDegrees;
    }


    
}