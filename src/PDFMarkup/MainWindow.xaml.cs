using Microsoft.Win32;
using PDFMarkup.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

using IOPath = System.IO.Path;

namespace PDFMarkup;

public partial class MainWindow : Window
{
    // ページ移動コマンド
    private static readonly RoutedCommand PreviousPageCommand = new();
    private static readonly RoutedCommand NextPageCommand = new();    
    
    // サービス
    private readonly PdfService _pdfService = new();

    // 現在ページの描画データ
    private readonly List<StrokeModel> _strokes = new();
    private readonly Stack<StrokeModel> _redoStrokes = new();

    // ページごとの未保存描画とRedo状態を保持する。
    private readonly Dictionary<int, List<StrokeModel>> _pageStrokes = new();
    private readonly Dictionary<int, Stack<StrokeModel>> _pageRedoStrokes = new();
    private readonly HashSet<int> _loadedAnnotationPages = new();

    private StrokeModel? _currentStrokeModel;
    private Polyline? _currentStrokeView;

    // 現在選択されている注釈。
    private StrokeModel? _selectedStroke;

    // 注釈選択時など、口径ComboBoxをコードから更新している間は
    // 自動色変更を行わない。
    private bool _isUpdatingDiameterSelection;

    // 注釈モードごとの表示状態。
    private bool _isMarkupVisible = true;
    private bool _isCheckVisible = true;

    // 現在の描画設定
    private DrawingMode _currentDrawingMode = DrawingMode.Markup;
    private StrokeColor _currentStrokeColor = StrokeColor.Red;
    private double _currentStrokeThickness = 1.0;
    private byte _currentStrokeOpacity = 255;

    // モードごとの最後の設定
    private StrokeColor _lastMarkupColor = StrokeColor.Red;
    private double _lastMarkupThickness = 1.0;
    private byte _lastMarkupOpacity = 255;

    private StrokeColor _lastCheckColor = StrokeColor.Yellow;
    private double _lastCheckThickness = 8.0;
    private byte _lastCheckOpacity = 96;

    // 描画状態
    private bool _isDrawing;
    private Point _lastCanvasPoint;

    // 現在のPDF情報
    private string? _currentPdfPath;
    private int _currentPageIndex;
    private int _pageCount;

    private double _pdfPageWidth;
    private double _pdfPageHeight;

    // 左右パネルの開閉状態と復元用の幅
    private bool _isLeftPanelOpen = true;
    private bool _isRightPanelOpen = true;

    private GridLength _leftPanelOpenWidth =
        new GridLength(220);

    private GridLength _rightPanelOpenWidth =
        new GridLength(260);

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

        InputBindings.Add(
            new KeyBinding(
                PreviousPageCommand,
                new KeyGesture(
                    Key.Left,
                    ModifierKeys.Control)));

        InputBindings.Add(
            new KeyBinding(
                NextPageCommand,
                new KeyGesture(
                    Key.Right,
                    ModifierKeys.Control)));

        CommandBindings.Add(
            new CommandBinding(
                PreviousPageCommand,
                PreviousPageCommand_Executed));

        CommandBindings.Add(
            new CommandBinding(
                NextPageCommand,
                NextPageCommand_Executed));



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
        _redoStrokes.Clear();
        _pageStrokes.Clear();
        _pageRedoStrokes.Clear();
        _loadedAnnotationPages.Clear();

        _isDrawing = false;
        _currentStrokeModel = null;
        _currentStrokeView = null;
        _selectedStroke = null;

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

        // 初回だけPDF注釈を読み込み、再表示時はページ別の作業内容を復元する。
        RestoreCurrentPageStrokes();

        PageText.Text =
            $"ページ: {_currentPageIndex + 1} / {_pageCount}";

        PageNavigationText.Text =
            $"{_currentPageIndex + 1} / {_pageCount}";

        PreviousPageButton.IsEnabled =
            _currentPageIndex > 0;

        NextPageButton.IsEnabled =
            _currentPageIndex < _pageCount - 1;

        // 初回表示時の縦横比崩れを防ぐため、レイアウト確定後に更新する。
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(UpdatePdfPageDisplaySize));
    }

    /// 指定されたページへ移動する。
    private void ChangePage(
        int pageIndex)
    {
        if (string.IsNullOrWhiteSpace(_currentPdfPath))
        {
            return;
        }

        if (pageIndex < 0 ||
            pageIndex >= _pageCount)
        {
            return;
        }

        // 移動前ページの未保存描画をメモリへ退避する。
        SaveCurrentPageState();
        _selectedStroke = null;

        _currentPageIndex =
            pageIndex;

        DisplayCurrentPage();
    }

    /// 前のページを表示する。
    private void PreviousPageButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ChangePage(
            _currentPageIndex - 1);
    }

    /// 次のページを表示する。
    private void NextPageButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ChangePage(
            _currentPageIndex + 1);
    }

    /// ショートカットキーで前のページを表示する。
    private void PreviousPageCommand_Executed(
        object sender,
        ExecutedRoutedEventArgs e)
    {
        ChangePage(
            _currentPageIndex - 1);
    }

    /// ショートカットキーで次のページを表示する。
    private void NextPageCommand_Executed(
        object sender,
        ExecutedRoutedEventArgs e)
    {
        ChangePage(
            _currentPageIndex + 1);
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

        // 非表示中のモードで新しく描き始めた場合は、描画結果が見えるよう自動表示する。
        EnsureCurrentDrawingModeVisible();

        // 既存注釈をクリックした場合は描画を開始せず、その注釈を選択する。
        StrokeModel? hitStroke =
            FindStrokeAtCanvasPoint(canvasPoint);

        if (hitStroke != null)
        {
            SelectStroke(hitStroke);
            e.Handled = true;
            return;
        }

        _selectedStroke = null;
        _isDrawing = true;
        _lastCanvasPoint = canvasPoint;

        _currentStrokeModel =
            CreateStrokeFromCurrentSettings();

        Point pdfPoint =
            ConvertCanvasPointToPdfPoint(canvasPoint);

        _currentStrokeModel.PdfPoints.Add(pdfPoint);
        _strokes.Add(_currentStrokeModel);
        _redoStrokes.Clear();

        _currentStrokeView = new Polyline
        {
            Stroke =
                CreateStrokeBrush(
                    _currentStrokeModel.Color,
                    _currentStrokeModel.Opacity),

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

        if (_currentStrokeModel != null)
        {
            _currentStrokeModel.RecalculateSelectionBounds();
            _selectedStroke = _currentStrokeModel;
            ApplySelectedStrokeToRightPanel(_currentStrokeModel);
        }

        _currentStrokeModel = null;
        _currentStrokeView = null;

        RedrawStrokes();
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

    /// 指定位置にある注釈を後から描いた順に検索する。
    private StrokeModel? FindStrokeAtCanvasPoint(
        Point canvasPoint)
    {
        Point pdfPoint =
            ConvertCanvasPointToPdfPoint(canvasPoint);

        for (int index = _strokes.Count - 1;
            index >= 0;
            index--)
        {
            StrokeModel stroke = _strokes[index];

            // 非表示中の注釈はクリック対象にしない。
            if (!IsStrokeVisible(stroke))
            {
                continue;
            }

            if (stroke.SelectionBounds.IsEmpty)
            {
                stroke.RecalculateSelectionBounds();
            }

            if (stroke.SelectionBounds.Contains(pdfPoint))
            {
                return stroke;
            }
        }

        return null;
    }

    /// 注釈を選択し、選択枠と右パネルへ情報を反映する。
    private void SelectStroke(
        StrokeModel stroke)
    {
        _selectedStroke = stroke;
        ApplySelectedStrokeToRightPanel(stroke);
        RedrawStrokes();
    }

    /// 選択した注釈情報を描画モードと右パネルへ反映する。
    private void ApplySelectedStrokeToRightPanel(
        StrokeModel stroke)
    {
        // 先に上部のモードボタンを切り替え、
        // ツールバーや右パネルの背景色、色パレットを同期する。
        if (stroke.Mode == DrawingMode.Markup)
        {
            if (MarkupModeButton != null)
            {
                MarkupModeButton.IsChecked = true;
            }
        }
        else
        {
            if (CheckModeButton != null)
            {
                CheckModeButton.IsChecked = true;
            }
        }

        // Checkedイベントでは各モードの前回設定が一度復元されるため、
        // 最後に選択した注釈自身の設定で上書きする。
        _currentDrawingMode = stroke.Mode;
        _currentStrokeColor = stroke.Color;
        _currentStrokeThickness = stroke.Thickness;
        _currentStrokeOpacity = stroke.Opacity;

        SaveCurrentModeSettings();

        if (CurrentDrawingModeText != null)
        {
            CurrentDrawingModeText.Text =
                stroke.Mode == DrawingMode.Markup
                    ? "朱書き"
                    : "チェック";
        }

        SelectDiameterInRightPanel(stroke.Diameter);

        if (AnnotationCommentTextBox != null)
        {
            AnnotationCommentTextBox.Text = stroke.Comment;
        }

        UpdateDrawingSettingsUi();
    }

    /// 指定された口径を右パネルのComboBoxへ表示する。
    private void SelectDiameterInRightPanel(
        string diameter)
    {
        if (DiameterComboBox == null)
        {
            return;
        }

        _isUpdatingDiameterSelection = true;

        try
        {
            foreach (object item in DiameterComboBox.Items)
            {
                if (item is ComboBoxItem comboBoxItem &&
                    string.Equals(
                        comboBoxItem.Content?.ToString(),
                        diameter,
                        StringComparison.Ordinal))
                {
                    DiameterComboBox.SelectedItem = comboBoxItem;
                    return;
                }
            }
        }
        finally
        {
            _isUpdatingDiameterSelection = false;
        }
    }

    /// 口径変更時に、選択口径以外のチェック注釈をグレー表示へ更新する。
    private void DiameterComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isUpdatingDiameterSelection)
        {
            return;
        }

        // 元のストローク色は変更せず、画面上の表示色だけを更新する。
        RedrawStrokes();
    }

    /// 口径によるチェック注釈の強調表示を切り替える。
    private void AutoColorChangeCheckBox_Click(
        object sender,
        RoutedEventArgs e)
    {
        // チェック状態に応じて、表示中ページを即時に再描画する。
        RedrawStrokes();
    }

    /// 口径に対応するチェック色を取得する。
    private static bool TryGetColorForDiameter(
        string diameter,
        out StrokeColor color)
    {
        switch (diameter)
        {
            case "φ50":
                color = StrokeColor.Blue;
                return true;

            case "φ75":
                color = StrokeColor.Yellow;
                return true;

            case "φ100":
                color = StrokeColor.Brown;
                return true;

            case "φ150":
                color = StrokeColor.Green;
                return true;

            case "φ200":
                color = StrokeColor.Orange;
                return true;

            case "φ300":
                color = StrokeColor.Red;
                return true;

            case "φ400":
                color = StrokeColor.Purple;
                return true;

            default:
                color = default;
                return false;
        }
    }

    /// 選択中注釈の範囲をAcrobat風の青枠で描画する。
    private void DrawSelectionAdorner(
        StrokeModel stroke)
    {
        if (stroke.SelectionBounds.IsEmpty)
        {
            stroke.RecalculateSelectionBounds();
        }

        if (stroke.SelectionBounds.IsEmpty)
        {
            return;
        }

        Point topLeft =
            ConvertPdfPointToCanvasPoint(
                new Point(
                    stroke.SelectionBounds.Left,
                    stroke.SelectionBounds.Top));

        Point bottomRight =
            ConvertPdfPointToCanvasPoint(
                new Point(
                    stroke.SelectionBounds.Right,
                    stroke.SelectionBounds.Bottom));

        double left = Math.Min(topLeft.X, bottomRight.X);
        double top = Math.Min(topLeft.Y, bottomRight.Y);
        double width = Math.Abs(bottomRight.X - topLeft.X);
        double height = Math.Abs(bottomRight.Y - topLeft.Y);

        var border = new Rectangle
        {
            Width = width,
            Height = height,
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = 1.5,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, top);
        DrawingCanvas.Children.Add(border);

        const double handleSize = 9.0;

        AddSelectionHandle(left, top, handleSize);
        AddSelectionHandle(left + width, top, handleSize);
        AddSelectionHandle(left, top + height, handleSize);
        AddSelectionHandle(left + width, top + height, handleSize);
    }

    /// 選択枠の角へ丸いハンドルを追加する。
    private void AddSelectionHandle(
        double centerX,
        double centerY,
        double size)
    {
        var handle = new Ellipse
        {
            Width = size,
            Height = size,
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = 1.5,
            Fill = Brushes.White,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(handle, centerX - size / 2.0);
        Canvas.SetTop(handle, centerY - size / 2.0);
        DrawingCanvas.Children.Add(handle);
    }

    /// 現在の描画設定をコピーしたストロークを作成する。
    private StrokeModel CreateStrokeFromCurrentSettings()
    {
        return new StrokeModel
        {
            Mode = _currentDrawingMode,
            Color = _currentStrokeColor,
            Thickness = _currentStrokeThickness,
            Opacity = _currentStrokeOpacity,
            Diameter = GetSelectedDiameter(),
            Comment = AnnotationCommentTextBox.Text.Trim()
        };
    }

    /// 右パネルで現在選択されている口径を取得する。
    private string GetSelectedDiameter()
    {
        if (DiameterComboBox.SelectedItem is ComboBoxItem selectedItem &&
            selectedItem.Content is string diameter &&
            !string.IsNullOrWhiteSpace(diameter))
        {
            return diameter;
        }

        return "未設定";
    }

    /// ストローク色と透明度からWPFのBrushを作成する。
    private static Brush CreateStrokeBrush(
        StrokeColor color,
        byte opacity)
    {
        Color baseColor =
            GetMediaColor(color);

        var brush =
            new SolidColorBrush(
                Color.FromArgb(
                    opacity,
                    baseColor.R,
                    baseColor.G,
                    baseColor.B));

        brush.Freeze();

        return brush;
    }

    /// StrokeColorをWPFのColorへ変換する。
    private static Color GetMediaColor(
        StrokeColor color)
    {
        return color switch
        {
            StrokeColor.Blue => Color.FromRgb(0, 80, 220),
            StrokeColor.Green => Color.FromRgb(0, 150, 70),
            StrokeColor.Yellow => Color.FromRgb(255, 230, 0),
            StrokeColor.Orange => Color.FromRgb(255, 145, 0),
            StrokeColor.Pink => Color.FromRgb(255, 105, 180),
            StrokeColor.LightBlue => Color.FromRgb(80, 190, 255),
            StrokeColor.LightGreen => Color.FromRgb(100, 220, 120),
            StrokeColor.Purple => Color.FromRgb(150, 80, 210),
            StrokeColor.Brown => Color.FromRgb(150, 90, 40),
            StrokeColor.Gray => Color.FromRgb(120, 120, 120),
            StrokeColor.Cyan => Color.FromRgb(0, 210, 210),
            StrokeColor.Magenta => Color.FromRgb(220, 0, 180),
            _ => Color.FromRgb(220, 0, 0)
        };
    }


    /// 画面表示に使用するストローク色を取得する。
    /// 自動変更が有効な場合、現在選択中の口径以外のチェック注釈をグレー表示にする。
    /// 元のStrokeModel.Colorは変更しないため、OFFに戻すと元の色へ復元される。
    private StrokeColor GetDisplayStrokeColor(
        StrokeModel stroke)
    {
        if (AutoColorChangeCheckBox?.IsChecked != true ||
            stroke.Mode != DrawingMode.Check)
        {
            return stroke.Color;
        }

        string selectedDiameter =
            GetSelectedDiameter();

        // 口径が未設定、または現在選択中の口径と一致する線は元の色で表示する。
        if (string.IsNullOrWhiteSpace(stroke.Diameter) ||
            string.Equals(
                stroke.Diameter,
                "未設定",
                StringComparison.Ordinal) ||
            string.Equals(
                stroke.Diameter,
                selectedDiameter,
                StringComparison.Ordinal))
        {
            return stroke.Color;
        }

        return StrokeColor.Gray;
    }

    /// 保存されているストロークを再描画する。
    private void RedrawStrokes()
    {
        DrawingCanvas.Children.Clear();

        foreach (StrokeModel stroke in _strokes)
        {
            // 非表示中のモードは画面へ描画しない。
            if (!IsStrokeVisible(stroke))
            {
                continue;
            }

            var polyline = new Polyline
            {
                Stroke =
                    CreateStrokeBrush(
                        GetDisplayStrokeColor(stroke),
                        stroke.Opacity),

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

        if (_selectedStroke != null &&
            _strokes.Contains(_selectedStroke) &&
            IsStrokeVisible(_selectedStroke))
        {
            DrawSelectionAdorner(_selectedStroke);
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

        if (ReferenceEquals(_selectedStroke, stroke))
        {
            _selectedStroke = null;
        }

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

    /// 全ページの描画内容を名前を付けて保存する。
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

        // 最後に表示しているページの編集内容も保存対象へ反映する。
        SaveCurrentPageState();

        // まだ表示していないページも含め、全ページの既存注釈を保存対象へ読み込む。
        EnsureAllPageAnnotationsLoadedForSave();

        bool hasAnyStrokes =
            _pageStrokes.Values.Any(
                strokes => strokes.Any(IsStrokeVisible));

        if (!hasAnyStrokes)
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
            var savePageStrokes =
                new Dictionary<int, IReadOnlyList<StrokeModel>>();

            foreach ((int pageIndex, List<StrokeModel> strokes)
                in _pageStrokes.OrderBy(entry => entry.Key))
            {
                List<StrokeModel> visibleStrokes =
                    strokes
                        .Where(IsStrokeVisible)
                        .ToList();

                if (visibleStrokes.Count == 0)
                {
                    continue;
                }

                var pageSize =
                    _pdfService.GetPageSize(
                        _currentPdfPath,
                        pageIndex);

                bool isRotated270 =
                    pageSize.Width > pageSize.Height;

                List<StrokeModel> convertedStrokes =
                    visibleStrokes
                        .Select(stroke =>
                            ConvertStrokeForPdfSave(
                                stroke,
                                isRotated270,
                                pageSize.Height))
                        .ToList();

                savePageStrokes[pageIndex] =
                    convertedStrokes;
            }

            _pdfService.SaveInkAnnotations(
                _currentPdfPath,
                dialog.FileName,
                savePageStrokes);

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
        bool isRotated270,
        double pdfPageHeight)
    {
        var converted =
            new StrokeModel
            {
                Mode = source.Mode,
                Color = source.Color,
                Thickness = source.Thickness,
                Opacity = source.Opacity,
                Diameter = source.Diameter,
                Comment = source.Comment
            };

        foreach (Point point in source.PdfPoints)
        {
            Point savePoint;

            if (isRotated270)
            {
                // PoCで確認した横向きPDFの270度補正。
                savePoint =
                    new Point(
                        pdfPageHeight - point.Y,
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

        converted.RecalculateSelectionBounds();
        return converted;
    }

    /// 読み込んだInk注釈を画面表示用の座標へ変換する。
    private StrokeModel ConvertStrokeFromPdfLoad(
        StrokeModel source,
        bool isRotated270)
    {
        return ConvertStrokeFromPdfLoad(
            source,
            isRotated270,
            _pdfPageHeight);
    }

    /// 読み込んだInk注釈を、指定ページ高さを使って画面表示用座標へ変換する。
    private static StrokeModel ConvertStrokeFromPdfLoad(
        StrokeModel source,
        bool isRotated270,
        double pdfPageHeight)
    {
        var converted =
            new StrokeModel
            {
                Mode = source.Mode,
                Color = source.Color,
                Thickness = source.Thickness,
                Opacity = source.Opacity,
                Diameter = source.Diameter,
                Comment = source.Comment
            };

        foreach (Point point in source.PdfPoints)
        {
            Point displayPoint;

            if (isRotated270)
            {
                // 保存時に行った270度補正を元へ戻す。
                displayPoint =
                    new Point(
                        point.Y,
                        pdfPageHeight - point.X);
            }
            else
            {
                displayPoint =
                    point;
            }

            converted.PdfPoints.Add(
                displayPoint);
        }

        converted.RecalculateSelectionBounds();
        return converted;
    }

    /// 保存前に、まだ表示していない全ページのPDF注釈をページ別データへ読み込む。
    private void EnsureAllPageAnnotationsLoadedForSave()
    {
        if (string.IsNullOrWhiteSpace(_currentPdfPath))
        {
            return;
        }

        for (int pageIndex = 0;
            pageIndex < _pageCount;
            pageIndex++)
        {
            if (_loadedAnnotationPages.Contains(pageIndex))
            {
                continue;
            }

            var pageSize =
                _pdfService.GetPageSize(
                    _currentPdfPath,
                    pageIndex);

            bool isRotated270 =
                pageSize.Width > pageSize.Height;

            List<StrokeModel> loadedStrokes =
                _pdfService.LoadInkAnnotations(
                    _currentPdfPath,
                    pageIndex);

            var convertedStrokes =
                new List<StrokeModel>();

            foreach (StrokeModel stroke in loadedStrokes)
            {
                StrokeModel convertedStroke =
                    ConvertStrokeFromPdfLoad(
                        stroke,
                        isRotated270,
                        pageSize.Height);

                convertedStrokes.Add(
                    convertedStroke);
            }

            _pageStrokes[pageIndex] =
                convertedStrokes;

            _pageRedoStrokes[pageIndex] =
                new Stack<StrokeModel>();

            _loadedAnnotationPages.Add(pageIndex);
        }
    }

    /// 現在ページの描画内容とRedo状態をページ別に退避する。
    private void SaveCurrentPageState()
    {
        if (string.IsNullOrWhiteSpace(_currentPdfPath) ||
            _currentPageIndex < 0 ||
            _currentPageIndex >= _pageCount)
        {
            return;
        }

        _pageStrokes[_currentPageIndex] =
            new List<StrokeModel>(_strokes);

        // Stackの先頭が変わらないよう、列挙順を反転して複製する。
        _pageRedoStrokes[_currentPageIndex] =
            new Stack<StrokeModel>(
                _redoStrokes.Reverse());
    }

    /// ページ別に保持した描画を復元し、初回表示時だけPDF注釈を読み込む。
    private void RestoreCurrentPageStrokes()
    {
        if (string.IsNullOrWhiteSpace(_currentPdfPath))
        {
            return;
        }

        if (!_loadedAnnotationPages.Contains(_currentPageIndex))
        {
            LoadCurrentPageAnnotationsFromPdf();
            _loadedAnnotationPages.Add(_currentPageIndex);

            // 読み込んだ時点の状態をページ別データとして保持する。
            SaveCurrentPageState();
            return;
        }

        _strokes.Clear();

        if (_pageStrokes.TryGetValue(
                _currentPageIndex,
                out List<StrokeModel>? pageStrokes))
        {
            _strokes.AddRange(pageStrokes);
        }

        _redoStrokes.Clear();

        if (_pageRedoStrokes.TryGetValue(
                _currentPageIndex,
                out Stack<StrokeModel>? pageRedoStrokes))
        {
            foreach (StrokeModel stroke in pageRedoStrokes.Reverse())
            {
                _redoStrokes.Push(stroke);
            }
        }
    }

    /// 現在ページのInk注釈をPDFから読み込む。
    private void LoadCurrentPageAnnotationsFromPdf()
    {
        if (string.IsNullOrWhiteSpace(_currentPdfPath))
        {
            return;
        }

        List<StrokeModel> loadedStrokes =
            _pdfService.LoadInkAnnotations(
                _currentPdfPath,
                _currentPageIndex);

        bool isRotated270 =
            _pdfPageWidth > _pdfPageHeight;

        _strokes.Clear();

        foreach (StrokeModel stroke in loadedStrokes)
        {
            StrokeModel convertedStroke =
                ConvertStrokeFromPdfLoad(
                    stroke,
                    isRotated270);

            _strokes.Add(
                convertedStroke);
        }

        _redoStrokes.Clear();
    }

    /// 指定されたストロークが現在の表示対象か確認する。
    private bool IsStrokeVisible(
        StrokeModel stroke)
    {
        return stroke.Mode switch
        {
            DrawingMode.Markup => _isMarkupVisible,
            DrawingMode.Check => _isCheckVisible,
            _ => true
        };
    }

    /// 現在の描画モードを、必要に応じて表示状態へ戻す。
    private void EnsureCurrentDrawingModeVisible()
    {
        if (_currentDrawingMode == DrawingMode.Markup &&
            !_isMarkupVisible)
        {
            _isMarkupVisible = true;
            UpdateVisibilityButtons();
            RedrawStrokes();
        }
        else if (_currentDrawingMode == DrawingMode.Check &&
                 !_isCheckVisible)
        {
            _isCheckVisible = true;
            UpdateVisibilityButtons();
            RedrawStrokes();
        }
    }

    /// 朱書き注釈の表示／非表示を切り替える。
    private void MarkupVisibilityButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _isMarkupVisible = !_isMarkupVisible;

        ClearSelectionIfHidden();
        UpdateVisibilityButtons();
        RedrawStrokes();
    }

    /// チェック注釈の表示／非表示を切り替える。
    private void CheckVisibilityButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _isCheckVisible = !_isCheckVisible;

        ClearSelectionIfHidden();
        UpdateVisibilityButtons();
        RedrawStrokes();
    }

    /// 非表示になった注釈が選択中なら、選択状態を解除する。
    private void ClearSelectionIfHidden()
    {
        if (_selectedStroke != null &&
            !IsStrokeVisible(_selectedStroke))
        {
            _selectedStroke = null;
        }
    }

    /// 目ボタンの表示と説明を現在の表示状態へ合わせる。
    private void UpdateVisibilityButtons()
    {
        if (MarkupVisibilityButton != null)
        {
            MarkupVisibilityButton.Content =
                _isMarkupVisible
                    ? "👁"
                    : "⊘";

            MarkupVisibilityButton.ToolTip =
                _isMarkupVisible
                    ? "朱書きを非表示にする"
                    : "朱書きを表示する";

            MarkupVisibilityButton.Opacity =
                _isMarkupVisible
                    ? 1.0
                    : 0.55;
        }

        if (CheckVisibilityButton != null)
        {
            CheckVisibilityButton.Content =
                _isCheckVisible
                    ? "👁"
                    : "⊘";

            CheckVisibilityButton.ToolTip =
                _isCheckVisible
                    ? "チェックを非表示にする"
                    : "チェックを表示する";

            CheckVisibilityButton.Opacity =
                _isCheckVisible
                    ? 1.0
                    : 0.55;
        }
    }

    /// 朱書きモードの表示へ切り替える。
    private void MarkupModeButton_Checked(
        object sender,
        RoutedEventArgs e)
    {
        SaveCurrentModeSettings();

        _currentDrawingMode = DrawingMode.Markup;
        _currentStrokeColor = _lastMarkupColor;
        _currentStrokeThickness = _lastMarkupThickness;
        _currentStrokeOpacity = _lastMarkupOpacity;

        var modeBrush =
            new SolidColorBrush(
                Color.FromRgb(
                    252,
                    232,
                    232));

        if (MainToolBarBorder != null)
        {
            MainToolBarBorder.Background =
                modeBrush;
        }

        if (DrawingSettingsHeaderBorder != null)
        {
            DrawingSettingsHeaderBorder.Background =
                modeBrush;
        }

        if (AnnotationInfoHeaderBorder != null)
        {
            AnnotationInfoHeaderBorder.Background =
                modeBrush;
        }

        if (CurrentDrawingModeText != null)
        {
            CurrentDrawingModeText.Text =
                "朱書き";
        }

        UpdateDrawingSettingsUi();
    }

    /// チェックモードの表示へ切り替える。
    private void CheckModeButton_Checked(
        object sender,
        RoutedEventArgs e)
    {
        SaveCurrentModeSettings();

        _currentDrawingMode = DrawingMode.Check;
        _currentStrokeColor = _lastCheckColor;
        _currentStrokeThickness = _lastCheckThickness;
        _currentStrokeOpacity = _lastCheckOpacity;

        var modeBrush =
            new SolidColorBrush(
                Color.FromRgb(
                    255,
                    246,
                    204));

        if (MainToolBarBorder != null)
        {
            MainToolBarBorder.Background =
                modeBrush;
        }

        if (DrawingSettingsHeaderBorder != null)
        {
            DrawingSettingsHeaderBorder.Background =
                modeBrush;
        }

        if (AnnotationInfoHeaderBorder != null)
        {
            AnnotationInfoHeaderBorder.Background =
                modeBrush;
        }

        if (CurrentDrawingModeText != null)
        {
            CurrentDrawingModeText.Text =
                "チェック";
        }

        UpdateDrawingSettingsUi();
    }


    /// 現在モードの設定をモード別の最終値として保存する。
    private void SaveCurrentModeSettings()
    {
        if (_currentDrawingMode == DrawingMode.Markup)
        {
            _lastMarkupColor = _currentStrokeColor;
            _lastMarkupThickness = _currentStrokeThickness;
            _lastMarkupOpacity = _currentStrokeOpacity;
            return;
        }

        _lastCheckColor = _currentStrokeColor;
        _lastCheckThickness = _currentStrokeThickness;
        _lastCheckOpacity = _currentStrokeOpacity;
    }

    /// 色ボタンのTagに指定された色を現在の描画色へ設定する。
    private void StrokeColorButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not string colorName ||
            !Enum.TryParse(
                colorName,
                true,
                out StrokeColor color))
        {
            return;
        }

        _currentStrokeColor = color;

        // 色ごとに標準透明度を保持する方針。
        _currentStrokeOpacity =
            _currentDrawingMode == DrawingMode.Markup
                ? (byte)255
                : (byte)96;

        SaveCurrentModeSettings();
        UpdateDrawingSettingsUi();
    }

    /// 太さスライダーの値を現在の描画太さへ設定する。
    private void StrokeThicknessSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        double thickness =
            Math.Clamp(
                e.NewValue,
                0.1,
                100.0);

        _currentStrokeThickness = thickness;
        SaveCurrentModeSettings();

        if (StrokeThicknessText != null)
        {
            StrokeThicknessText.Text =
                thickness.ToString("0.0");
        }

        UpdateStrokeSettingPreviews();
    }

    /// 透明度スライダーの値を現在の不透明度へ設定する。
    private void StrokeOpacitySlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        double percent =
            Math.Clamp(
                e.NewValue,
                0,
                100);

        _currentStrokeOpacity =
            (byte)Math.Round(
                percent / 100.0 * 255.0);

        SaveCurrentModeSettings();

        if (StrokeOpacityText != null)
        {
            StrokeOpacityText.Text =
                $"{percent:0}%";
        }

        UpdateStrokeSettingPreviews();
    }

    /// 現在選択されている色ボタンだけを選択表示にする。
    private void UpdateColorPaletteSelection()
    {
        Style normalStyle =
            (Style)FindResource(
                "ColorPaletteButtonStyle");

        Style selectedStyle =
            (Style)FindResource(
                "SelectedColorPaletteButtonStyle");

        foreach (Button button in GetColorPaletteButtons())
        {
            bool isSelected =
                button.Tag is string colorName &&
                Enum.TryParse(
                    colorName,
                    true,
                    out StrokeColor buttonColor) &&
                buttonColor == _currentStrokeColor;

            button.Style =
                isSelected
                    ? selectedStyle
                    : normalStyle;
        }
    }

    /// 朱書き・チェック両方の色ボタンを列挙する。
    private IEnumerable<Button> GetColorPaletteButtons()
    {
        if (MarkupColorPalette != null)
        {
            foreach (object child in MarkupColorPalette.Children)
            {
                if (child is Button button)
                {
                    yield return button;
                }
            }
        }

        if (CheckColorPalette != null)
        {
            foreach (object child in CheckColorPalette.Children)
            {
                if (child is Button button)
                {
                    yield return button;
                }
            }
        }
    }

    /// 太さと透明度の凡例へ現在の描画設定を反映する。
    private void UpdateStrokeSettingPreviews()
    {
        Brush previewBrush =
            CreateStrokeBrush(
                _currentStrokeColor,
                255);

        if (StrokeThicknessPreviewLine != null)
        {
            StrokeThicknessPreviewLine.Stroke =
                previewBrush;

            StrokeThicknessPreviewLine.StrokeThickness =
                Math.Clamp(
                    _currentStrokeThickness,
                    1.0,
                    12.0);
        }

        if (StrokeOpacityPreviewBorder != null)
        {
            StrokeOpacityPreviewBorder.Background =
                previewBrush;

            StrokeOpacityPreviewBorder.Opacity =
                _currentStrokeOpacity / 255.0;
        }
    }

    /// 現在の描画設定を右パネルへ反映する。
    private void UpdateDrawingSettingsUi()
    {
        if (MarkupColorPalette != null)
        {
            MarkupColorPalette.Visibility =
                _currentDrawingMode == DrawingMode.Markup
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        if (CheckColorPalette != null)
        {
            CheckColorPalette.Visibility =
                _currentDrawingMode == DrawingMode.Check
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        if (StrokeThicknessSlider != null)
        {
            StrokeThicknessSlider.Value =
                Math.Clamp(
                    _currentStrokeThickness,
                    StrokeThicknessSlider.Minimum,
                    StrokeThicknessSlider.Maximum);
        }

        if (StrokeThicknessText != null)
        {
            StrokeThicknessText.Text =
                _currentStrokeThickness.ToString("0.0");
        }

        double opacityPercent =
            _currentStrokeOpacity / 255.0 * 100.0;

        if (StrokeOpacitySlider != null)
        {
            StrokeOpacitySlider.Value =
                Math.Clamp(
                    opacityPercent,
                    StrokeOpacitySlider.Minimum,
                    StrokeOpacitySlider.Maximum);
        }

        if (StrokeOpacityText != null)
        {
            StrokeOpacityText.Text =
                $"{opacityPercent:0}%";
        }

        UpdateColorPaletteSelection();
        UpdateStrokeSettingPreviews();
    }

    /// ページ一覧の選択変更を受け取る。
    private void PageListBox_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // ページ一覧の実装時に、選択ページへの移動処理を追加する。
    }


    /// 左側のページ一覧を開閉する。
    private void ToggleLeftPanelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isLeftPanelOpen)
        {
            if (LeftPanelColumn.ActualWidth > 0)
            {
                _leftPanelOpenWidth =
                    new GridLength(
                        LeftPanelColumn.ActualWidth);
            }

            LeftPanelColumn.MinWidth =
                0;

            LeftPanelColumn.Width =
                new GridLength(0);

            LeftPanelSplitter.IsEnabled =
                false;

            ToggleLeftPanelButton.Content =
                "▶";

            ToggleLeftPanelButton.ToolTip =
                "ページ一覧を開く";
        }
        else
        {
            LeftPanelColumn.MinWidth =
                220;

            LeftPanelColumn.Width =
                new GridLength(
                    Math.Max(
                        220,
                        _leftPanelOpenWidth.Value));

            LeftPanelSplitter.IsEnabled =
                true;

            ToggleLeftPanelButton.Content =
                "◀";

            ToggleLeftPanelButton.ToolTip =
                "ページ一覧を閉じる";
        }

        _isLeftPanelOpen =
            !_isLeftPanelOpen;
    }

    /// 右側の設定パネルを開閉する。
    private void ToggleRightPanelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isRightPanelOpen)
        {
            if (RightPanelColumn.ActualWidth > 0)
            {
                _rightPanelOpenWidth =
                    new GridLength(
                        RightPanelColumn.ActualWidth);
            }

            RightPanelColumn.MinWidth =
                0;

            RightPanelColumn.Width =
                new GridLength(0);

            RightPanelSplitter.IsEnabled =
                false;

            ToggleRightPanelButton.Content =
                "◀";

            ToggleRightPanelButton.ToolTip =
                "設定パネルを開く";
        }
        else
        {
            RightPanelColumn.MinWidth =
                260;

            RightPanelColumn.Width =
                new GridLength(
                    Math.Max(
                        260,
                        _rightPanelOpenWidth.Value));

            RightPanelSplitter.IsEnabled =
                true;

            ToggleRightPanelButton.Content =
                "▶";

            ToggleRightPanelButton.ToolTip =
                "設定パネルを閉じる";
        }

        _isRightPanelOpen =
            !_isRightPanelOpen;
    }


    /// 右側設定パネルの幅を変更する。
    private void RightPanelSplitter_DragDelta(
        object sender,
        System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (!_isRightPanelOpen)
        {
            return;
        }

        double newWidth =
            RightPanelColumn.ActualWidth -
            e.HorizontalChange;

        RightPanelColumn.Width =
            new GridLength(
                Math.Max(
                    260,
                    newWidth));

        _rightPanelOpenWidth =
            RightPanelColumn.Width;
    }

}
