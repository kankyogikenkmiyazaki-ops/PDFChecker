using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PdfDisplayTest;

public partial class MainWindow : Window
{
    private readonly PdfService _pdfService = new();

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
}