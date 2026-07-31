using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PdfDisplayTest;

public partial class MainWindow : Window
{
    private readonly PdfService _pdfService = new();
    private string? _currentPdfPath;

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
            string bmpPath = _pdfService.RenderFirstPageToBmp(dialog.FileName);

            var image = new BitmapImage();

            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(bmpPath);
            image.EndInit();
            image.Freeze();

            PdfImage.Source = image;

            Title = $"PDF表示テスト - {fileName}";

            FilePathText.Text =
                $"{dialog.FileName}　ページ数: {pageCount}　BMP: {bmpPath}";
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

}