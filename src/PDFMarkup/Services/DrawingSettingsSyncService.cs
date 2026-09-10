using PDFMarkup.Models;

namespace PDFMarkup.Services;

/// <summary>
/// 同一プロセス内のPDFウィンドウ間で、新規描画用設定を共有する。
/// </summary>
public static class DrawingSettingsSyncService
{
    public sealed record Snapshot(
        DrawingMode Mode,
        StrokeColor MarkupColor,
        double MarkupThickness,
        byte MarkupOpacity,
        StrokeColor CheckColor,
        double CheckThickness,
        byte CheckOpacity,
        string Diameter);

    private static Snapshot? _current;

    public static event Action<Guid, Snapshot>? Changed;

    public static Snapshot? Current =>
        _current ??= new SettingsService().LoadDrawingSettings();

    public static Snapshot? LoadLatest()
    {
        Snapshot? latest =
            new SettingsService().LoadDrawingSettings();

        if (latest != null)
        {
            _current = latest;
        }

        return latest;
    }

    public static void Publish(Guid sourceId, Snapshot snapshot)
    {
        _current = snapshot;
        new SettingsService().SaveDrawingSettings(snapshot);
        Changed?.Invoke(sourceId, snapshot);
    }
}
