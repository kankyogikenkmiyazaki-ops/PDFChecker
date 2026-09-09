using System.Windows;

using System;
using System.IO;
using System.Linq;

namespace PDFMarkup;

/// <summary>
/// PDFMarkupアプリケーション全体のエントリーポイント。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();

        string? pdfPath = e.Args.FirstOrDefault(path =>
            string.Equals(
                Path.GetExtension(path),
                ".pdf",
                StringComparison.OrdinalIgnoreCase));

        if (pdfPath != null)
        {
            window.OpenPdfFromExternalPath(pdfPath);
        }

        window.Show();
    }
}
