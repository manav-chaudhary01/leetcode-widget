using System;
using System.Windows;
using System.Windows.Media;
using LeetCodeDesktopWidget.Desktop;

namespace LeetCodeDesktopWidget.Helpers;

/// <summary>
/// Helper utilities for multi-monitor desktop position calculation, virtual screen bounds,
/// clamping, and DPI-aware cursor coordinates.
/// </summary>
public static class PositionHelper
{
    public const double DefaultOffset = 60.0;

    /// <summary>
    /// Gets the bounding rectangle of the virtual screen across all monitors in DIPs.
    /// Falls back to sensible defaults on headless or uninitialized systems.
    /// </summary>
    public static Rect GetVirtualScreenBounds()
    {
        try
        {
            double left = SystemParameters.VirtualScreenLeft;
            double top = SystemParameters.VirtualScreenTop;
            double width = SystemParameters.VirtualScreenWidth;
            double height = SystemParameters.VirtualScreenHeight;

            if (width > 0 && height > 0)
            {
                return new Rect(left, top, width, height);
            }
        }
        catch
        {
            // Headless / unit test fallback
        }

        try
        {
            double pWidth = SystemParameters.PrimaryScreenWidth;
            double pHeight = SystemParameters.PrimaryScreenHeight;
            if (pWidth > 0 && pHeight > 0)
            {
                return new Rect(0, 0, pWidth, pHeight);
            }
        }
        catch
        {
            // Headless fallback
        }

        return new Rect(0, 0, 1920, 1080);
    }

    /// <summary>
    /// Gets the primary work area in DIPs, with fallback.
    /// </summary>
    public static Rect GetWorkArea()
    {
        try
        {
            var workArea = SystemParameters.WorkArea;
            if (workArea.Width > 0 && workArea.Height > 0)
            {
                return workArea;
            }
        }
        catch
        {
            // Fallback
        }

        return new Rect(0, 0, 1920, 1040);
    }

    /// <summary>
    /// Calculates the default window position on first launch.
    /// Uses primary work area offset by (DefaultOffset, DefaultOffset) and clamped to the virtual screen.
    /// </summary>
    public static Point GetDefaultPosition(double windowWidth, double windowHeight)
    {
        return GetDefaultPosition(windowWidth, windowHeight, GetWorkArea(), GetVirtualScreenBounds());
    }

    /// <summary>
    /// Calculates default window position given an explicit work area and virtual screen bounds.
    /// </summary>
    public static Point GetDefaultPosition(double windowWidth, double windowHeight, Rect workArea, Rect virtualBounds)
    {
        double targetX = workArea.Left + DefaultOffset;
        double targetY = workArea.Top + DefaultOffset;
        return ClampWindowPosition(targetX, targetY, windowWidth, windowHeight, virtualBounds);
    }

    /// <summary>
    /// Clamps candidate window coordinates (x, y) so that the entire window remains within the virtual screen bounds.
    /// Handles multi-monitor environments, negative coordinates, and guards against NaN/Infinity.
    /// </summary>
    public static Point ClampWindowPosition(double x, double y, double windowWidth, double windowHeight, Rect virtualBounds)
    {
        if (double.IsNaN(x) || double.IsInfinity(x))
            x = virtualBounds.Left + DefaultOffset;
        if (double.IsNaN(y) || double.IsInfinity(y))
            y = virtualBounds.Top + DefaultOffset;

        double w = windowWidth > 0 ? windowWidth : 0;
        double h = windowHeight > 0 ? windowHeight : 0;

        double minX = virtualBounds.Left;
        double maxX = virtualBounds.Left + virtualBounds.Width - w;
        double minY = virtualBounds.Top;
        double maxY = virtualBounds.Top + virtualBounds.Height - h;

        double clampedX;
        if (maxX <= minX)
        {
            clampedX = minX;
        }
        else
        {
            clampedX = Math.Max(minX, Math.Min(x, maxX));
        }

        double clampedY;
        if (maxY <= minY)
        {
            clampedY = minY;
        }
        else
        {
            clampedY = Math.Max(minY, Math.Min(y, maxY));
        }

        return new Point(clampedX, clampedY);
    }

    /// <summary>
    /// Clamps candidate window coordinates using current system virtual screen bounds.
    /// </summary>
    public static Point ClampWindowPosition(double x, double y, double windowWidth, double windowHeight)
    {
        return ClampWindowPosition(x, y, windowWidth, windowHeight, GetVirtualScreenBounds());
    }

    /// <summary>
    /// Gets the current cursor position in device-independent units (DIPs) relative to the screen.
    /// Uses Win32 GetCursorPos and adjusts for visual DPI scaling.
    /// </summary>
    public static Point GetCursorPositionDips(Visual? visual)
    {
        if (!DesktopInterop.GetCursorPos(out var pt))
        {
            return new Point(0, 0);
        }

        double dpiScaleX = 1.0;
        double dpiScaleY = 1.0;

        if (visual != null)
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(visual);
                if (dpi.DpiScaleX > 0) dpiScaleX = dpi.DpiScaleX;
                if (dpi.DpiScaleY > 0) dpiScaleY = dpi.DpiScaleY;
            }
            catch
            {
                // Fallback to 1.0 on unattached visual
            }
        }

        return new Point(pt.X / dpiScaleX, pt.Y / dpiScaleY);
    }
}
