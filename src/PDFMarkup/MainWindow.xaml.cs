using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace PDFMarkup;

public partial class MainWindow : Window
{
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

        Title = $"PDF Markup - {fileName}";
        StatusText.Text = dialog.FileName;
    }
}