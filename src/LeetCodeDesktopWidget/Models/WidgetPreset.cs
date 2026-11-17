using System;

namespace LeetCodeDesktopWidget.Models;

/// <summary>
/// User-facing widget size presets.
/// Replaces arbitrary manual resizing with intentionally designed standard form factors.
/// </summary>
public enum WidgetPreset
{
    /// <summary>
    /// 2×2: Minimal desktop footprint with essential statistics and recent activity.
    /// </summary>
    Compact,

    /// <summary>
    /// 4×2: Primary/default experience with full 12-month heatmap and complete difficulty breakdown.
    /// </summary>
    Standard,

    /// <summary>
    /// 6×2: Expanded horizontal presence with generous cell spacing across the entire year.
    /// </summary>
    Wide,

    /// <summary>
    /// 6×3: High-clarity expanded dashboard with increased vertical space and rich telemetry.
    /// </summary>
    Large
}

/// <summary>
/// Centralized dimensions and layout configuration for each widget preset.
/// </summary>
public record WidgetPresetDimensions(
    WidgetPreset Preset,
    string DisplayName,
    string GridLabel,
    string Description,
    double Width,
    double Height,
    double HeatmapCellSize,
    double HeatmapCellSpacing,
    int? HeatmapMaxColumns = null)
{
    public static readonly WidgetPresetDimensions Compact = new(
        WidgetPreset.Compact,
        "Compact",
        "2×2",
        "Minimal desktop footprint with essential statistics and recent activity.",
        Width: 420,
        Height: 260,
        HeatmapCellSize: 11.5,
        HeatmapCellSpacing: 3.0,
        HeatmapMaxColumns: 24);

    public static readonly WidgetPresetDimensions Standard = new(
        WidgetPreset.Standard,
        "Standard",
        "4×2",
        "Full 12-month heatmap with complete statistics and difficulty breakdown.",
        Width: 845,
        Height: 285,
        HeatmapCellSize: 11.5,
        HeatmapCellSpacing: 3.0,
        HeatmapMaxColumns: null);

    public static readonly WidgetPresetDimensions Wide = new(
        WidgetPreset.Wide,
        "Wide",
        "6×2",
        "Expanded horizontal presence with generous cell spacing across the year.",
        Width: 1180,
        Height: 285,
        HeatmapCellSize: 15.0,
        HeatmapCellSpacing: 3.0,
        HeatmapMaxColumns: null);

    public static readonly WidgetPresetDimensions Large = new(
        WidgetPreset.Large,
        "Large",
        "6×3",
        "Spacious high-clarity dashboard with expanded vertical height and rich telemetry.",
        Width: 1180,
        Height: 410,
        HeatmapCellSize: 16.5,
        HeatmapCellSpacing: 4.0,
        HeatmapMaxColumns: null);

    public static WidgetPresetDimensions ForPreset(WidgetPreset preset) => preset switch
    {
        WidgetPreset.Compact => Compact,
        WidgetPreset.Wide => Wide,
        WidgetPreset.Large => Large,
        _ => Standard
    };
}
