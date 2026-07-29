using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;

namespace PDFMarkup;

public partial class MainWindow : Window
{
    private readonly PdfService _pdfService = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OpenPdfMenuItem_Click(object sender, RoutedEventArgs e)
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

            Title = $"PDF Markup - {fileName}";
            StatusText.Text = dialog.FileName;
            PageText.Text = $"ページ: 1 / {pageCount}";
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
}