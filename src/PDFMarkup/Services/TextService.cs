using PDFMarkup.Models;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PDFMarkup.Services;

/// <summary>
/// PDF上のテキスト入力・表示を管理するサービス。
/// </summary>
public sealed class TextService
{
    /// <summary>
    /// 文字注釈がEnterで確定されたときに通知する。
    /// 引数はページ番号、注釈、ページ内の追加位置。
    /// </summary>
    public event Action<int, TextAnnotationModel, int>? AnnotationCommitted;

    // ページごとの文字注釈。
    private readonly Dictionary<int, List<TextAnnotationModel>>
        _pageAnnotations = new();

    // 現在Canvas上に表示している一時文字入力欄。
    private TextBox? _activeTextInput;

    // 一時入力中のCanvasとページ情報。
    private Canvas? _activeCanvas;
    private int _activePageIndex;
    private Point _activePdfPosition;

    // 一時入力開始時のCanvas表示倍率。
    private double _activeCanvasScale = 1.0;

    // 現在選択されている文字注釈とページ。
    private TextAnnotationModel? _selectedAnnotation;
    private int _selectedPageIndex = -1;
    private readonly HashSet<TextAnnotationModel> _selectedAnnotations = new();

    // コメント編集開始時の値。
    private TextAnnotationModel? _commentEditingAnnotation;
    private string _commentEditOriginalValue = string.Empty;

    // 透明度編集開始時の値。
    private TextAnnotationModel? _opacityEditingAnnotation;
    private byte _opacityEditOriginalValue;

    // 文字サイズ編集開始時の値。
    private TextAnnotationModel? _fontSizeEditingAnnotation;
    private double _fontSizeEditOriginalValue;

    // 一時入力開始時の文字設定。
    private StrokeColor _activeColor;
    private byte _activeOpacity;
    private double _activeFontSize;
    private DrawingMode _activeMode;
    private string _activeDiameter = "未設定";
    private string _activeComment = string.Empty;

    /// <summary>
    /// 指定位置へ一時文字入力欄を表示する。
    /// Enterで確定、Escでキャンセルする。
    /// </summary>
    public void BeginTextInput(
        Canvas drawingCanvas,
        int pageIndex,
        Point canvasPoint,
        Point pdfPosition,
        StrokeColor color,
        byte opacity,
        DrawingMode mode,
        string diameter,
        string comment,
        Brush foreground,
        double canvasScale,
        double fontSizePdf = 16.0)
    {
        CancelTextInput(
            drawingCanvas);

        _activeCanvas =
            drawingCanvas;

        _activePageIndex =
            pageIndex;

        _activePdfPosition =
            pdfPosition;

        _activeCanvasScale =
            Math.Max(
                0.0001,
                canvasScale);

        _activeColor =
            color;

        _activeOpacity =
            opacity;

        _activeFontSize =
            fontSizePdf;

        _activeMode =
            mode;

        _activeDiameter =
            diameter;

        _activeComment =
            comment;

        _activeTextInput =
            new TextBox
            {
                MinWidth = 90,
                MinHeight = 26,
                Padding = new Thickness(4, 2, 4, 2),
                FontSize =
                    Math.Max(
                        1.0,
                        fontSizePdf * _activeCanvasScale),
                Foreground = foreground,
                Background = Brushes.White,
                BorderBrush = Brushes.DodgerBlue,
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center,
                AcceptsReturn = false
            };

        _activeTextInput.PreviewKeyDown +=
            ActiveTextInput_PreviewKeyDown;

        Canvas.SetLeft(
            _activeTextInput,
            canvasPoint.X);

        Canvas.SetTop(
            _activeTextInput,
            canvasPoint.Y);

        drawingCanvas.Children.Add(
            _activeTextInput);

        _activeTextInput.Focus();
        Keyboard.Focus(
            _activeTextInput);
    }

    /// <summary>
    /// 現在表示中の一時文字入力欄を破棄する。
    /// </summary>
    public void CancelTextInput(
        Canvas drawingCanvas)
    {
        if (_activeTextInput == null)
        {
            return;
        }

        _activeTextInput.PreviewKeyDown -=
            ActiveTextInput_PreviewKeyDown;

        drawingCanvas.Children.Remove(
            _activeTextInput);

        _activeTextInput = null;
        _activeCanvas = null;
    }

    /// <summary>
    /// 現在選択されている文字注釈を取得する。
    /// </summary>
    public TextAnnotationModel? SelectedAnnotation =>
        _selectedAnnotation;

    /// <summary>
    /// 現在選択されているすべての文字注釈を取得する。
    /// </summary>
    public IReadOnlyCollection<TextAnnotationModel> SelectedAnnotations =>
        _selectedAnnotations;

    /// <summary>
    /// 現在選択中の文字注釈が属するページ番号を取得する。
    /// </summary>
    public int SelectedPageIndex =>
        _selectedPageIndex;

    /// <summary>
    /// 指定された文字注釈を選択状態にする。
    /// </summary>
    public void SelectAnnotation(
        int pageIndex,
        TextAnnotationModel annotation)
    {
        _selectedAnnotation =
            annotation;

        _selectedAnnotations.Clear();
        _selectedAnnotations.Add(annotation);

        _selectedPageIndex =
            pageIndex;
    }

    /// <summary>
    /// 現在の文字注釈選択を解除する。
    /// </summary>
    public void ClearSelection()
    {
        _selectedAnnotation =
            null;

        _selectedAnnotations.Clear();

        _selectedPageIndex =
            -1;

        _commentEditingAnnotation =
            null;

        _commentEditOriginalValue =
            string.Empty;

        _opacityEditingAnnotation =
            null;

        _fontSizeEditingAnnotation =
            null;
    }

    public bool IsSelected(TextAnnotationModel annotation) =>
        _selectedAnnotations.Contains(annotation);

    public void ToggleSelection(int pageIndex, TextAnnotationModel annotation)
    {
        _selectedPageIndex = pageIndex;
        if (!_selectedAnnotations.Add(annotation))
        {
            _selectedAnnotations.Remove(annotation);
        }

        _selectedAnnotation = _selectedAnnotations.Count == 1
            ? _selectedAnnotations.First()
            : null;
    }

    public void SetSelection(int pageIndex, IEnumerable<TextAnnotationModel> annotations)
    {
        _selectedAnnotations.Clear();
        foreach (TextAnnotationModel annotation in annotations)
        {
            _selectedAnnotations.Add(annotation);
        }

        _selectedPageIndex = _selectedAnnotations.Count > 0 ? pageIndex : -1;
        _selectedAnnotation = _selectedAnnotations.Count == 1
            ? _selectedAnnotations.First()
            : null;
    }

    public static Rect GetAnnotationBounds(TextAnnotationModel annotation)
    {
        double width = Math.Max(annotation.FontSize, annotation.Text.Length * annotation.FontSize * 0.65);
        double height = Math.Max(annotation.FontSize, annotation.FontSize * 1.4);
        return new Rect(annotation.PdfPosition.X, annotation.PdfPosition.Y - height, width, height);
    }

    /// <summary>
    /// 指定PDF座標の文字注釈を検索し、見つかった場合はその注釈を選択する。
    /// </summary>
    public TextAnnotationModel? SelectAnnotationAtPdfPoint(
        int pageIndex,
        Point pdfPoint,
        Func<TextAnnotationModel, bool>? isVisible = null)
    {
        TextAnnotationModel? annotation =
            FindAnnotationAtPdfPoint(
                pageIndex,
                pdfPoint,
                isVisible);

        if (annotation == null)
        {
            return null;
        }

        SelectAnnotation(
            pageIndex,
            annotation);

        return annotation;
    }

    /// <summary>
    /// 選択中の文字注釈の色を変更し、変更前の値を返す。
    /// </summary>
    public bool TryChangeSelectedColor(
        StrokeColor newColor,
        out StrokeColor oldColor)
    {
        oldColor =
            default;

        if (_selectedAnnotation == null)
        {
            return false;
        }

        oldColor =
            _selectedAnnotation.Color;

        if (oldColor == newColor)
        {
            return false;
        }

        _selectedAnnotation.Color =
            newColor;

        return true;
    }

    /// <summary>
    /// 選択中の文字注釈の口径を変更し、変更前の値を返す。
    /// </summary>
    public bool TryChangeSelectedDiameter(
        string newDiameter,
        out string oldDiameter)
    {
        oldDiameter =
            string.Empty;

        if (_selectedAnnotation == null)
        {
            return false;
        }

        oldDiameter =
            _selectedAnnotation.Diameter;

        if (string.Equals(
                oldDiameter,
                newDiameter,
                StringComparison.Ordinal))
        {
            return false;
        }

        _selectedAnnotation.Diameter =
            newDiameter;

        return true;
    }

    /// <summary>
    /// 選択中の文字注釈のコメント編集を開始する。
    /// </summary>
    public bool BeginSelectedCommentEdit()
    {
        if (_selectedAnnotation == null)
        {
            _commentEditingAnnotation =
                null;

            _commentEditOriginalValue =
                string.Empty;

            return false;
        }

        _commentEditingAnnotation =
            _selectedAnnotation;

        _commentEditOriginalValue =
            _selectedAnnotation.Comment;

        return true;
    }

    /// <summary>
    /// 文字注釈のコメント編集を確定する。
    /// 変更があった場合は対象注釈・変更前後の値を返す。
    /// </summary>
    public bool TryCommitCommentEdit(
        string newValue,
        out TextAnnotationModel? annotation,
        out string oldValue)
    {
        annotation =
            _commentEditingAnnotation;

        oldValue =
            _commentEditOriginalValue;

        _commentEditingAnnotation =
            null;

        _commentEditOriginalValue =
            string.Empty;

        if (annotation == null)
        {
            return false;
        }

        annotation.Comment =
            newValue;

        return !string.Equals(
            oldValue,
            newValue,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// 選択中の文字注釈の透明度編集を開始する。
    /// </summary>
    public bool BeginSelectedOpacityEdit()
    {
        if (_selectedAnnotation == null)
        {
            _opacityEditingAnnotation =
                null;

            return false;
        }

        _opacityEditingAnnotation =
            _selectedAnnotation;

        _opacityEditOriginalValue =
            _selectedAnnotation.Opacity;

        return true;
    }

    /// <summary>
    /// 選択中の文字注釈へ透明度を即時反映する。
    /// </summary>
    public bool SetSelectedOpacity(
        byte opacity)
    {
        if (_selectedAnnotation == null)
        {
            return false;
        }

        _selectedAnnotation.Opacity =
            opacity;

        return true;
    }

    /// <summary>
    /// 文字注釈の透明度編集を確定する。
    /// 変更があった場合は対象注釈・変更前後の値を返す。
    /// </summary>
    public bool TryCommitOpacityEdit(
        out TextAnnotationModel? annotation,
        out byte oldValue,
        out byte newValue)
    {
        annotation =
            _opacityEditingAnnotation;

        oldValue =
            _opacityEditOriginalValue;

        newValue =
            annotation?.Opacity ?? oldValue;

        _opacityEditingAnnotation =
            null;

        return annotation != null &&
               oldValue != newValue;
    }

    /// <summary>
    /// 選択中の文字注釈の文字サイズ編集を開始する。
    /// </summary>
    public bool BeginSelectedFontSizeEdit()
    {
        if (_selectedAnnotation == null)
        {
            _fontSizeEditingAnnotation =
                null;

            return false;
        }

        _fontSizeEditingAnnotation =
            _selectedAnnotation;

        _fontSizeEditOriginalValue =
            _selectedAnnotation.FontSize;

        return true;
    }

    /// <summary>
    /// 文字サイズ編集を確定し、変更前後の値を返す。
    /// </summary>
    public bool TryCommitFontSizeEdit(
        double newValue,
        out TextAnnotationModel? annotation,
        out double oldValue)
    {
        annotation =
            _fontSizeEditingAnnotation;

        oldValue =
            _fontSizeEditOriginalValue;

        _fontSizeEditingAnnotation =
            null;

        if (annotation == null)
        {
            return false;
        }

        annotation.FontSize =
            newValue;

        return Math.Abs(
            oldValue - newValue) >= 0.0001;
    }

    /// <summary>
    /// 文字サイズ編集中か確認する。
    /// </summary>
    public bool IsFontSizeEditing =>
        _fontSizeEditingAnnotation != null;

    /// <summary>
    /// コメント編集中か確認する。
    /// </summary>
    public bool IsCommentEditing =>
        _commentEditingAnnotation != null;

    /// <summary>
    /// 指定ページの文字注釈から、PDF座標上でクリックされた文字を後から追加した順に検索する。
    /// </summary>
    public TextAnnotationModel? FindAnnotationAtPdfPoint(
        int pageIndex,
        Point pdfPoint,
        Func<TextAnnotationModel, bool>? isVisible = null)
    {
        if (!_pageAnnotations.TryGetValue(
                pageIndex,
                out List<TextAnnotationModel>? annotations))
        {
            return null;
        }

        for (int index = annotations.Count - 1;
            index >= 0;
            index--)
        {
            TextAnnotationModel annotation =
                annotations[index];

            // 非表示中のモードに属する文字注釈は、
            // 選択・消しゴムの当たり判定対象から除外する。
            if (isVisible != null &&
                !isVisible(annotation))
            {
                continue;
            }

            double estimatedWidth =
                Math.Max(
                    annotation.FontSize,
                    annotation.Text.Length *
                    annotation.FontSize *
                    0.65);

            double estimatedHeight =
                Math.Max(
                    annotation.FontSize,
                    annotation.FontSize * 1.4);

            // PdfPositionはCanvas上の左上位置をPDF座標へ変換した値。
            // PDFはY軸上向きなので、文字領域は位置から下方向へ広がる。
            double left =
                annotation.PdfPosition.X;

            double right =
                left + estimatedWidth;

            double top =
                annotation.PdfPosition.Y;

            double bottom =
                top - estimatedHeight;

            if (pdfPoint.X >= left &&
                pdfPoint.X <= right &&
                pdfPoint.Y >= bottom &&
                pdfPoint.Y <= top)
            {
                return annotation;
            }
        }

        return null;
    }

    /// <summary>
    /// 指定ページの確定済み文字注釈をCanvasへ再描画する。
    /// </summary>
    public void RedrawAnnotations(
        Canvas drawingCanvas,
        int pageIndex,
        Func<Point, Point> pdfToCanvas,
        double canvasScale,
        Func<TextAnnotationModel, bool>? isVisible = null)
    {
        if (!_pageAnnotations.TryGetValue(
                pageIndex,
                out List<TextAnnotationModel>? annotations))
        {
            return;
        }

        foreach (TextAnnotationModel annotation in annotations)
        {
            // 朱書き／チェックの表示状態に合わせて文字注釈も非表示にする。
            if (isVisible != null &&
                !isVisible(annotation))
            {
                continue;
            }

            Point canvasPoint =
                pdfToCanvas(
                    annotation.PdfPosition);

            var textBlock =
                new TextBlock
                {
                    Text = annotation.Text,
                    FontSize =
                        Math.Max(
                            1.0,
                            annotation.FontSize * canvasScale),
                    Foreground =
                        CreateBrush(
                            annotation.Color,
                            annotation.Opacity),
                    Background = Brushes.Transparent,
                    IsHitTestVisible = false
                };

            Canvas.SetLeft(
                textBlock,
                canvasPoint.X);

            Canvas.SetTop(
                textBlock,
                canvasPoint.Y);

            drawingCanvas.Children.Add(
                textBlock);

            if (pageIndex == _selectedPageIndex &&
                _selectedAnnotations.Contains(annotation))
            {
                textBlock.Measure(
                    new Size(
                        double.PositiveInfinity,
                        double.PositiveInfinity));

                Size desiredSize =
                    textBlock.DesiredSize;

                var selectionBorder =
                    new Border
                    {
                        Width =
                            Math.Max(
                                8.0,
                                desiredSize.Width + 8.0),
                        Height =
                            Math.Max(
                                8.0,
                                desiredSize.Height + 6.0),
                        BorderBrush =
                            Brushes.DodgerBlue,
                        BorderThickness =
                            new Thickness(1.5),
                        Background =
                            Brushes.Transparent,
                        IsHitTestVisible = false
                    };

                Canvas.SetLeft(
                    selectionBorder,
                    canvasPoint.X - 4.0);

                Canvas.SetTop(
                    selectionBorder,
                    canvasPoint.Y - 3.0);

                drawingCanvas.Children.Add(
                    selectionBorder);
            }
        }
    }

    /// <summary>
    /// 指定ページから文字注釈を削除する。
    /// Undo時に使用する。
    /// </summary>
    public bool RemoveAnnotation(
        int pageIndex,
        TextAnnotationModel annotation)
    {
        if (!_pageAnnotations.TryGetValue(
                pageIndex,
                out List<TextAnnotationModel>? annotations))
        {
            return false;
        }

        bool removed =
            annotations.Remove(
                annotation);

        if (removed &&
            ReferenceEquals(
                _selectedAnnotation,
                annotation))
        {
            ClearSelection();
        }

        return removed;
    }

    /// <summary>
    /// 指定ページの指定位置へ文字注釈を挿入する。
    /// Redo時に使用する。
    /// </summary>
    public void InsertAnnotation(
        int pageIndex,
        int annotationIndex,
        TextAnnotationModel annotation)
    {
        if (!_pageAnnotations.TryGetValue(
                pageIndex,
                out List<TextAnnotationModel>? annotations))
        {
            annotations =
                new List<TextAnnotationModel>();

            _pageAnnotations[pageIndex] =
                annotations;
        }

        int insertIndex =
            Math.Clamp(
                annotationIndex,
                0,
                annotations.Count);

        if (!annotations.Contains(annotation))
        {
            annotations.Insert(
                insertIndex,
                annotation);
        }
    }

    /// <summary>
    /// 指定ページで保持している文字注釈を取得する。
    /// PDF保存時に使用する。
    /// </summary>
    public IReadOnlyList<TextAnnotationModel> GetPageAnnotations(
        int pageIndex)
    {
        if (_pageAnnotations.TryGetValue(
                pageIndex,
                out List<TextAnnotationModel>? annotations))
        {
            return annotations;
        }

        return Array.Empty<TextAnnotationModel>();
    }

    /// <summary>
    /// PDFから読み込んだ文字注釈で、指定ページの保持内容を置き換える。
    /// 初回ページ読込時に使用する。
    /// </summary>
    public void SetPageAnnotations(
        int pageIndex,
        IEnumerable<TextAnnotationModel> annotations)
    {
        _pageAnnotations[pageIndex] =
            new List<TextAnnotationModel>(
                annotations);

        if (_selectedPageIndex == pageIndex)
        {
            ClearSelection();
        }
    }

    /// <summary>
    /// PDFを開き直すときなど、保持している文字注釈をすべて破棄する。
    /// </summary>
    public void ClearAll(
        Canvas drawingCanvas)
    {
        CancelTextInput(
            drawingCanvas);

        _pageAnnotations.Clear();
        ClearSelection();
    }

    /// <summary>
    /// 現在、一時文字入力欄が表示されているか取得する。
    /// </summary>
    public bool IsTextInputActive =>
        _activeTextInput != null;

    /// <summary>
    /// EnterまたはEscによる一時文字入力の確定・キャンセルを処理する。
    /// </summary>
    private void ActiveTextInput_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (_activeTextInput == null ||
            _activeCanvas == null)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            Canvas canvas =
                _activeCanvas;

            CancelTextInput(
                canvas);

            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        string text =
            _activeTextInput.Text.Trim();

        Canvas activeCanvas =
            _activeCanvas;

        if (string.IsNullOrWhiteSpace(text))
        {
            CancelTextInput(
                activeCanvas);

            e.Handled = true;
            return;
        }

        var annotation =
            new TextAnnotationModel
            {
                Text = text,
                PdfPosition = _activePdfPosition,
                Color = _activeColor,
                Opacity = _activeOpacity,
                FontSize = _activeFontSize,
                Mode = _activeMode,
                Diameter = _activeDiameter,
                Comment = _activeComment
            };

        if (!_pageAnnotations.TryGetValue(
                _activePageIndex,
                out List<TextAnnotationModel>? annotations))
        {
            annotations =
                new List<TextAnnotationModel>();

            _pageAnnotations[_activePageIndex] =
                annotations;
        }

        int annotationIndex =
            annotations.Count;

        annotations.Add(
            annotation);

        AnnotationCommitted?.Invoke(
            _activePageIndex,
            annotation,
            annotationIndex);

        Point canvasPoint =
            new Point(
                Canvas.GetLeft(_activeTextInput),
                Canvas.GetTop(_activeTextInput));

        CancelTextInput(
            activeCanvas);

        var textBlock =
            new TextBlock
            {
                Text = annotation.Text,
                FontSize =
                    Math.Max(
                        1.0,
                        annotation.FontSize * _activeCanvasScale),
                Foreground =
                    CreateBrush(
                        annotation.Color,
                        annotation.Opacity),
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };

        Canvas.SetLeft(
            textBlock,
            canvasPoint.X);

        Canvas.SetTop(
            textBlock,
            canvasPoint.Y);

        activeCanvas.Children.Add(
            textBlock);

        e.Handled = true;
    }

    /// <summary>
    /// StrokeColorと不透明度から文字表示用Brushを作成する。
    /// </summary>
    private static Brush CreateBrush(
        StrokeColor color,
        byte opacity)
    {
        (byte red, byte green, byte blue) =
            StrokeColorDefinition.GetRgb(color);

        Color baseColor =
            Color.FromRgb(red, green, blue);

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
}
