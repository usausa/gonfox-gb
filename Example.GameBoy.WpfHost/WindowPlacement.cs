namespace Example.GameBoy.WpfHost;

using System.Windows;

internal static class WindowPlacement
{
    // Fits saved bounds to the screen, or returns null when the title bar would be out of reach.
    internal static WindowBounds? Fit(WindowBounds bounds, Rect screen, Size minimum)
    {
        var width = Math.Clamp(bounds.Width, minimum.Width, Math.Max(minimum.Width, screen.Width));
        var height = Math.Clamp(bounds.Height, minimum.Height, Math.Max(minimum.Height, screen.Height));
        Rect title = new(bounds.Left, bounds.Top, width, 32);
        title.Intersect(screen);
        if (title.IsEmpty || title.Width < Math.Min(160, width) || title.Height < 16)
        {
            return null;
        }

        return bounds with { Width = width, Height = height };
    }
}
