using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PDFMarkup.Services;

/// <summary>
/// PDFMarkupの軽量なローカル設定を読み書きするサービス。
/// 最近使ったPDFやウィンドウ表示状態など、ユーザーごとの設定を保持する。
/// </summary>
public sealed class SettingsService
{
    private const int MaximumRecentFileCount = 5;

    private readonly string _settingsDirectory;
    private readonly string _settingsFilePath;

    /// <summary>
    /// ユーザーごとのAppData配下へ設定保存先を作成する。
    /// </summary>
    public SettingsService()
    {
        _settingsDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "PDFMarkup");

        _settingsFilePath =
            Path.Combine(
                _settingsDirectory,
                "settings.json");
    }

    /// <summary>
    /// 保存済みの最近使ったPDFを取得する。
    /// 存在しなくなったファイルは一覧から除外して返す。
    /// </summary>
    public IReadOnlyList<string> LoadRecentFiles()
    {
        AppSettings settings =
            LoadSettings();

        List<string> existingFiles =
            settings.RecentFiles
                .Where(path =>
                    !string.IsNullOrWhiteSpace(path) &&
                    File.Exists(path))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Take(MaximumRecentFileCount)
                .ToList();

        if (!settings.RecentFiles.SequenceEqual(
                existingFiles,
                StringComparer.OrdinalIgnoreCase))
        {
            settings.RecentFiles =
                existingFiles;

            SaveSettings(
                settings);
        }

        return existingFiles;
    }

    /// <summary>
    /// 指定PDFを最近使ったファイルの先頭へ追加する。
    /// 同じパスは重複させず、最大5件だけ保持する。
    /// </summary>
    public IReadOnlyList<string> AddRecentFile(
        string filePath)
    {
        string fullPath =
            Path.GetFullPath(
                filePath);

        AppSettings settings =
            LoadSettings();

        settings.RecentFiles.RemoveAll(path =>
            string.Equals(
                path,
                fullPath,
                StringComparison.OrdinalIgnoreCase));

        settings.RecentFiles.Insert(
            0,
            fullPath);

        settings.RecentFiles =
            settings.RecentFiles
                .Where(path =>
                    !string.IsNullOrWhiteSpace(path) &&
                    File.Exists(path))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Take(MaximumRecentFileCount)
                .ToList();

        SaveSettings(
            settings);

        return settings.RecentFiles;
    }


    /// <summary>
    /// 指定PDFを最近使ったファイル一覧から削除する。
    /// PDFファイル本体は削除しない。
    /// </summary>
    public IReadOnlyList<string> RemoveRecentFile(
        string filePath)
    {
        string fullPath =
            Path.GetFullPath(
                filePath);

        AppSettings settings =
            LoadSettings();

        settings.RecentFiles.RemoveAll(path =>
            string.Equals(
                path,
                fullPath,
                StringComparison.OrdinalIgnoreCase));

        SaveSettings(
            settings);

        return settings.RecentFiles;
    }

    /// <summary>
    /// 最近使ったファイル履歴をすべて削除する。
    /// PDFファイル本体は削除しない。
    /// </summary>
    public void ClearRecentFiles()
    {
        AppSettings settings =
            LoadSettings();

        settings.RecentFiles.Clear();

        SaveSettings(
            settings);
    }


    /// <summary>
    /// 保存済みのウィンドウ位置・サイズを取得する。
    /// 未保存の場合はnullを返す。
    /// </summary>
    public WindowPlacementSettings? LoadWindowPlacement()
    {
        return LoadSettings().WindowPlacement;
    }

    /// <summary>
    /// ウィンドウの通常時位置・サイズと最大化状態を保存する。
    /// </summary>
    public void SaveWindowPlacement(
        double left,
        double top,
        double width,
        double height,
        bool isMaximized)
    {
        if (double.IsNaN(left) ||
            double.IsNaN(top) ||
            double.IsNaN(width) ||
            double.IsNaN(height) ||
            width <= 0 ||
            height <= 0)
        {
            return;
        }

        AppSettings settings =
            LoadSettings();

        settings.WindowPlacement =
            new WindowPlacementSettings
            {
                Left = left,
                Top = top,
                Width = width,
                Height = height,
                IsMaximized = isMaximized
            };

        SaveSettings(
            settings);
    }

    /// <summary>
    /// 保存済みの左右パネル状態を取得する。
    /// 未保存の場合はnullを返す。
    /// </summary>
    public PanelLayoutSettings? LoadPanelLayout()
    {
        return LoadSettings().PanelLayout;
    }

    /// <summary>
    /// 左右パネルの開閉状態と、開いているときの幅を保存する。
    /// </summary>
    public void SavePanelLayout(
        bool isLeftPanelOpen,
        double leftPanelWidth,
        bool isRightPanelOpen,
        double rightPanelWidth)
    {
        if (double.IsNaN(leftPanelWidth) ||
            double.IsNaN(rightPanelWidth) ||
            leftPanelWidth <= 0 ||
            rightPanelWidth <= 0)
        {
            return;
        }

        AppSettings settings =
            LoadSettings();

        settings.PanelLayout =
            new PanelLayoutSettings
            {
                IsLeftPanelOpen = isLeftPanelOpen,
                LeftPanelWidth = leftPanelWidth,
                IsRightPanelOpen = isRightPanelOpen,
                RightPanelWidth = rightPanelWidth
            };

        SaveSettings(
            settings);
    }

    /// <summary>
    /// settings.jsonを読み込む。
    /// ファイルが存在しない、または内容が壊れている場合は初期設定を返す。
    /// </summary>
    private AppSettings LoadSettings()
    {
        if (!File.Exists(
                _settingsFilePath))
        {
            return new AppSettings();
        }

        try
        {
            string json =
                File.ReadAllText(
                    _settingsFilePath);

            return JsonSerializer.Deserialize<AppSettings>(json)
                   ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// 現在の設定をsettings.jsonへ保存する。
    /// </summary>
    private void SaveSettings(
        AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(
                _settingsDirectory);

            string json =
                JsonSerializer.Serialize(
                    settings,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

            File.WriteAllText(
                _settingsFilePath,
                json);
        }
        catch (IOException)
        {
            // 設定保存に失敗しても、PDF本体の操作は継続する。
        }
        catch (UnauthorizedAccessException)
        {
            // 設定保存に失敗しても、PDF本体の操作は継続する。
        }
    }

    /// <summary>
    /// 保存する左右パネルの開閉状態と幅。
    /// 閉じている場合も、再度開くための直前幅を保持する。
    /// </summary>
    public sealed class PanelLayoutSettings
    {
        public bool IsLeftPanelOpen { get; set; } = true;

        public double LeftPanelWidth { get; set; } = 220.0;

        public bool IsRightPanelOpen { get; set; } = true;

        public double RightPanelWidth { get; set; } = 260.0;
    }

    /// <summary>
    /// 保存するウィンドウ位置・サイズ。
    /// 最大化時もRestoreBounds相当の通常時サイズを保持する。
    /// </summary>
    public sealed class WindowPlacementSettings
    {
        public double Left { get; set; }

        public double Top { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public bool IsMaximized { get; set; }
    }

    /// <summary>
    /// settings.jsonへ保存する設定項目。
    /// </summary>
    private sealed class AppSettings
    {
        public List<string> RecentFiles { get; set; } = new();

        public WindowPlacementSettings? WindowPlacement { get; set; }

        public PanelLayoutSettings? PanelLayout { get; set; }
    }
}
