using Microsoft.Win32;
using PDFMarkup.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

using IOPath = System.IO.Path;

namespace PDFMarkup;

public partial class MainWindow : Window
{
    // サービス
    private readonly PdfService _pdfService = new();

    // 描画データ
    private readonly List<StrokeModel> _strokes = new();
    private readonly Stack<StrokeModel> _redoStrokes = new();

    private StrokeModel? _currentStrokeModel;
    private Polyline? _currentStrokeView;

    // 描画状態
    private bool _isDrawing;
    private Point _lastCanvasPoint;

    // 現在のPDF情報
    private string? _currentPdfPath;
    private int _currentPageIndex;
    private int _pageCount;

    private double _pdfPageWidth;
    private double _pdfPageHeight;

    /// メイン画面を初期化する。
    public MainWindow()
    {
        InitializeComponent();

        InputBindings.Add(
            new KeyBinding(
                ApplicationCommands.Undo,
                new KeyGesture(
                    Key.Z,
                    ModifierKeys.Control)));

        InputBindings.Add(
            new KeyBinding(
                ApplicationCommands.Redo,
                new KeyGesture(
                    Key.Y,
                    ModifierKeys.Control)));

        CommandBindings.Add(
            new CommandBinding(
                ApplicationCommands.Undo,
                UndoMenuItem_Click));

        CommandBindings.Add(
            new CommandBinding(
                ApplicationCommands.Redo,
                RedoMenuItem_Click));
    }

    /// PDF選択ダイアログを表示する。
    private void OpenPdfMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "PDFを開く",
            Filter = "PDFファイル (*.pdf)|*.pdf",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            OpenPdf(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"PDFを開けませんでした。\n\n{ex.Message}",
                "PDF読込エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// 指定されたPDFを開く。
    private void OpenPdf(string filePath)
    {
        _currentPdfPath = filePath;
        _currentPageIndex = 0;
        _pageCount = _pdfService.GetPageCount(filePath);

        if (_pageCount <= 0)
        {
            throw new InvalidOperationException(
                "PDFにページがありません。");
        }

        DrawingCanvas.Children.Clear();
        _strokes.Clear();

        _isDrawing = false;
        _currentStrokeModel = null;
        _currentStrokeView = null;

        DisplayCurrentPage();

        string fileName =
            IOPath.GetFileName(filePath);

        Title =
            $"PDF Markup - {fileName}";

        StatusText.Text =
            filePath;
    }

    /// 現在選択されているページを表示する。
    private void DisplayCurrentPage()
    {
        if (string.IsNullOrWhiteSpace(_currentPdfPath))
        {
            return;
        }

        if (_currentPageIndex < 0 ||
            _currentPageIndex >= _pageCount)
        {
            return;
        }

        var pageSize =
            _pdfService.GetPageSize(
                _currentPdfPath,
                _currentPageIndex);

        _pdfPageWidth =
            pageSize.Width;

        _pdfPageHeight =
            pageSize.Height;

        BitmapImage pageImage =
            _pdfService.RenderPage(
                _currentPdfPath,
                _currentPageIndex);

        PdfImage.Source =
            pageImage;

        PageText.Text =
            $"ページ: {_currentPageIndex + 1} / {_pageCount}";

        // 初回表示時の縦横比崩れを防ぐため、レイアウト確定後に更新する。
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(UpdatePdfPageDisplaySize));
    }

    /// PDF表示領域のサイズ変更を処理する。
    private void PdfViewport_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        UpdatePdfPageDisplaySize();
    }

    /// PDF表示サイズを更新する。
    private void UpdatePdfPageDisplaySize()
    {
        if (PdfImage.Source is not BitmapSource bitmap)
        {
            return;
        }

        double viewportWidth =
            PdfViewport.ActualWidth;

        double viewportHeight =
            PdfViewport.ActualHeight;

        if (viewportWidth <= 1 ||
            viewportHeight <= 1)
        {
            return;
        }

        double imageWidth =
            bitmap.PixelWidth;

        double imageHeight =
            bitmap.PixelHeight;

        if (imageWidth <= 0 ||
            imageHeight <= 0)
        {
            return;
        }

        const double viewportMargin = 30.0;

        double availableWidth =
            Math.Max(
                1,
                viewportWidth - viewportMargin * 2);

        double availableHeight =
            Math.Max(
                1,
                viewportHeight - viewportMargin * 2);

        double widthScale =
            availableWidth / imageWidth;

        double heightScale =
            availableHeight / imageHeight;

        // 小さい方の倍率を使い、縦横比を維持したまま全体を表示する。
        double scale =
            Math.Min(
                widthScale,
                heightScale);

        PdfPageHost.Width =
            imageWidth * scale;

        PdfPageHost.Height =
            imageHeight * scale;

        // Canvasの新しいサイズが確定してからストロークを再描画する。
        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(RedrawStrokes));
    }

    /// マウスドラッグによる描画を開始する。
    private void DrawingCanvas_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (PdfImage.Source == null)
        {
            return;
        }

        Point canvasPoint =
            e.GetPosition(DrawingCanvas);

        if (!IsCanvasPointInside(canvasPoint))
        {
            return;
        }

        _isDrawing = true;
        _lastCanvasPoint = canvasPoint;

        _currentStrokeModel =
            new StrokeModel();

        Point pdfPoint =
            ConvertCanvasPointToPdfPoint(canvasPoint);

        _currentStrokeModel.PdfPoints.Add(pdfPoint);
        _strokes.Add(_currentStrokeModel);
        _redoStrokes.Clear();

        _currentStrokeView = new Polyline
        {
            Stroke =
                GetBrush(_currentStrokeModel.Color),

            StrokeThickness =
                _currentStrokeModel.Thickness,

            StrokeLineJoin =
                PenLineJoin.Round,

            StrokeStartLineCap =
                PenLineCap.Round,

            StrokeEndLineCap =
                PenLineCap.Round
        };

        _currentStrokeView.Points.Add(canvasPoint);

        DrawingCanvas.Children.Add(
            _currentStrokeView);

        // Canvas外へマウスが移動しても描画終了を取得できるようにする。
        DrawingCanvas.CaptureMouse();
    }

    /// 描画中のストロークへ点を追加する。
    private void DrawingCanvas_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_isDrawing ||
            _currentStrokeModel == null ||
            _currentStrokeView == null)
        {
            return;
        }

        Point canvasPoint =
            e.GetPosition(DrawingCanvas);

        canvasPoint =
            ClampCanvasPoint(canvasPoint);

        double distanceX =
            canvasPoint.X - _lastCanvasPoint.X;

        double distanceY =
            canvasPoint.Y - _lastCanvasPoint.Y;

        double distance =
            Math.Sqrt(
                distanceX * distanceX +
                distanceY * distanceY);

        // 点が増えすぎないよう、小さなマウス移動は記録しない。
        if (distance < 2.0)
        {
            return;
        }

        Point pdfPoint =
            ConvertCanvasPointToPdfPoint(canvasPoint);

        _currentStrokeModel.PdfPoints.Add(pdfPoint);
        _currentStrokeView.Points.Add(canvasPoint);

        _lastCanvasPoint = canvasPoint;
    }

    /// マウスドラッグによる描画を終了する。
    private void DrawingCanvas_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_isDrawing)
        {
            return;
        }

        _isDrawing = false;

        DrawingCanvas.ReleaseMouseCapture();

        _currentStrokeModel = null;
        _currentStrokeView = null;
    }

    /// Canvas座標をPDF座標へ変換する。
    private Point ConvertCanvasPointToPdfPoint(
        Point canvasPoint)
    {
        double canvasWidth =
            DrawingCanvas.ActualWidth;

        double canvasHeight =
            DrawingCanvas.ActualHeight;

        if (canvasWidth <= 0 ||
            canvasHeight <= 0 ||
            _pdfPageWidth <= 0 ||
            _pdfPageHeight <= 0)
        {
            return new Point();
        }

        double normalizedX =
            canvasPoint.X / canvasWidth;

        double normalizedY =
            canvasPoint.Y / canvasHeight;

        double pdfX =
            normalizedX * _pdfPageWidth;

        // WPFは左上原点、PDFは左下原点のためY座標を反転する。
        double pdfY =
            _pdfPageHeight -
            normalizedY * _pdfPageHeight;

        return new Point(
            pdfX,
            pdfY);
    }

    /// 指定された座標がCanvas内か確認する。
    private bool IsCanvasPointInside(
        Point point)
    {
        return point.X >= 0 &&
               point.X <= DrawingCanvas.ActualWidth &&
               point.Y >= 0 &&
               point.Y <= DrawingCanvas.ActualHeight;
    }

    /// Canvas外の座標をCanvas範囲内へ補正する。
    private Point ClampCanvasPoint(
        Point point)
    {
        double x =
            Math.Clamp(
                point.X,
                0,
                DrawingCanvas.ActualWidth);

        double y =
            Math.Clamp(
                point.Y,
                0,
                DrawingCanvas.ActualHeight);

        return new Point(x, y);
    }

    /// ストローク色をWPFのBrushへ変換する。
    private static Brush GetBrush(
        StrokeColor color)
    {
        return color switch
        {
            StrokeColor.Blue =>
                Brushes.Blue,

            StrokeColor.Green =>
                Brushes.Green,

            _ =>
                Brushes.Red
        };
    }

    /// 保存されているストロークを再描画する。
    private void RedrawStrokes()
    {
        DrawingCanvas.Children.Clear();

        foreach (StrokeModel stroke in _strokes)
        {
            var polyline = new Polyline
            {
                Stroke =
                    GetBrush(stroke.Color),

                StrokeThickness =
                    stroke.Thickness,

                StrokeLineJoin =
                    PenLineJoin.Round,

                StrokeStartLineCap =
                    PenLineCap.Round,

                StrokeEndLineCap =
                    PenLineCap.Round
            };

            foreach (Point pdfPoint in stroke.PdfPoints)
            {
                Point canvasPoint =
                    ConvertPdfPointToCanvasPoint(pdfPoint);

                polyline.Points.Add(canvasPoint);
            }

            DrawingCanvas.Children.Add(polyline);
        }
    }

    /// PDF座標をCanvas座標へ変換する。
    private Point ConvertPdfPointToCanvasPoint(
        Point pdfPoint)
    {
        double canvasWidth =
            DrawingCanvas.ActualWidth;

        double canvasHeight =
            DrawingCanvas.ActualHeight;

        if (canvasWidth <= 0 ||
            canvasHeight <= 0 ||
            _pdfPageWidth <= 0 ||
            _pdfPageHeight <= 0)
        {
            return new Point();
        }

        double normalizedX =
            pdfPoint.X / _pdfPageWidth;

        // PDFの左下原点から、WPFの左上原点へ戻す。
        double normalizedY =
            1.0 -
            pdfPoint.Y / _pdfPageHeight;

        return new Point(
            normalizedX * canvasWidth,
            normalizedY * canvasHeight);
    }

    /// 元に戻す。
    private void UndoMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        UndoStroke();
    }

    /// やり直す。
    private void RedoMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        RedoStroke();
    }

    /// 最後のストロークを元に戻す。
    private void UndoStroke()
    {
        if (_strokes.Count == 0)
        {
            return;
        }

        StrokeModel stroke =
            _strokes[^1];

        _strokes.RemoveAt(
            _strokes.Count - 1);

        _redoStrokes.Push(stroke);

        RedrawStrokes();
    }

    /// 元に戻したストロークをやり直す。
    private void RedoStroke()
    {
        if (_redoStrokes.Count == 0)
        {
            return;
        }

        StrokeModel stroke =
            _redoStrokes.Pop();

        _strokes.Add(stroke);

        RedrawStrokes();
    }

    /// 描画内容を名前を付けて保存する。
    private void SaveAsMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPdfPath))
        {
            MessageBox.Show(
                "先にPDFを開いてください。",
                "PDF未選択",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        if (_strokes.Count == 0)
        {
            MessageBox.Show(
                "保存する描画がありません。",
                "描画なし",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        string sourceFileName =
            IOPath.GetFileNameWithoutExtension(
                _currentPdfPath);

        var dialog = new SaveFileDialog
        {
            Title = "名前を付けて保存",
            Filter = "PDFファイル (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            AddExtension = true,
            FileName =
                $"{sourceFileName}_markup.pdf"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            bool isRotated270 =
                _pdfPageWidth > _pdfPageHeight;

            var saveStrokes =
                _strokes
                    .Select(stroke =>
                        ConvertStrokeForPdfSave(
                            stroke,
                            isRotated270))
                    .ToList();

            _pdfService.SaveInkAnnotations(
                _currentPdfPath,
                dialog.FileName,
                _currentPageIndex,
                saveStrokes);

            MessageBox.Show(
                $"PDFを保存しました。\n\n{dialog.FileName}",
                "保存完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"PDFの保存に失敗しました。\n\n{ex.Message}",
                "保存エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// 保存用にストローク座標を変換する。
    private StrokeModel ConvertStrokeForPdfSave(
        StrokeModel source,
        bool isRotated270)
    {
        var converted =
            new StrokeModel
            {
                Color = source.Color,
                Thickness = source.Thickness
            };

        foreach (Point point in source.PdfPoints)
        {
            Point savePoint;

            if (isRotated270)
            {
                // PoCで確認した横向きPDFの270度補正。
                savePoint =
                    new Point(
                        _pdfPageHeight - point.Y,
                        point.X);
            }
            else
            {
                savePoint =
                    point;
            }

            converted.PdfPoints.Add(
                savePoint);
        }

        return converted;
    }

}
