using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PDFMarkup.Services;

/// <summary>
/// PDFMarkupの軽量なローカル設定を読み書きするサービス。
/// 現在は最近使ったPDFのみを保持する。
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
    /// settings.jsonへ保存する設定項目。
    /// </summary>
    private sealed class AppSettings
    {
        public List<string> RecentFiles { get; set; } = new();
    }
}
