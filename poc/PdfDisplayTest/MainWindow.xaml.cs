using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Collections.Generic;

namespace PdfDisplayTest;

public partial class MainWindow : Window
{
    private readonly PdfService _pdfService = new();
    private string? _currentPdfPath;
    private double _pdfPageWidth;
    private double _pdfPageHeight;
    private bool _isDrawing;
    private Point _lastPoint;
    private System.Windows.Shapes.Polyline? _currentStroke;
    private readonly Stack<System.Windows.UIElement> _undoStack = new();
    private readonly Stack<System.Windows.UIElement> _redoStack = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OpenPdfButton_Click(object sender, RoutedEventArgs e)
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

        string fileName = Path.GetFileName(dialog.FileName);
        _currentPdfPath = dialog.FileName;

        try
        {
            int pageCount = _pdfService.GetPageCount(dialog.FileName);

            var pageSize = _pdfService.GetPageSize(dialog.FileName);

            _pdfPageWidth = pageSize.Width;
            _pdfPageHeight = pageSize.Height;

            string bmpPath =
                _pdfService.RenderFirstPageToBmp(dialog.FileName);

            BitmapImage image;

            using (var stream = new FileStream(
                bmpPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            {
                image = new BitmapImage();

                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
            }

            DrawingCanvas.Children.Clear();

            PdfImage.Source = image;

            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(UpdatePdfPageDisplaySize));

            Title = $"PDF表示テスト - {fileName}";

            FilePathText.Text =
                $"PDF : {_pdfPageWidth:F2} × {_pdfPageHeight:F2}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"PDFの画像化に失敗しました。\n\n{ex.Message}",
                "変換エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void AddAnnotationButton_Click(object sender, RoutedEventArgs e)
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

        try
        {
            string outputPath =
                _pdfService.AddTestAnnotation(_currentPdfPath);

            MessageBox.Show(
                $"テスト注釈を追加して保存しました。\n\n{outputPath}",
                "保存完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"注釈の追加に失敗しました。\n\n{ex.Message}",
                "注釈エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ReadAnnotationButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPdfPath))
        {
            MessageBox.Show(
                "先に注釈付きPDFを開いてください。",
                "PDF未選択",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        try
        {
            string result =
                _pdfService.ReadAnnotations(_currentPdfPath);

            MessageBox.Show(
                result,
                "注釈読込結果",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"注釈の読込に失敗しました。\n\n{ex.Message}",
                "読込エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    //文字抽出
    private void ExtractTextButton_Click(
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

        try
        {
            string text =
                _pdfService.ExtractFirstPageText(_currentPdfPath);

            MessageBox.Show(
                text,
                "文字抽出結果",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"文字抽出に失敗しました。\n\n{ex.Message}",
                "文字抽出エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    //文字座標
    private void ExtractCharacterPositionsButton_Click(
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

        try
        {
            string result =
                _pdfService.ExtractFirstPageCharacterPositions(
                    _currentPdfPath);

            MessageBox.Show(
                result,
                "FPDF_TEXTPAGE型確認",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
                
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"文字座標の取得に失敗しました。\n\n{ex.Message}",
                "文字座標エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    //マウスクリック
    private void PdfImage_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (PdfImage.Source is not BitmapSource bitmap)
        {
            return;
        }

        if (_pdfPageWidth <= 0 || _pdfPageHeight <= 0)
        {
            MessageBox.Show("PDFページサイズが取得されていません。");
            return;
        }

        Point point = e.GetPosition(PdfImage);

        double imageControlWidth = PdfImage.ActualWidth;
        double imageControlHeight = PdfImage.ActualHeight;

        double bitmapWidth = bitmap.PixelWidth;
        double bitmapHeight = bitmap.PixelHeight;

        // Stretch="Uniform" の表示倍率
        double scale = Math.Min(
            imageControlWidth / bitmapWidth,
            imageControlHeight / bitmapHeight);

        // Imageコントロール内で実際に表示されている画像サイズ
        double displayedWidth = bitmapWidth * scale;
        double displayedHeight = bitmapHeight * scale;

        // Uniformで中央配置されたことによる余白
        double offsetX = (imageControlWidth - displayedWidth) / 2.0;
        double offsetY = (imageControlHeight - displayedHeight) / 2.0;

        // 画像の余白部分をクリックした場合
        if (point.X < offsetX ||
            point.X > offsetX + displayedWidth ||
            point.Y < offsetY ||
            point.Y > offsetY + displayedHeight)
        {
            MessageBox.Show("PDFページ外をクリックしました。");
            return;
        }

        // 実際に表示されている画像内の座標
        double imageX = point.X - offsetX;
        double imageY = point.Y - offsetY;

        // 0.0 ～ 1.0 の相対座標
        double normalizedX = imageX / displayedWidth;
        double normalizedY = imageY / displayedHeight;

        // PDF座標へ変換
        // WPFは左上原点、PDFは左下原点なのでYを反転
        double pdfX = normalizedX * _pdfPageWidth;
        double pdfY = _pdfPageHeight -
                    normalizedY * _pdfPageHeight;

        double textX;
        double textY;
        string mode;

        bool isRotated270 =
            _pdfPageWidth > _pdfPageHeight;

        if (isRotated270)
        {
            textX =
                _pdfPageHeight - pdfY;

            textY =
                pdfX;

            mode = "横向き";
        }
        else
        {
            textX =
                pdfX;

            textY =
                pdfY;

            mode = "縦向き";
        }

        string? character =
            _pdfService.GetCharacterAt(
                _currentPdfPath!,
                textX,
                textY);

        string? text =
            _pdfService.GetTextAt(
                _currentPdfPath!,
                textX,
                textY,
                isRotated270);

        FilePathText.Text =
            $"{mode}　" +
            $"文字={character ?? "なし"}　" +
            $"文字列={text ?? "なし"}";
            }

    private void DrawingCanvas_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (PdfImage.Source == null)
        {
            return;
        }

        _isDrawing = true;

        _lastPoint = e.GetPosition(DrawingCanvas);

        _currentStroke = new System.Windows.Shapes.Polyline
        {
            Stroke = Brushes.Red,
            StrokeThickness = 3,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };

        _currentStroke.Points.Add(_lastPoint);
        DrawingCanvas.Children.Add(_currentStroke);

        _undoStack.Push(_currentStroke);
        _redoStack.Clear();

        DrawingCanvas.CaptureMouse();
    }

    private void DrawingCanvas_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_isDrawing || _currentStroke == null)
        {
            return;
        }

        Point currentPoint = e.GetPosition(DrawingCanvas);

        double distanceX = currentPoint.X - _lastPoint.X;
        double distanceY = currentPoint.Y - _lastPoint.Y;

        double distance = Math.Sqrt(
            distanceX * distanceX +
            distanceY * distanceY);

        // マウス移動が小さすぎる場合は点を追加しない
        if (distance < 2)
        {
            return;
        }

        _currentStroke.Points.Add(currentPoint);
        _lastPoint = currentPoint;
    }

    private void DrawingCanvas_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_isDrawing)
        {
            return;
        }

        _isDrawing = false;
        _currentStroke = null;

        DrawingCanvas.ReleaseMouseCapture();
    }

    private void PdfViewport_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        UpdatePdfPageDisplaySize();
    }

    private void UpdatePdfPageDisplaySize()
    {
        if (PdfImage.Source is not BitmapSource bitmap)
        {
            return;
        }

        double viewportWidth = PdfViewport.ActualWidth;
        double viewportHeight = PdfViewport.ActualHeight;

        if (viewportWidth <= 1 || viewportHeight <= 1)
        {
            return;
        }

        double imageWidth = bitmap.PixelWidth;
        double imageHeight = bitmap.PixelHeight;

        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return;
        }

        double widthScale = viewportWidth / imageWidth;
        double heightScale = viewportHeight / imageHeight;

        double scale = Math.Min(widthScale, heightScale);

        double displayWidth = imageWidth * scale;
        double displayHeight = imageHeight * scale;

        PdfPageHost.Width = displayWidth;
        PdfPageHost.Height = displayHeight;
    }

    private void UndoButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        var element = _undoStack.Pop();

        DrawingCanvas.Children.Remove(element);
        _redoStack.Push(element);
    }

    private void RedoButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        var element = _redoStack.Pop();

        DrawingCanvas.Children.Add(element);
        _undoStack.Push(element);
    }

}