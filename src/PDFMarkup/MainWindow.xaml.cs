using Microsoft.Win32;
using PDFMarkup.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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
    /// 現在使用している操作ツールを表す。
    private enum ToolMode
    {
        Drawing,
        Select,
        Eraser,
        Hand
    }

    /// Undo / Redoで扱う操作の種類。
    private enum UndoActionType
    {
        AddStroke,
        DeleteStroke,
        EditDiameter,
        EditComment,
        EditColor,
        EditThickness,
        EditOpacity
    }

    /// 1回分の操作履歴を保持する。
    private sealed class UndoAction
    {
        public UndoActionType Type { get; init; }

        public StrokeModel Stroke { get; init; } = null!;

        public int StrokeIndex { get; init; }

        public string OldValue { get; init; } = string.Empty;

        public string NewValue { get; init; } = string.Empty;
    }

    /// 左側ページ一覧へ表示する1ページ分の情報を保持する。
    private sealed class PageThumbnailItem : INotifyPropertyChanged
    {
        private BitmapSource? _thumbnail;

        public int PageIndex { get; init; }

        public int PageNumber =>
            PageIndex + 1;

        public BitmapSource? Thumbnail
        {
            get => _thumbnail;

            set
            {
                if (ReferenceEquals(
                        _thumbnail,
                        value))
                {
                    return;
                }

                _thumbnail = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(
            [CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }
    }

    // ページ移動コマンド
    private static readonly RoutedCommand PreviousPageCommand = new();
    private static readonly RoutedCommand NextPageCommand = new();    
    
    // サービス
    private readonly PdfService _pdfService = new();

    // 現在ページの描画データ
    private readonly List<StrokeModel> _strokes = new();

    // 現在ページの操作履歴。
    private readonly Stack<UndoAction> _undoActions = new();
    private readonly Stack<UndoAction> _redoActions = new();

    // ページごとの未保存描画と操作履歴を保持する。
    private readonly Dictionary<int, List<StrokeModel>> _pageStrokes = new();
    private readonly Dictionary<int, Stack<UndoAction>> _pageUndoActions = new();
    private readonly Dictionary<int, Stack<UndoAction>> _pageRedoActions = new();
    private readonly HashSet<int> _loadedAnnotationPages = new();

    // 左側のサムネイル一覧。
    private readonly ObservableCollection<PageThumbnailItem> _pageThumbnails = new();

    // PDFを開き直したとき、古いサムネイル生成を停止する。
    private CancellationTokenSource? _thumbnailCancellation;

    // コードからページ一覧の選択を同期している間は、ページ移動を発生させない。
    private bool _isUpdatingPageListSelection;

    private StrokeModel? _currentStrokeModel;
    private Polyline? _currentStrokeView;

    // 直線・矢印描画時のプレビューに使用するシャドウ線。
    private Line? _shadowLine;

    // 矢印プレビューの始点側に表示する2本の矢羽根。
    private Line? _shadowArrowHeadLine1;
    private Line? _shadowArrowHeadLine2;

    // 現在Ctrl+Shiftによる矢印プレビュー中かを表す。
    private bool _isArrowPreviewActive;

    // 現在描画中ストロークの開始位置。
    // Shift直線プレビューは常にこの位置から現在位置まで表示する。
    private Point _drawingStartCanvasPoint;

    // 現在、Shiftによる直線プレビュー中かを表す。
    private bool _isStraightPreviewActive;

    // 現在選択されている注釈。
    private StrokeModel? _selectedStroke;

    // 注釈選択時など、右パネルをコードから更新している間は
    // 編集イベントを選択中注釈へ反映しない。
    private bool _isUpdatingAnnotationPanel;

    // 選択中注釈のモード表示をコードから同期している間は、
    // 描画ツールへの切替や基本設定の復元を行わない。
    private bool _isApplyingSelectedStroke;

    // 口径ComboBoxをコードから更新している間は、
    // 口径強調表示の再描画を行わない。
    private bool _isUpdatingDiameterSelection;

    // コメント編集を1回のUndoとしてまとめるため、編集開始時の値を保持する。
    private StrokeModel? _commentEditingStroke;
    private string _commentEditOriginalValue = string.Empty;

    // 描画設定UIをコードから更新している間は、
    // スライダー変更を注釈編集として扱わない。
    private bool _isUpdatingDrawingSettingsUi;

    // 太さスライダーのドラッグを1回のUndoとしてまとめる。
    private StrokeModel? _thicknessEditingStroke;
    private double _thicknessEditOriginalValue;

    // 透明度スライダーのドラッグを1回のUndoとしてまとめる。
    private StrokeModel? _opacityEditingStroke;
    private byte _opacityEditOriginalValue;

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

    // 現在選択されている操作ツール。
    private ToolMode _currentToolMode = ToolMode.Drawing;

    // PDFをつかんで移動するパン操作の状態。
    private bool _isPanning;
    private Point _panStartPoint;
    private double _panStartHorizontalOffset;
    private double _panStartVerticalOffset;

    // 描画状態
    private bool _isDrawing;
    private Point _lastCanvasPoint;

    // 現在のPDF情報
    private string? _currentPdfPath;
    private int _currentPageIndex;
    private int _pageCount;

    private double _pdfPageWidth;
    private double _pdfPageHeight;

    // 全体表示時のPDF表示サイズ。
    private double _fitPageWidth;
    private double _fitPageHeight;

    // 全体表示を100%とした現在のズーム倍率。
    private double _zoomFactor = 1.0;

    private const double MinimumZoomFactor = 0.25;
    private const double MaximumZoomFactor = 5.0;
    private const double ZoomStep = 1.2;

    // 8方向スナップの吸着許容角度。
    private const double SnapAngleToleranceDegrees = 7.5;

    // A3長辺のPDFポイント値（420mm）。
    // A4はA3と同じ1.0倍とし、それより大きいページだけ自動拡大する。
    private const double A3LongSidePdfPoints = 1190.55;

    // A3基準の矢印サイズ（PDFポイント）。
    // PDF上の実寸として保持するため、描画時のズーム倍率には左右されない。
    private const double BaseArrowLengthPdfPoints = 16.0;
    private const double BaseArrowHalfWidthPdfPoints = 7.0;

    // 将来、右パネルの「小・標準・大」などから変更するための倍率。
    private double _arrowUserScale = 1.0;

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

        PageListBox.ItemsSource =
            _pageThumbnails;

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

        // Escキーで選択中の注釈を解除できるようにする。
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        // Shiftを離した瞬間に直線プレビューをキャンセルする。
        PreviewKeyUp += MainWindow_PreviewKeyUp;

        // 太さスライダーのドラッグ開始から終了までを、1回の編集として扱う。
        StrokeThicknessSlider.PreviewMouseLeftButtonDown +=
            StrokeThicknessSlider_PreviewMouseLeftButtonDown;

        StrokeThicknessSlider.PreviewMouseLeftButtonUp +=
            StrokeThicknessSlider_PreviewMouseLeftButtonUp;

        StrokeThicknessSlider.LostMouseCapture +=
            StrokeThicknessSlider_LostMouseCapture;

        // 透明度スライダーのドラッグ開始から終了までを、1回の編集として扱う。
        StrokeOpacitySlider.PreviewMouseLeftButtonDown +=
            StrokeOpacitySlider_PreviewMouseLeftButtonDown;

        StrokeOpacitySlider.PreviewMouseLeftButtonUp +=
            StrokeOpacitySlider_PreviewMouseLeftButtonUp;

        StrokeOpacitySlider.LostMouseCapture +=
            StrokeOpacitySlider_LostMouseCapture;

        // 将来の画像アイコン・専用カーソル差し替えに備え、
        // ボタン表示とカーソルは初期化処理からまとめて設定する。
        InitializeReplaceableUiVisuals();

        Closed +=
            MainWindow_Closed;
    }

    /// ウィンドウ終了時にバックグラウンド処理を停止する。
    private void MainWindow_Closed(
        object? sender,
        EventArgs e)
    {
        CancelThumbnailGeneration();
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
        _undoActions.Clear();
        _redoActions.Clear();
        _pageStrokes.Clear();
        _pageUndoActions.Clear();
        _pageRedoActions.Clear();
        _loadedAnnotationPages.Clear();

        CancelThumbnailGeneration();
        PreparePageThumbnailItems();

        _isDrawing = false;
        _currentStrokeModel = null;
        _currentStrokeView = null;
        _zoomFactor = 1.0;
        ClearStrokeSelection();

        DisplayCurrentPage();
        StartThumbnailGeneration();

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

        SyncPageListSelection();

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

        // 編集中のコメントを確定してからページを移動する。
        CommitCommentEditUndo();

        // 移動前ページの未保存描画をメモリへ退避する。
        SaveCurrentPageState();
        ClearStrokeSelection();

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
            PdfViewport.ViewportWidth;

        double viewportHeight =
            PdfViewport.ViewportHeight;

        if (viewportWidth <= 1 ||
            viewportHeight <= 1)
        {
            viewportWidth =
                PdfViewport.ActualWidth;

            viewportHeight =
                PdfViewport.ActualHeight;
        }

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

        const double viewportMargin = 60.0;

        double availableWidth =
            Math.Max(
                1,
                viewportWidth - viewportMargin);

        double availableHeight =
            Math.Max(
                1,
                viewportHeight - viewportMargin);

        double widthScale =
            availableWidth / imageWidth;

        double heightScale =
            availableHeight / imageHeight;

        double fitScale =
            Math.Min(
                widthScale,
                heightScale);

        _fitPageWidth =
            imageWidth * fitScale;

        _fitPageHeight =
            imageHeight * fitScale;

        ApplyZoomSize();
    }

    /// 現在のズーム倍率をPDFページと注釈Canvasへ反映する。
    private void ApplyZoomSize()
    {
        if (_fitPageWidth <= 0 ||
            _fitPageHeight <= 0)
        {
            return;
        }

        PdfPageHost.Width =
            _fitPageWidth * _zoomFactor;

        PdfPageHost.Height =
            _fitPageHeight * _zoomFactor;

        if (ZoomText != null)
        {
            ZoomText.Text =
                $"倍率: {_zoomFactor * 100:0}%";
        }

        ZoomOutButton.IsEnabled =
            _zoomFactor > MinimumZoomFactor;

        ZoomInButton.IsEnabled =
            _zoomFactor < MaximumZoomFactor;

        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(RedrawStrokes));
    }

    /// 指定倍率へ変更し、必要に応じて表示中心を維持する。
    private void SetZoomFactor(
        double zoomFactor,
        bool keepViewportCenter = true)
    {
        double newZoomFactor =
            Math.Clamp(
                zoomFactor,
                MinimumZoomFactor,
                MaximumZoomFactor);

        if (Math.Abs(
                newZoomFactor - _zoomFactor) < 0.0001)
        {
            return;
        }

        double oldScrollableWidth =
            Math.Max(
                1,
                PdfViewport.ExtentWidth);

        double oldScrollableHeight =
            Math.Max(
                1,
                PdfViewport.ExtentHeight);

        double centerXRatio =
            (PdfViewport.HorizontalOffset +
             PdfViewport.ViewportWidth / 2.0) /
            oldScrollableWidth;

        double centerYRatio =
            (PdfViewport.VerticalOffset +
             PdfViewport.ViewportHeight / 2.0) /
            oldScrollableHeight;

        _zoomFactor =
            newZoomFactor;

        ApplyZoomSize();

        if (!keepViewportCenter)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                double targetHorizontalOffset =
                    centerXRatio * PdfViewport.ExtentWidth -
                    PdfViewport.ViewportWidth / 2.0;

                double targetVerticalOffset =
                    centerYRatio * PdfViewport.ExtentHeight -
                    PdfViewport.ViewportHeight / 2.0;

                PdfViewport.ScrollToHorizontalOffset(
                    Math.Max(
                        0,
                        targetHorizontalOffset));

                PdfViewport.ScrollToVerticalOffset(
                    Math.Max(
                        0,
                        targetVerticalOffset));
            }));
    }

    /// 拡大ボタンでPDFを一段階拡大する。
    private void ZoomInButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetZoomFactor(
            _zoomFactor * ZoomStep);
    }

    /// 縮小ボタンでPDFを一段階縮小する。
    private void ZoomOutButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetZoomFactor(
            _zoomFactor / ZoomStep);
    }

    /// PDFを表示領域全体へ収める。
    private void FitPageButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _zoomFactor = 1.0;
        UpdatePdfPageDisplaySize();

        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                PdfViewport.ScrollToHorizontalOffset(0);
                PdfViewport.ScrollToVerticalOffset(0);
            }));
    }

    /// 中ボタン、手のひらツール、Space＋左ボタンでパン操作を開始する。
    private void PdfViewport_PreviewMouseDown(
        object sender,
        MouseButtonEventArgs e)
    {
        bool isMiddleButton =
            e.ChangedButton == MouseButton.Middle;

        bool isHandLeftButton =
            e.ChangedButton == MouseButton.Left &&
            _currentToolMode == ToolMode.Hand;

        bool isSpaceLeftButton =
            e.ChangedButton == MouseButton.Left &&
            Keyboard.IsKeyDown(Key.Space);

        if (!isMiddleButton &&
            !isHandLeftButton &&
            !isSpaceLeftButton)
        {
            return;
        }

        BeginPan(
            e.GetPosition(PdfViewport));

        e.Handled = true;
    }

    /// パン操作中のマウス移動をスクロール位置へ反映する。
    private void PdfViewport_PreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        Point currentPoint =
            e.GetPosition(PdfViewport);

        double deltaX =
            currentPoint.X - _panStartPoint.X;

        double deltaY =
            currentPoint.Y - _panStartPoint.Y;

        PdfViewport.ScrollToHorizontalOffset(
            _panStartHorizontalOffset - deltaX);

        PdfViewport.ScrollToVerticalOffset(
            _panStartVerticalOffset - deltaY);

        e.Handled = true;
    }

    /// マウスボタンを離した時点でパン操作を終了する。
    private void PdfViewport_PreviewMouseUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        bool shouldEnd =
            e.ChangedButton == MouseButton.Middle ||
            e.ChangedButton == MouseButton.Left;

        if (!shouldEnd)
        {
            return;
        }

        EndPan();
        e.Handled = true;
    }

    /// PDFをつかんで移動する処理を開始する。
    private void BeginPan(
        Point startPoint)
    {
        _isPanning = true;
        _panStartPoint = startPoint;
        _panStartHorizontalOffset =
            PdfViewport.HorizontalOffset;
        _panStartVerticalOffset =
            PdfViewport.VerticalOffset;

        PdfViewport.CaptureMouse();
        PdfViewport.Cursor =
            Cursors.SizeAll;
    }

    /// PDFをつかんで移動する処理を終了する。
    private void EndPan()
    {
        _isPanning = false;
        if (PdfViewport.IsMouseCaptured)
        {
            PdfViewport.ReleaseMouseCapture();
        }

        PdfViewport.Cursor =
            Cursors.Arrow;

        UpdateCanvasCursor();
    }

    /// マウスホイールをPDFのズーム操作として処理する。
    private void PdfViewport_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        double zoomMultiplier =
            e.Delta > 0
                ? ZoomStep
                : 1.0 / ZoomStep;

        SetZoomFactor(
            _zoomFactor * zoomMultiplier);

        e.Handled = true;
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

        // 選択モードでは、クリックした注釈だけを選択する。
        // 空白部分をクリックした場合は選択を解除する。
        if (_currentToolMode == ToolMode.Select)
        {
            CommitCommentEditUndo();

            StrokeModel? hitStroke =
                FindStrokeAtCanvasPoint(canvasPoint);

            if (hitStroke != null)
            {
                SelectStroke(hitStroke);
            }
            else
            {
                ClearStrokeSelection();
                RedrawStrokes();
            }

            e.Handled = true;
            return;
        }

        // 消しゴムモードでは、クリックした注釈だけを削除する。
        // 空白部分をクリックしても新しい線は描画しない。
        if (_currentToolMode == ToolMode.Eraser)
        {
            StrokeModel? eraseTarget =
                FindStrokeAtCanvasPoint(canvasPoint);

            if (eraseTarget != null)
            {
                DeleteStroke(
                    eraseTarget);
            }

            e.Handled = true;
            return;
        }

        // 非表示中のモードで新しく描き始めた場合は、描画結果が見えるよう自動表示する。
        EnsureCurrentDrawingModeVisible();

        // 描画モードでは既存注釈との重なりに関係なく、新しい線を描く。
        // 編集中のコメントを確定してから新規描画へ移る。
        CommitCommentEditUndo();
        // 新規描画へ移るときは前の注釈選択を解除し、コメントを空にする。
        ClearStrokeSelection();
        _isDrawing = true;
        _lastCanvasPoint = canvasPoint;
        _isStraightPreviewActive = false;
        _isArrowPreviewActive = false;

        _currentStrokeModel =
            CreateStrokeFromCurrentSettings();

        Point pdfPoint =
            ConvertCanvasPointToPdfPoint(canvasPoint);

        _currentStrokeModel.PdfPoints.Add(pdfPoint);
        _strokes.Add(_currentStrokeModel);

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

        // Shiftを押した状態で描画を開始した場合は、
        // 現在までの軌跡を保存して直線プレビューへ切り替える。
        if (IsShiftPressed())
        {
            BeginStraightPreview();
        }

        // Canvas外へマウスが移動しても描画終了を取得できるようにする。
        DrawingCanvas.CaptureMouse();
    }

    /// Shiftキーが現在押されているか確認する。
    private static bool IsShiftPressed()
    {
        return (Keyboard.Modifiers & ModifierKeys.Shift) ==
               ModifierKeys.Shift;
    }

    /// Ctrlキーが現在押されているか確認する。
    private static bool IsControlPressed()
    {
        return (Keyboard.Modifiers & ModifierKeys.Control) ==
               ModifierKeys.Control;
    }

    /// Ctrl+Shiftが現在押されているか確認する。
    private static bool IsArrowShortcutPressed()
    {
        return IsShiftPressed() &&
               IsControlPressed();
    }

    /// Shift切替時点までのフリーハンド軌跡を保存し、
    /// ストローク開始点からの直線プレビューを開始する。
    private void BeginStraightPreview()
    {
        if (_isStraightPreviewActive ||
            _currentStrokeModel == null ||
            _currentStrokeView == null ||
            _currentStrokeView.Points.Count == 0)
        {
            return;
        }

        // 直線プレビューの始点は、
        // Shiftを押した位置ではなく常にストローク開始点とする。
        _drawingStartCanvasPoint =
            _currentStrokeView.Points[0];

        _isStraightPreviewActive = true;
        _isArrowPreviewActive =
            IsArrowShortcutPressed();

        // Shift中は保存済みのフリーハンド軌跡を一時的に隠し、
        // 直線プレビューだけを表示する。
        _currentStrokeView.Visibility =
            Visibility.Hidden;

        CreateShadowLine(
            _drawingStartCanvasPoint);
    }

    /// Shiftを離したとき、現在描画中の1ストロークを破棄する。
    /// ペンタブ操作ではカーソル位置を元へ戻さず、描き直す仕様とする。
    private void CancelCurrentDrawing()
    {
        if (!_isDrawing)
        {
            return;
        }

        RemoveShadowLine();

        if (_currentStrokeModel != null)
        {
            _strokes.Remove(
                _currentStrokeModel);
        }

        if (_currentStrokeView != null)
        {
            DrawingCanvas.Children.Remove(
                _currentStrokeView);
        }

        _isStraightPreviewActive = false;
        _isArrowPreviewActive = false;
        _isDrawing = false;

        _currentStrokeModel = null;
        _currentStrokeView = null;

        if (DrawingCanvas.IsMouseCaptured)
        {
            DrawingCanvas.ReleaseMouseCapture();
        }

        RedrawStrokes();
    }

    /// Shiftを押したまま描画終了した場合、
    /// ストローク開始点から終了位置までの直線として確定する。
    private void CommitStraightPreview(
        Point endPoint)
    {
        if (!_isStraightPreviewActive ||
            _currentStrokeModel == null ||
            _currentStrokeView == null)
        {
            return;
        }

        Point startPdfPoint =
            ConvertCanvasPointToPdfPoint(
                _drawingStartCanvasPoint);

        Point snappedEndPoint =
            SnapToEightDirections(
                _drawingStartCanvasPoint,
                endPoint);

        Point endPdfPoint =
            ConvertCanvasPointToPdfPoint(
                snappedEndPoint);

        _currentStrokeModel.PdfPoints.Clear();
        _currentStrokeModel.PdfPoints.Add(
            startPdfPoint);
        _currentStrokeModel.PdfPoints.Add(
            endPdfPoint);

        _currentStrokeView.Points.Clear();
        _currentStrokeView.Points.Add(
            _drawingStartCanvasPoint);
        _currentStrokeView.Points.Add(
            snappedEndPoint);

        _currentStrokeView.Visibility =
            Visibility.Visible;

        _lastCanvasPoint =
            snappedEndPoint;

        _isStraightPreviewActive = false;
        _isArrowPreviewActive = false;
        RemoveShadowLine();
    }

    /// Ctrl+Shiftを押したまま描画終了した場合、
    /// ストローク開始点側に矢印を付けて確定する。
    private void CommitArrowPreview(
        Point endPoint)
    {
        if (!_isStraightPreviewActive ||
            _currentStrokeModel == null ||
            _currentStrokeView == null)
        {
            return;
        }

        Point snappedEndPoint =
            SnapToEightDirections(
                _drawingStartCanvasPoint,
                endPoint);

        (Point arrowPoint1, Point arrowPoint2) =
            GetStartArrowHeadPoints(
                _drawingStartCanvasPoint,
                snappedEndPoint);

        Point arrowPdfPoint1 =
            ConvertCanvasPointToPdfPoint(
                arrowPoint1);

        Point startPdfPoint =
            ConvertCanvasPointToPdfPoint(
                _drawingStartCanvasPoint);

        Point arrowPdfPoint2 =
            ConvertCanvasPointToPdfPoint(
                arrowPoint2);

        Point endPdfPoint =
            ConvertCanvasPointToPdfPoint(
                snappedEndPoint);

        // 1本のInk注釈として保存できるよう、
        // 矢羽根1→始点→矢羽根2→始点→終点の順で点列を作る。
        _currentStrokeModel.PdfPoints.Clear();
        _currentStrokeModel.PdfPoints.Add(
            arrowPdfPoint1);
        _currentStrokeModel.PdfPoints.Add(
            startPdfPoint);
        _currentStrokeModel.PdfPoints.Add(
            arrowPdfPoint2);
        _currentStrokeModel.PdfPoints.Add(
            startPdfPoint);
        _currentStrokeModel.PdfPoints.Add(
            endPdfPoint);

        _currentStrokeView.Points.Clear();
        _currentStrokeView.Points.Add(
            arrowPoint1);
        _currentStrokeView.Points.Add(
            _drawingStartCanvasPoint);
        _currentStrokeView.Points.Add(
            arrowPoint2);
        _currentStrokeView.Points.Add(
            _drawingStartCanvasPoint);
        _currentStrokeView.Points.Add(
            snappedEndPoint);

        _currentStrokeView.Visibility =
            Visibility.Visible;

        _lastCanvasPoint =
            snappedEndPoint;

        _isStraightPreviewActive = false;
        _isArrowPreviewActive = false;
        RemoveShadowLine();
    }

    /// 現在ページの用紙サイズから、A3基準の矢印自動倍率を取得する。
    /// A4以下は1.0倍、A2は約1.4倍、A1は約2.0倍、A0は約2.8倍となる。
    private double GetArrowPaperScale()
    {
        if (_pdfPageWidth <= 0 ||
            _pdfPageHeight <= 0)
        {
            return 1.0;
        }

        double longSide =
            Math.Max(
                _pdfPageWidth,
                _pdfPageHeight);

        return Math.Max(
            1.0,
            longSide / A3LongSidePdfPoints);
    }

    /// PDFポイントで定義した矢印サイズを、現在のCanvas表示サイズへ変換する。
    /// これにより、どのズーム倍率で描いてもPDF保存後の矢印実寸は一定になる。
    private (double Length, double HalfWidth) GetArrowSizeInCanvas()
    {
        double canvasWidth =
            DrawingCanvas.ActualWidth;

        double canvasHeight =
            DrawingCanvas.ActualHeight;

        double canvasScaleX =
            _pdfPageWidth > 0
                ? canvasWidth / _pdfPageWidth
                : 1.0;

        double canvasScaleY =
            _pdfPageHeight > 0
                ? canvasHeight / _pdfPageHeight
                : canvasScaleX;

        // PDFとCanvasは同じ縦横比で表示しているため、平均倍率を使用する。
        double canvasScale =
            Math.Max(
                0.0001,
                (canvasScaleX + canvasScaleY) / 2.0);

        double paperScale =
            GetArrowPaperScale();

        double totalScale =
            paperScale *
            _arrowUserScale;

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
    private (Point Point1, Point Point2) GetStartArrowHeadPoints(
        Point startPoint,
        Point endPoint)
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
            GetArrowSizeInCanvas();

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

    /// 現在位置を描画中ストロークへ1点追加する。
    private void AddCurrentDrawingPoint(
        Point canvasPoint)
    {
        if (_currentStrokeModel == null ||
            _currentStrokeView == null)
        {
            return;
        }

        double distanceX =
            canvasPoint.X - _lastCanvasPoint.X;

        double distanceY =
            canvasPoint.Y - _lastCanvasPoint.Y;

        double distance =
            Math.Sqrt(
                distanceX * distanceX +
                distanceY * distanceY);

        // 点が増えすぎないよう、小さな移動は記録しない。
        if (distance < 2.0)
        {
            return;
        }

        Point pdfPoint =
            ConvertCanvasPointToPdfPoint(
                canvasPoint);

        _currentStrokeModel.PdfPoints.Add(
            pdfPoint);

        _currentStrokeView.Points.Add(
            canvasPoint);

        _lastCanvasPoint =
            canvasPoint;
    }

    /// 指定位置から現在位置までを示すシャドウ線を作成する。
    private void CreateShadowLine(
        Point startPoint)
    {
        RemoveShadowLine();

        _shadowLine =
            new Line
            {
                X1 = startPoint.X,
                Y1 = startPoint.Y,
                X2 = startPoint.X,
                Y2 = startPoint.Y,

                Stroke =
                    CreateStrokeBrush(
                        _currentStrokeColor,
                        120),

                StrokeThickness =
                    Math.Max(
                        1.0,
                        _currentStrokeThickness),

                StrokeDashArray =
                    new DoubleCollection
                    {
                        4,
                        3
                    },

                IsHitTestVisible = false
            };

        DrawingCanvas.Children.Add(
            _shadowLine);
    }

    /// シャドウ線の終点を現在のポインター位置へ更新する。
    /// 8方向に近い場合だけスナップし、それ以外は自由角度のまま表示する。
    private void UpdateShadowLine(
        Point currentPoint)
    {
        if (_shadowLine == null)
        {
            return;
        }

        Point displayPoint =
            SnapToEightDirections(
                _drawingStartCanvasPoint,
                currentPoint);

        bool isSnapped =
            Math.Abs(displayPoint.X - currentPoint.X) > 0.01 ||
            Math.Abs(displayPoint.Y - currentPoint.Y) > 0.01;

        // 自由角度は点線、8方向へ吸着したら実線にする。
        _shadowLine.StrokeDashArray =
            isSnapped
                ? null
                : new DoubleCollection { 4, 3 };

        if (_isArrowPreviewActive)
        {
            UpdateShadowArrowHead(
                _drawingStartCanvasPoint,
                displayPoint,
                isSnapped);
        }
        else
        {
            RemoveShadowArrowHead();
        }

        _shadowLine.X1 =
            _drawingStartCanvasPoint.X;

        _shadowLine.Y1 =
            _drawingStartCanvasPoint.Y;

        _shadowLine.X2 =
            displayPoint.X;

        _shadowLine.Y2 =
            displayPoint.Y;
    }

    /// 始点側へ矢印プレビューの矢羽根を表示する。
    private void UpdateShadowArrowHead(
        Point startPoint,
        Point endPoint,
        bool isSnapped)
    {
        (Point point1, Point point2) =
            GetStartArrowHeadPoints(
                startPoint,
                endPoint);

        if (_shadowArrowHeadLine1 == null)
        {
            _shadowArrowHeadLine1 =
                CreateShadowArrowHeadLine();

            DrawingCanvas.Children.Add(
                _shadowArrowHeadLine1);
        }

        if (_shadowArrowHeadLine2 == null)
        {
            _shadowArrowHeadLine2 =
                CreateShadowArrowHeadLine();

            DrawingCanvas.Children.Add(
                _shadowArrowHeadLine2);
        }

        DoubleCollection? dashArray =
            isSnapped
                ? null
                : new DoubleCollection { 4, 3 };

        _shadowArrowHeadLine1.StrokeDashArray =
            dashArray;

        _shadowArrowHeadLine2.StrokeDashArray =
            isSnapped
                ? null
                : new DoubleCollection { 4, 3 };

        _shadowArrowHeadLine1.X1 =
            startPoint.X;

        _shadowArrowHeadLine1.Y1 =
            startPoint.Y;

        _shadowArrowHeadLine1.X2 =
            point1.X;

        _shadowArrowHeadLine1.Y2 =
            point1.Y;

        _shadowArrowHeadLine2.X1 =
            startPoint.X;

        _shadowArrowHeadLine2.Y1 =
            startPoint.Y;

        _shadowArrowHeadLine2.X2 =
            point2.X;

        _shadowArrowHeadLine2.Y2 =
            point2.Y;
    }

    /// 矢印プレビュー用の矢羽根Lineを作成する。
    private Line CreateShadowArrowHeadLine()
    {
        return new Line
        {
            Stroke =
                CreateStrokeBrush(
                    _currentStrokeColor,
                    120),

            StrokeThickness =
                Math.Max(
                    1.0,
                    _currentStrokeThickness),

            IsHitTestVisible = false
        };
    }

    /// 表示中の矢印プレビューの矢羽根を削除する。
    private void RemoveShadowArrowHead()
    {
        if (_shadowArrowHeadLine1 != null)
        {
            DrawingCanvas.Children.Remove(
                _shadowArrowHeadLine1);

            _shadowArrowHeadLine1 = null;
        }

        if (_shadowArrowHeadLine2 != null)
        {
            DrawingCanvas.Children.Remove(
                _shadowArrowHeadLine2);

            _shadowArrowHeadLine2 = null;
        }
    }

    /// 指定された点が8方向に近い場合、最寄りの45度方向へ吸着させる。
    private static Point SnapToEightDirections(
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

    /// 表示中のシャドウ線をCanvasから削除する。
    private void RemoveShadowLine()
    {
        RemoveShadowArrowHead();

        if (_shadowLine == null)
        {
            return;
        }

        DrawingCanvas.Children.Remove(
            _shadowLine);

        _shadowLine = null;
    }

    /// 描画中のストロークまたはShift直線プレビューを更新する。
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

        if (IsShiftPressed())
        {
            if (!_isStraightPreviewActive)
            {
                BeginStraightPreview();
            }

            // Ctrl+Shiftなら始点側矢印、Shiftだけなら直線としてプレビューする。
            _isArrowPreviewActive =
                IsArrowShortcutPressed();

            UpdateShadowLine(
                canvasPoint);

            return;
        }

        if (_isStraightPreviewActive)
        {
            // 通常はPreviewKeyUpで即時キャンセルする。
            // キーイベントを取りこぼした場合の保険として、
            // MouseMove側でもShift解除を検出したらキャンセルする。
            CancelCurrentDrawing();

            return;
        }

        AddCurrentDrawingPoint(
            canvasPoint);
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

        Point endCanvasPoint =
            ClampCanvasPoint(
                e.GetPosition(DrawingCanvas));

        // MouseMoveを経由せずShiftが押された場合にも対応する。
        if (IsShiftPressed() &&
            !_isStraightPreviewActive)
        {
            BeginStraightPreview();
        }

        if (_isStraightPreviewActive)
        {
            if (IsArrowShortcutPressed())
            {
                // Ctrl+Shiftを押したまま離した場合は、始点側矢印として確定する。
                CommitArrowPreview(
                    endCanvasPoint);
            }
            else
            {
                // Shiftだけを押したまま離した場合は、直線として確定する。
                CommitStraightPreview(
                    endCanvasPoint);
            }
        }
        else
        {
            RemoveShadowLine();
        }

        _isDrawing = false;

        DrawingCanvas.ReleaseMouseCapture();

        if (_currentStrokeModel != null)
        {
            _currentStrokeModel.RecalculateSelectionBounds();

            PushUndoAction(
                new UndoAction
                {
                    Type = UndoActionType.AddStroke,
                    Stroke = _currentStrokeModel,
                    StrokeIndex = _strokes.IndexOf(_currentStrokeModel)
                });
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

    /// 選択中の注釈を解除し、コメント欄だけを空にする。
    private void ClearStrokeSelection()
    {
        _selectedStroke = null;

        if (AnnotationCommentTextBox == null)
        {
            return;
        }

        _isUpdatingAnnotationPanel = true;

        try
        {
            AnnotationCommentTextBox.Text = string.Empty;
        }
        finally
        {
            _isUpdatingAnnotationPanel = false;
        }
    }

    /// 直線・矢印プレビューで必要なキーを離した瞬間に描画をキャンセルする。
    private void MainWindow_PreviewKeyUp(
        object sender,
        KeyEventArgs e)
    {
        if (!_isDrawing ||
            !_isStraightPreviewActive)
        {
            return;
        }

        bool releasedShift =
            e.Key == Key.LeftShift ||
            e.Key == Key.RightShift;

        bool releasedControl =
            e.Key == Key.LeftCtrl ||
            e.Key == Key.RightCtrl;

        if (!releasedShift &&
            !(releasedControl && _isArrowPreviewActive))
        {
            return;
        }

        CancelCurrentDrawing();
        e.Handled = true;
    }

    /// Escキーによる選択解除と、Deleteキーによる注釈削除を処理する。
    private void MainWindow_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // 選択・消しゴム・手のひらモード中は、通常の描画モードへ戻す。
            if (_currentToolMode == ToolMode.Select ||
                _currentToolMode == ToolMode.Eraser ||
                _currentToolMode == ToolMode.Hand)
            {
                SetToolMode(
                    ToolMode.Drawing);

                ClearStrokeSelection();
                RedrawStrokes();
                e.Handled = true;
                return;
            }

            // 通常時は編集中のコメントを確定してから選択を解除する。
            CommitCommentEditUndo();

            ClearStrokeSelection();
            RedrawStrokes();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Delete)
        {
            return;
        }

        // コメントを実際に編集中のときだけ、TextBox側の文字削除を優先する。
        // 線を選び直した後もキーボードフォーカスだけがTextBoxへ残る場合があるため、
        // FocusedElementだけでは判定しない。
        if (Keyboard.FocusedElement is TextBox &&
            _commentEditingStroke != null)
        {
            return;
        }

        DeleteSelectedStroke();
        e.Handled = true;
    }

    /// 消しゴムボタンで、クリック消しゴムモードへ切り替える。
    private void EraserButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        CommitCommentEditUndo();
        ClearStrokeSelection();

        SetToolMode(
            _currentToolMode == ToolMode.Eraser
                ? ToolMode.Drawing
                : ToolMode.Eraser);

        RedrawStrokes();
    }

    /// 将来画像アイコンへ差し替える可能性があるUI表示を初期化する。
    private void InitializeReplaceableUiVisuals()
    {
        OpenPdfButton.Content =
            CreateIconContent(
                "file-open",
                "開く");

        SavePdfButton.Content =
            CreateIconContent(
                "file-save",
                "保存");

        SaveAsPdfButton.Content =
            CreateIconContent(
                "file-save-as",
                "別名保存");

        SelectToolButton.Content =
            CreateIconContent(
                "tool-select",
                "選択");

        EraserButton.Content =
            CreateIconContent(
                "tool-eraser",
                "消しゴム");

        HandToolButton.Content =
            CreateIconContent(
                "tool-hand",
                "手");

        UndoButton.Content =
            CreateIconContent(
                "undo",
                "↶");

        RedoButton.Content =
            CreateIconContent(
                "redo",
                "↷");

        PreviousPageButton.Content =
            CreateIconContent(
                "page-previous",
                "◀");

        NextPageButton.Content =
            CreateIconContent(
                "page-next",
                "▶");

        ZoomOutButton.Content =
            CreateIconContent(
                "zoom-out",
                "－");

        ZoomInButton.Content =
            CreateIconContent(
                "zoom-in",
                "＋");

        FitPageButton.Content =
            CreateIconContent(
                "zoom-fit",
                "全体");

        UpdateVisibilityButtons();

        ToggleLeftPanelButton.Content =
            CreateIconContent(
                "panel-left-close",
                "◀");

        ToggleRightPanelButton.Content =
            CreateIconContent(
                "panel-right-close",
                "▶");

        ApplyDrawingModeAppearance();
        UpdateDrawingModeButtonVisuals();
        UpdateCanvasCursor();
    }

    /// アイコンキーに対応するボタン表示を作成する。
    /// 現在は文字・記号を返し、将来ここでImageやPathへ差し替える。
    private static object CreateIconContent(
        string iconKey,
        string fallbackText)
    {
        // iconKeyは将来の画像アイコン検索に使用する。
        _ = iconKey;

        return fallbackText;
    }

    /// 現在のツールに対応するマウスカーソルを反映する。
    private void UpdateCanvasCursor()
    {
        if (DrawingCanvas == null)
        {
            return;
        }

        DrawingCanvas.Cursor =
            CreateToolCursor(
                _currentToolMode);
    }

    /// ツールに対応するカーソルを作成する。
    /// 将来ここで埋め込み.curファイルや独自カーソルへ差し替える。
    private static Cursor CreateToolCursor(
        ToolMode toolMode)
    {
        return toolMode switch
        {
            ToolMode.Select => Cursors.Arrow,
            ToolMode.Eraser => Cursors.Cross,
            ToolMode.Hand => Cursors.Hand,
            ToolMode.Drawing => Cursors.Pen,
            _ => Cursors.Arrow
        };
    }

    /// 選択ボタンで注釈選択モードを切り替える。
    /// 選択中にもう一度押した場合は、保持している朱書き／チェック描画へ戻る。
    private void SelectToolButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        CommitCommentEditUndo();
        ClearStrokeSelection();

        SetToolMode(
            _currentToolMode == ToolMode.Select
                ? ToolMode.Drawing
                : ToolMode.Select);

        RedrawStrokes();
    }

    /// 手のひらボタンで、左ドラッグによる移動モードを切り替える。
    private void HandToolButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        CommitCommentEditUndo();
        ClearStrokeSelection();

        SetToolMode(
            _currentToolMode == ToolMode.Hand
                ? ToolMode.Drawing
                : ToolMode.Hand);

        RedrawStrokes();
    }

    /// 現在の操作ツールを切り替え、ボタン表示とカーソルを更新する。
    private void SetToolMode(
        ToolMode toolMode)
    {
        _currentToolMode =
            toolMode;

        if (SelectToolButton != null)
        {
            bool isSelect =
                toolMode == ToolMode.Select;

            SelectToolButton.Opacity =
                isSelect
                    ? 1.0
                    : 0.72;

            SelectToolButton.FontWeight =
                isSelect
                    ? FontWeights.Bold
                    : FontWeights.Normal;

            SelectToolButton.ToolTip =
                isSelect
                    ? "選択モードを終了する"
                    : "注釈選択モード";
        }

        if (EraserButton != null)
        {
            bool isEraser =
                toolMode == ToolMode.Eraser;

            EraserButton.Opacity =
                isEraser
                    ? 1.0
                    : 0.72;

            EraserButton.FontWeight =
                isEraser
                    ? FontWeights.Bold
                    : FontWeights.Normal;

            EraserButton.ToolTip =
                isEraser
                    ? "消しゴムモードを終了する"
                    : "消しゴムモードに切り替える";
        }

        if (HandToolButton != null)
        {
            bool isHand =
                toolMode == ToolMode.Hand;

            HandToolButton.Opacity =
                isHand
                    ? 1.0
                    : 0.72;

            HandToolButton.FontWeight =
                isHand
                    ? FontWeights.Bold
                    : FontWeights.Normal;

            HandToolButton.ToolTip =
                isHand
                    ? "手のひらツールを終了する"
                    : "手のひらツールに切り替える";
        }

        UpdateDrawingModeButtonVisuals();
        UpdateCanvasCursor();
    }

    /// 現在選択されている注釈を削除する。
    private void DeleteSelectedStroke()
    {
        if (_selectedStroke == null)
        {
            return;
        }

        DeleteStroke(
            _selectedStroke);
    }

    /// 指定された注釈を削除し、Undo履歴へ登録する。
    private void DeleteStroke(
        StrokeModel stroke)
    {
        int strokeIndex =
            _strokes.IndexOf(stroke);

        if (strokeIndex < 0)
        {
            if (ReferenceEquals(
                    _selectedStroke,
                    stroke))
            {
                ClearStrokeSelection();
                RedrawStrokes();
            }

            return;
        }

        CommitCommentEditUndo();

        _strokes.RemoveAt(strokeIndex);

        PushUndoAction(
            new UndoAction
            {
                Type = UndoActionType.DeleteStroke,
                Stroke = stroke,
                StrokeIndex = strokeIndex
            });

        if (ReferenceEquals(
                _selectedStroke,
                stroke))
        {
            ClearStrokeSelection();
        }

        RedrawStrokes();
    }

    /// 注釈を選択し、選択枠と右パネルへ情報を反映する。
    private void SelectStroke(
        StrokeModel stroke)
    {
        if (!ReferenceEquals(
                _selectedStroke,
                stroke))
        {
            CommitCommentEditUndo();
        }

        _selectedStroke = stroke;
        ApplySelectedStrokeToRightPanel(stroke);
        RedrawStrokes();
    }

    /// 選択した注釈情報を描画モードと右パネルへ反映する。
    private void ApplySelectedStrokeToRightPanel(
        StrokeModel stroke)
    {
        // 選択した注釈自身の描画モードを表示へ反映する。
        // Button化したため、選択状態はコード側で更新する。
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

        _isUpdatingAnnotationPanel = true;

        try
        {
            SelectDiameterInRightPanel(stroke.Diameter);

            if (AnnotationCommentTextBox != null)
            {
                AnnotationCommentTextBox.Text = stroke.Comment;
            }
        }
        finally
        {
            _isUpdatingAnnotationPanel = false;
        }

        UpdateDrawingModeButtonVisuals();
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

    /// 口径変更を選択中の注釈へ即時反映し、強調表示を更新する。
    private void DiameterComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isUpdatingDiameterSelection)
        {
            return;
        }

        if (!_isUpdatingAnnotationPanel &&
            _selectedStroke != null &&
            _strokes.Contains(_selectedStroke))
        {
            string oldDiameter =
                _selectedStroke.Diameter;

            string newDiameter =
                GetSelectedDiameter();

            if (!string.Equals(
                    oldDiameter,
                    newDiameter,
                    StringComparison.Ordinal))
            {
                _selectedStroke.Diameter =
                    newDiameter;

                PushUndoAction(
                    new UndoAction
                    {
                        Type = UndoActionType.EditDiameter,
                        Stroke = _selectedStroke,
                        OldValue = oldDiameter,
                        NewValue = newDiameter
                    });
            }
        }

        // 元のストローク色は変更せず、画面上の表示色だけを更新する。
        RedrawStrokes();
    }

    /// コメント入力中の変更を受け取る。
    /// コメントはフォーカスが外れた時点、またはEnterキーで確定する。
    private void AnnotationCommentTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_isUpdatingAnnotationPanel)
        {
            return;
        }

        // 入力中はTextBoxだけを変更し、
        // StrokeModelへの反映とUndo登録は確定時に行う。
    }

    /// コメント編集開始時の値を保持する。
    private void AnnotationCommentTextBox_GotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (_isUpdatingAnnotationPanel ||
            _selectedStroke == null ||
            !_strokes.Contains(_selectedStroke))
        {
            _commentEditingStroke = null;
            _commentEditOriginalValue = string.Empty;
            return;
        }

        _commentEditingStroke =
            _selectedStroke;

        _commentEditOriginalValue =
            _selectedStroke.Comment;
    }

    /// コメント欄からフォーカスが外れた時点で、編集を1回のUndo履歴へ登録する。
    private void AnnotationCommentTextBox_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        CommitCommentEditUndo();
    }

    /// コメント欄でEnterキーが押されたときに編集を確定する。
    private void AnnotationCommentTextBox_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        bool isControlPressed =
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        // TextBox標準の文字列Undoより先に、
        // PDFMarkup全体の注釈Undoを実行する。
        if (isControlPressed && e.Key == Key.Z)
        {
            UndoLastAction();
            e.Handled = true;
            return;
        }

        // TextBox標準のRedoではなく、
        // PDFMarkup全体の注釈Redoを実行する。
        if (isControlPressed && e.Key == Key.Y)
        {
            CommitCommentEditUndo();
            RedoLastAction();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        CommitCommentEditUndo();

        // 入力欄からフォーカスを外し、編集確定状態にする。
        Keyboard.ClearFocus();

        e.Handled = true;
    }

    /// Enterキーでコメント編集を確定する。
    private void AnnotationCommentTextBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        CommitCommentEditUndo();

        // Enter確定後に入力フォーカスを外す。
        Keyboard.ClearFocus();

        e.Handled = true;
    }

    /// コメント変更を1回分のUndo履歴として確定する。
    private void CommitCommentEditUndo()
    {
        StrokeModel? stroke =
            _commentEditingStroke;

        _commentEditingStroke = null;

        if (stroke == null ||
            !_strokes.Contains(stroke))
        {
            _commentEditOriginalValue = string.Empty;
            return;
        }

        string newValue =
            AnnotationCommentTextBox.Text.Trim();

        // 編集開始時に固定した注釈だけへ反映する。
        stroke.Comment =
            newValue;

        if (!string.Equals(
                _commentEditOriginalValue,
                newValue,
                StringComparison.Ordinal))
        {
            PushUndoAction(
                new UndoAction
                {
                    Type = UndoActionType.EditComment,
                    Stroke = stroke,
                    OldValue = _commentEditOriginalValue,
                    NewValue = newValue
                });
        }

        _commentEditOriginalValue = string.Empty;

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
        UndoLastAction();
    }

    /// やり直す。
    private void RedoMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        RedoLastAction();
    }

    /// 新しい操作をUndo履歴へ登録し、Redo履歴を破棄する。
    private void PushUndoAction(
        UndoAction action)
    {
        _undoActions.Push(action);
        _redoActions.Clear();
    }

    /// 最後の操作を元に戻す。
    private void UndoLastAction()
    {
        CommitCommentEditUndo();

        if (_undoActions.Count == 0)
        {
            return;
        }

        UndoAction action =
            _undoActions.Pop();

        ApplyUndoAction(
            action,
            isUndo: true);

        _redoActions.Push(action);
        RefreshAfterUndoRedo(action.Stroke);
    }

    /// 元に戻した操作をやり直す。
    private void RedoLastAction()
    {
        if (_redoActions.Count == 0)
        {
            return;
        }

        UndoAction action =
            _redoActions.Pop();

        ApplyUndoAction(
            action,
            isUndo: false);

        _undoActions.Push(action);
        RefreshAfterUndoRedo(action.Stroke);
    }

    /// 操作内容をUndoまたはRedoとして反映する。
    private void ApplyUndoAction(
        UndoAction action,
        bool isUndo)
    {
        switch (action.Type)
        {
            case UndoActionType.AddStroke:
                if (isUndo)
                {
                    _strokes.Remove(action.Stroke);

                    if (ReferenceEquals(
                            _selectedStroke,
                            action.Stroke))
                    {
                        ClearStrokeSelection();
                    }
                }
                else if (!_strokes.Contains(action.Stroke))
                {
                    int insertIndex =
                        Math.Clamp(
                            action.StrokeIndex,
                            0,
                            _strokes.Count);

                    _strokes.Insert(
                        insertIndex,
                        action.Stroke);
                }

                break;

            case UndoActionType.DeleteStroke:
                if (isUndo)
                {
                    if (!_strokes.Contains(action.Stroke))
                    {
                        int insertIndex =
                            Math.Clamp(
                                action.StrokeIndex,
                                0,
                                _strokes.Count);

                        _strokes.Insert(
                            insertIndex,
                            action.Stroke);
                    }
                }
                else
                {
                    _strokes.Remove(action.Stroke);

                    if (ReferenceEquals(
                            _selectedStroke,
                            action.Stroke))
                    {
                        ClearStrokeSelection();
                    }
                }

                break;

            case UndoActionType.EditDiameter:
                action.Stroke.Diameter =
                    isUndo
                        ? action.OldValue
                        : action.NewValue;
                break;

            case UndoActionType.EditComment:
                action.Stroke.Comment =
                    isUndo
                        ? action.OldValue
                        : action.NewValue;
                break;

            case UndoActionType.EditColor:
                if (Enum.TryParse(
                        isUndo
                            ? action.OldValue
                            : action.NewValue,
                        true,
                        out StrokeColor color))
                {
                    action.Stroke.Color = color;
                }

                break;

            case UndoActionType.EditThickness:
                if (double.TryParse(
                        isUndo
                            ? action.OldValue
                            : action.NewValue,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double thickness))
                {
                    action.Stroke.Thickness = thickness;
                    action.Stroke.RecalculateSelectionBounds();
                }

                break;

            case UndoActionType.EditOpacity:
                if (byte.TryParse(
                        isUndo
                            ? action.OldValue
                            : action.NewValue,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out byte opacity))
                {
                    action.Stroke.Opacity = opacity;
                }

                break;
        }
    }

    /// Undo / Redo後に選択表示と右パネルを更新する。
    private void RefreshAfterUndoRedo(
        StrokeModel stroke)
    {
        if (_strokes.Contains(stroke))
        {
            _selectedStroke = stroke;
            ApplySelectedStrokeToRightPanel(stroke);
        }

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

            _pageUndoActions[pageIndex] =
                new Stack<UndoAction>();

            _pageRedoActions[pageIndex] =
                new Stack<UndoAction>();

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
        _pageUndoActions[_currentPageIndex] =
            new Stack<UndoAction>(
                _undoActions.Reverse());

        _pageRedoActions[_currentPageIndex] =
            new Stack<UndoAction>(
                _redoActions.Reverse());
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

        _undoActions.Clear();

        if (_pageUndoActions.TryGetValue(
                _currentPageIndex,
                out Stack<UndoAction>? pageUndoActions))
        {
            foreach (UndoAction action in pageUndoActions.Reverse())
            {
                _undoActions.Push(action);
            }
        }

        _redoActions.Clear();

        if (_pageRedoActions.TryGetValue(
                _currentPageIndex,
                out Stack<UndoAction>? pageRedoActions))
        {
            foreach (UndoAction action in pageRedoActions.Reverse())
            {
                _redoActions.Push(action);
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

        _undoActions.Clear();
        _redoActions.Clear();
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
            ClearStrokeSelection();
        }
    }

    /// 目ボタンの表示と説明を現在の表示状態へ合わせる。
    private void UpdateVisibilityButtons()
    {
        if (MarkupVisibilityButton != null)
        {
            MarkupVisibilityButton.Content =
                CreateIconContent(
                    _isMarkupVisible
                        ? "visibility-on"
                        : "visibility-off",
                    _isMarkupVisible
                        ? "👁"
                        : "⊘");

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
                CreateIconContent(
                    _isCheckVisible
                        ? "visibility-on"
                        : "visibility-off",
                    _isCheckVisible
                        ? "👁"
                        : "⊘");

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

    /// 朱書きボタンで、必ず朱書き描画へ切り替える。
    private void MarkupModeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ActivateDrawingMode(
            DrawingMode.Markup);
    }

    /// チェックボタンで、必ずチェック描画へ切り替える。
    private void CheckModeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ActivateDrawingMode(
            DrawingMode.Check);
    }

    /// 指定された描画モードへ切り替え、一時ツールと注釈選択を解除する。
    private void ActivateDrawingMode(
        DrawingMode drawingMode)
    {
        CommitCommentEditUndo();

        if (_selectedStroke != null)
        {
            ClearStrokeSelection();
        }

        SaveCurrentModeSettings();

        _currentDrawingMode =
            drawingMode;

        if (drawingMode == DrawingMode.Markup)
        {
            _currentStrokeColor =
                _lastMarkupColor;

            _currentStrokeThickness =
                _lastMarkupThickness;

            _currentStrokeOpacity =
                _lastMarkupOpacity;

            // 朱書きでは口径を基本的に使用しないため未設定へ戻す。
            SelectDiameterInRightPanel(
                "未設定");
        }
        else
        {
            _currentStrokeColor =
                _lastCheckColor;

            _currentStrokeThickness =
                _lastCheckThickness;

            _currentStrokeOpacity =
                _lastCheckOpacity;
        }

        SetToolMode(
            ToolMode.Drawing);

        ApplyDrawingModeAppearance();
        UpdateDrawingModeButtonVisuals();
        UpdateDrawingSettingsUi();
        RedrawStrokes();
    }

    /// 現在の描画モードに応じて、画面の背景色と表示文字を更新する。
    private void ApplyDrawingModeAppearance()
    {
        bool isMarkup =
            _currentDrawingMode == DrawingMode.Markup;

        var modeBrush =
            new SolidColorBrush(
                isMarkup
                    ? Color.FromRgb(252, 232, 232)
                    : Color.FromRgb(255, 246, 204));

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
                isMarkup
                    ? "朱書き"
                    : "チェック";
        }
    }

    /// 朱書き・チェックボタンの選択表示を現在の描画モードへ合わせる。
    private void UpdateDrawingModeButtonVisuals()
    {
        if (MarkupModeButton == null ||
            CheckModeButton == null)
        {
            return;
        }

        bool isMarkup =
            _currentDrawingMode == DrawingMode.Markup;

        ApplyDrawingModeButtonVisual(
            MarkupModeButton,
            isMarkup);

        ApplyDrawingModeButtonVisual(
            CheckModeButton,
            !isMarkup);
    }

    /// 描画モードボタンへ選択中／未選択の見た目を反映する。
    private static void ApplyDrawingModeButtonVisual(
        Button button,
        bool isSelected)
    {
        button.Opacity =
            isSelected
                ? 1.0
                : 0.72;

        button.FontWeight =
            isSelected
                ? FontWeights.Bold
                : FontWeights.Normal;

        button.BorderThickness =
            isSelected
                ? new Thickness(2)
                : new Thickness(1);

        button.BorderBrush =
            isSelected
                ? Brushes.Black
                : Brushes.Gray;
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
    /// 注釈を選択中の場合は、その注釈の色変更としてUndo履歴へ登録する。
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

        // 選択中の注釈がある場合は、その注釈の色だけを変更する。
        // 透明度は別の編集項目として扱うため、ここでは変更しない。
        if (_selectedStroke != null &&
            _strokes.Contains(_selectedStroke))
        {
            StrokeColor oldColor =
                _selectedStroke.Color;

            if (oldColor != color)
            {
                _selectedStroke.Color = color;
                _currentStrokeColor = color;

                PushUndoAction(
                    new UndoAction
                    {
                        Type = UndoActionType.EditColor,
                        Stroke = _selectedStroke,
                        OldValue = oldColor.ToString(),
                        NewValue = color.ToString()
                    });
            }

            SaveCurrentModeSettings();
            UpdateDrawingSettingsUi();
            RedrawStrokes();
            return;
        }

        // 注釈を選択していない場合は、次に描く線の設定を変更する。
        _currentStrokeColor = color;

        // 新規描画用の色を選んだときは、モードごとの標準透明度へ戻す。
        _currentStrokeOpacity =
            _currentDrawingMode == DrawingMode.Markup
                ? (byte)255
                : (byte)96;

        SaveCurrentModeSettings();
        UpdateDrawingSettingsUi();
    }

    /// 太さスライダーのドラッグ開始時に、編集対象と変更前の値を固定する。
    private void StrokeThicknessSlider_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_isUpdatingDrawingSettingsUi ||
            _selectedStroke == null ||
            !_strokes.Contains(_selectedStroke))
        {
            _thicknessEditingStroke = null;
            return;
        }

        _thicknessEditingStroke = _selectedStroke;
        _thicknessEditOriginalValue = _selectedStroke.Thickness;
    }

    /// 太さスライダーのドラッグ終了時に、変更を1回のUndo履歴へ登録する。
    private void StrokeThicknessSlider_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        CommitThicknessEditUndo();
    }

    /// マウスキャプチャが解除された場合も、太さ変更を確定する。
    private void StrokeThicknessSlider_LostMouseCapture(
        object sender,
        MouseEventArgs e)
    {
        CommitThicknessEditUndo();
    }

    /// 太さスライダーの値を現在の描画太さ、または選択中注釈へ反映する。
    private void StrokeThicknessSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingDrawingSettingsUi)
        {
            return;
        }

        double thickness =
            Math.Clamp(
                e.NewValue,
                0.1,
                100.0);

        _currentStrokeThickness = thickness;

        if (_selectedStroke != null &&
            _strokes.Contains(_selectedStroke))
        {
            // ドラッグ中は見た目へ即時反映し、履歴はドラッグ終了時に1件だけ登録する。
            _selectedStroke.Thickness = thickness;
            _selectedStroke.RecalculateSelectionBounds();
            RedrawStrokes();
        }

        SaveCurrentModeSettings();

        if (StrokeThicknessText != null)
        {
            StrokeThicknessText.Text =
                thickness.ToString("0.0");
        }

        UpdateStrokeSettingPreviews();
    }

    /// 太さ変更を1回分のUndo履歴として確定する。
    private void CommitThicknessEditUndo()
    {
        StrokeModel? stroke = _thicknessEditingStroke;
        _thicknessEditingStroke = null;

        if (stroke == null ||
            !_strokes.Contains(stroke))
        {
            return;
        }

        double newValue = stroke.Thickness;

        if (Math.Abs(
                _thicknessEditOriginalValue - newValue) < 0.0001)
        {
            return;
        }

        PushUndoAction(
            new UndoAction
            {
                Type = UndoActionType.EditThickness,
                Stroke = stroke,
                OldValue = _thicknessEditOriginalValue.ToString(
                    CultureInfo.InvariantCulture),
                NewValue = newValue.ToString(
                    CultureInfo.InvariantCulture)
            });
    }

    /// 透明度スライダーのドラッグ開始時に、編集対象と変更前の値を固定する。
    private void StrokeOpacitySlider_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_isUpdatingDrawingSettingsUi ||
            _selectedStroke == null ||
            !_strokes.Contains(_selectedStroke))
        {
            _opacityEditingStroke = null;
            return;
        }

        _opacityEditingStroke = _selectedStroke;
        _opacityEditOriginalValue = _selectedStroke.Opacity;
    }

    /// 透明度スライダーのドラッグ終了時に、変更を1回のUndo履歴へ登録する。
    private void StrokeOpacitySlider_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        CommitOpacityEditUndo();
    }

    /// マウスキャプチャが解除された場合も、透明度変更を確定する。
    private void StrokeOpacitySlider_LostMouseCapture(
        object sender,
        MouseEventArgs e)
    {
        CommitOpacityEditUndo();
    }

    /// 透明度スライダーの値を現在の不透明度、または選択中注釈へ反映する。
    private void StrokeOpacitySlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingDrawingSettingsUi)
        {
            return;
        }

        double percent =
            Math.Clamp(
                e.NewValue,
                0,
                100);

        byte opacity =
            (byte)Math.Round(
                percent / 100.0 * 255.0);

        _currentStrokeOpacity = opacity;

        if (_selectedStroke != null &&
            _strokes.Contains(_selectedStroke))
        {
            // ドラッグ中は見た目へ即時反映し、履歴はドラッグ終了時に1件だけ登録する。
            _selectedStroke.Opacity = opacity;
            RedrawStrokes();
        }

        SaveCurrentModeSettings();

        if (StrokeOpacityText != null)
        {
            StrokeOpacityText.Text =
                $"{percent:0}%";
        }

        UpdateStrokeSettingPreviews();
    }

    /// 透明度変更を1回分のUndo履歴として確定する。
    private void CommitOpacityEditUndo()
    {
        StrokeModel? stroke = _opacityEditingStroke;
        _opacityEditingStroke = null;

        if (stroke == null ||
            !_strokes.Contains(stroke))
        {
            return;
        }

        byte newValue = stroke.Opacity;

        if (_opacityEditOriginalValue == newValue)
        {
            return;
        }

        PushUndoAction(
            new UndoAction
            {
                Type = UndoActionType.EditOpacity,
                Stroke = stroke,
                OldValue = _opacityEditOriginalValue.ToString(
                    CultureInfo.InvariantCulture),
                NewValue = newValue.ToString(
                    CultureInfo.InvariantCulture)
            });
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
        _isUpdatingDrawingSettingsUi = true;

        try
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
        finally
        {
            _isUpdatingDrawingSettingsUi = false;
        }
    }

    /// PDFの全ページ分のサムネイル項目を先に作成する。
    private void PreparePageThumbnailItems()
    {
        _pageThumbnails.Clear();

        for (int pageIndex = 0;
            pageIndex < _pageCount;
            pageIndex++)
        {
            _pageThumbnails.Add(
                new PageThumbnailItem
                {
                    PageIndex = pageIndex
                });
        }

        PageListEmptyText.Visibility =
            _pageThumbnails.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    /// 全ページのサムネイルをバックグラウンドで順番に生成する。
    private void StartThumbnailGeneration()
    {
        if (string.IsNullOrWhiteSpace(_currentPdfPath) ||
            _pageThumbnails.Count == 0)
        {
            return;
        }

        _thumbnailCancellation =
            new CancellationTokenSource();

        string pdfPath =
            _currentPdfPath;

        CancellationToken cancellationToken =
            _thumbnailCancellation.Token;

        _ = GenerateThumbnailsAsync(
            pdfPath,
            cancellationToken);
    }

    /// サムネイル画像を順番に生成してページ一覧へ反映する。
    private async Task GenerateThumbnailsAsync(
        string pdfPath,
        CancellationToken cancellationToken)
    {
        try
        {
            for (int pageIndex = 0;
                pageIndex < _pageThumbnails.Count;
                pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int targetPageIndex =
                    pageIndex;

                BitmapImage thumbnail =
                    await Task.Run(
                        () => _pdfService.RenderThumbnail(
                            pdfPath,
                            targetPageIndex,
                            160),
                        cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (!string.Equals(
                        _currentPdfPath,
                        pdfPath,
                        StringComparison.Ordinal))
                {
                    return;
                }

                _pageThumbnails[targetPageIndex].Thumbnail =
                    thumbnail;

                // 現在ページを優先表示しつつ、UIへ描画時間を返す。
                await Dispatcher.Yield(
                    DispatcherPriority.Background);
            }
        }
        catch (OperationCanceledException)
        {
            // PDFを開き直した場合は、古い生成処理を静かに終了する。
        }
        catch (Exception ex)
        {
            StatusText.Text =
                $"サムネイル生成エラー: {ex.Message}";
        }
    }

    /// 実行中のサムネイル生成を停止する。
    private void CancelThumbnailGeneration()
    {
        if (_thumbnailCancellation == null)
        {
            return;
        }

        _thumbnailCancellation.Cancel();
        _thumbnailCancellation.Dispose();
        _thumbnailCancellation = null;
    }

    /// 現在ページとページ一覧の選択位置を同期する。
    private void SyncPageListSelection()
    {
        if (_currentPageIndex < 0 ||
            _currentPageIndex >= _pageThumbnails.Count)
        {
            return;
        }

        _isUpdatingPageListSelection = true;

        try
        {
            PageThumbnailItem currentItem =
                _pageThumbnails[_currentPageIndex];

            PageListBox.SelectedItem =
                currentItem;

            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                    PageListBox.ScrollIntoView(
                        currentItem)));
        }
        finally
        {
            _isUpdatingPageListSelection = false;
        }
    }

    /// ページ一覧で選択したサムネイルのページへ移動する。
    private void PageListBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isUpdatingPageListSelection ||
            PageListBox.SelectedItem is not PageThumbnailItem selectedItem ||
            selectedItem.PageIndex == _currentPageIndex)
        {
            return;
        }

        ChangePage(
            selectedItem.PageIndex);
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
                CreateIconContent(
                    "panel-left-open",
                    "▶");

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
                CreateIconContent(
                    "panel-left-close",
                    "◀");

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
                CreateIconContent(
                    "panel-right-open",
                    "◀");

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
                CreateIconContent(
                    "panel-right-close",
                    "▶");

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
