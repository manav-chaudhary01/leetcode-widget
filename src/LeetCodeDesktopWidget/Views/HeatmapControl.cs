using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LeetCodeDesktopWidget.Models;

namespace LeetCodeDesktopWidget.Views;

/// <summary>
/// High-performance WPF drawing control for GitHub/LeetCode yearly contribution heatmap.
/// Renders the entire year in a single DrawingContext pass without creating hundreds of UI elements.
/// </summary>
public class HeatmapControl : FrameworkElement
{
    private const double CellSize = 11.0;
    private const double CellSpacing = 3.0;
    private const double Step = CellSize + CellSpacing;
    private const double GridOriginX = 32.0;
    private const double GridOriginY = 20.0;
    private const double CellCornerRadius = 2.0;

    // Pre-allocated, frozen brushes for 5 activity levels
    private static readonly Brush Level0Brush = CreateFrozenBrush(Color.FromRgb(0x16, 0x1B, 0x22));
    private static readonly Brush Level1Brush = CreateFrozenBrush(Color.FromRgb(0x0E, 0x44, 0x29));
    private static readonly Brush Level2Brush = CreateFrozenBrush(Color.FromRgb(0x00, 0x6D, 0x32));
    private static readonly Brush Level3Brush = CreateFrozenBrush(Color.FromRgb(0x26, 0xA6, 0x41));
    private static readonly Brush Level4Brush = CreateFrozenBrush(Color.FromRgb(0x39, 0xD3, 0x53));

    // Pre-allocated pens
    private static readonly Pen CellBorderPen = CreateFrozenPen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), 0.5);
    private static readonly Pen HoverPen = CreateFrozenPen(Color.FromRgb(0xE6, 0xED, 0xF3), 1.5);

    // Label brush & font
    private static readonly Brush LabelBrush = CreateFrozenBrush(Color.FromRgb(0x8B, 0x94, 0x9E));
    private static readonly Typeface LabelTypeface = new("Segoe UI Variable, Segoe UI, sans-serif");

    private readonly Dictionary<(int Col, int Row), DailySubmission> _gridMap = new();
    private (int Col, int Row)? _hoveredCoord;

    #region Dependency Properties

    public static readonly DependencyProperty ActivityProperty = DependencyProperty.Register(
        nameof(Activity),
        typeof(YearlyActivity),
        typeof(HeatmapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnActivityChanged));

    public YearlyActivity? Activity
    {
        get => (YearlyActivity?)GetValue(ActivityProperty);
        set => SetValue(ActivityProperty, value);
    }

    public static readonly DependencyProperty HoveredSubmissionProperty = DependencyProperty.Register(
        nameof(HoveredSubmission),
        typeof(DailySubmission),
        typeof(HeatmapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public DailySubmission? HoveredSubmission
    {
        get => (DailySubmission?)GetValue(HoveredSubmissionProperty);
        set => SetValue(HoveredSubmissionProperty, value);
    }

    #endregion

    public HeatmapControl()
    {
        ClipToBounds = true;
        Cursor = Cursors.Hand;
    }

    private static void OnActivityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeatmapControl control)
        {
            control.RebuildGridMap();
            control.InvalidateVisual();
        }
    }

    private void RebuildGridMap()
    {
        _gridMap.Clear();
        _hoveredCoord = null;
        HoveredSubmission = null;

        if (Activity == null) return;

        foreach (var day in Activity.Days)
        {
            var coord = YearlyActivity.GetGridCoordinates(day.Date);
            _gridMap[coord] = day;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // 53 columns * 14px + origin = ~774px width, 7 rows * 14px + origin = ~120px height
        return new Size(GridOriginX + (54 * Step), GridOriginY + (7 * Step));
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (Activity == null) return;

        // 1. Draw Weekday labels (Mon = row 1, Wed = row 3, Fri = row 5)
        DrawWeekdayLabel(dc, "Mon", 1);
        DrawWeekdayLabel(dc, "Wed", 3);
        DrawWeekdayLabel(dc, "Fri", 5);

        // 2. Draw Month labels at month start columns
        var monthStarts = Activity.GetMonthStartColumns();
        var culture = CultureInfo.InvariantCulture;

        for (int m = 1; m <= 12; m++)
        {
            if (monthStarts.TryGetValue(m, out int col))
            {
                string monthName = culture.DateTimeFormat.GetAbbreviatedMonthName(m);
                var formattedMonth = new FormattedText(
                    monthName,
                    culture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    9.5,
                    LabelBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                double x = GridOriginX + (col * Step);
                dc.DrawText(formattedMonth, new Point(x, 2));
            }
        }

        // 3. Draw Heatmap Squares
        foreach (var (coord, day) in _gridMap)
        {
            double x = GridOriginX + (coord.Col * Step);
            double y = GridOriginY + (coord.Row * Step);
            var rect = new Rect(x, y, CellSize, CellSize);

            Brush brush = day.Level switch
            {
                ActivityLevel.Low => Level1Brush,
                ActivityLevel.Medium => Level2Brush,
                ActivityLevel.High => Level3Brush,
                ActivityLevel.Maximum => Level4Brush,
                _ => Level0Brush
            };

            dc.DrawRoundedRectangle(brush, CellBorderPen, rect, CellCornerRadius, CellCornerRadius);

            // Draw highlight ring if hovered
            if (_hoveredCoord.HasValue && _hoveredCoord.Value == coord)
            {
                dc.DrawRoundedRectangle(null, HoverPen, rect, CellCornerRadius, CellCornerRadius);
            }
        }
    }

    private void DrawWeekdayLabel(DrawingContext dc, string text, int row)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            9.0,
            LabelBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        double y = GridOriginY + (row * Step) - 1;
        dc.DrawText(formatted, new Point(2, y));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var pos = e.GetPosition(this);
        int col = (int)((pos.X - GridOriginX) / Step);
        int row = (int)((pos.Y - GridOriginY) / Step);

        if (pos.X >= GridOriginX && pos.Y >= GridOriginY && col >= 0 && row >= 0 && row < 7)
        {
            var coord = (col, row);
            if (_gridMap.TryGetValue(coord, out var submission))
            {
                if (_hoveredCoord != coord)
                {
                    _hoveredCoord = coord;
                    HoveredSubmission = submission;
                    ToolTip = submission.GetDescription();
                    InvalidateVisual();
                }
                return;
            }
        }

        ClearHoverState();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ClearHoverState();
    }

    private void ClearHoverState()
    {
        if (_hoveredCoord.HasValue)
        {
            _hoveredCoord = null;
            HoveredSubmission = null;
            ToolTip = null;
            InvalidateVisual();
        }
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreateFrozenPen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}
