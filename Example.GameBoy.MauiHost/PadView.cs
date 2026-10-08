namespace Example.GameBoy.MauiHost;

using GonFox.GameBoy.Core.Devices;

using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

// Draws and hit-tests the buttons so several fingers can press at once, each as its own source.
internal sealed class PadView : SKCanvasView
{
    private static readonly SKColor BodyColor = new(0xC9, 0xCB, 0xC4);
    private static readonly SKColor CrossColor = new(0x2E, 0x31, 0x38);
    private static readonly SKColor FaceColor = new(0x9A, 0x24, 0x52);
    private static readonly SKColor PillColor = new(0x8E, 0x91, 0x99);
    private static readonly SKColor PressedColor = new(0xF0, 0xC0, 0x40);
    private static readonly SKColor TextColor = new(0x2E, 0x34, 0x6C);
    private readonly Dictionary<long, (JoypadButton? X, JoypadButton? Y, JoypadButton? Other)> touches = [];
    private byte shownMask;
    internal event Action<string, JoypadButton, bool>? Changed;

    internal PadView()
    {
        EnableTouchEvents = true;
        AutomationId = "Pad";
    }

    internal void ShowMask(byte mask)
    {
        if (mask == shownMask)
        {
            return;
        }

        shownMask = mask;
        InvalidateSurface();
    }

    private readonly record struct PadLayout(SKPoint Cross, float Arm, SKPoint A, SKPoint B, float Face, SKRect Select, SKRect Start);

    private static PadLayout Measure(float width, float height)
    {
        var unit = Math.Min(width / 2.4f, height / 1.4f);
        var cross = new SKPoint(width * 0.26f, height * 0.40f);
        float arm = unit * 0.42f, face = unit * 0.20f;
        var a = new SKPoint(width * 0.84f, height * 0.32f);
        var b = new SKPoint(width * 0.64f, height * 0.46f);
        float pillWidth = unit * 0.36f, pillHeight = unit * 0.11f, pillY = height * 0.80f;
        var select = SKRect.Create((width * 0.40f) - (pillWidth / 2), pillY, pillWidth, pillHeight);
        var start = SKRect.Create((width * 0.60f) - (pillWidth / 2), pillY, pillWidth, pillHeight);
        return new(cross, arm, a, b, face, select, start);
    }

    // Finds up to two directions on the cross (45-degree sectors), or one other button, at a point.
    private (JoypadButton? X, JoypadButton? Y, JoypadButton? Other) HitTest(SKPoint p)
    {
        var l = Measure(CanvasSize.Width, CanvasSize.Height);
        float dx = p.X - l.Cross.X, dy = p.Y - l.Cross.Y;
        if ((dx * dx) + (dy * dy) <= l.Arm * 1.35f * (l.Arm * 1.35f) && (dx * dx) + (dy * dy) >= l.Arm * l.Arm * 0.04f)
        {
            var angle = Math.Atan2(-dy, dx) * 180 / Math.PI; // 0 = right, 90 = up
            var sector = (int)Math.Round(((angle + 360) % 360) / 45) % 8;
            JoypadButton? x = sector is 0 or 1 or 7 ? JoypadButton.Right : sector is 3 or 4 or 5 ? JoypadButton.Left : null;
            JoypadButton? y = sector is 1 or 2 or 3 ? JoypadButton.Up : sector is 5 or 6 or 7 ? JoypadButton.Down : null;
            return (x, y, null);
        }
        var reach = l.Face * 1.5f;
        if (SKPoint.Distance(p, l.A) <= reach)
        {
            return (null, null, JoypadButton.A);
        }

        if (SKPoint.Distance(p, l.B) <= reach)
        {
            return (null, null, JoypadButton.B);
        }

        SKRect select = l.Select, start = l.Start;
        select.Inflate(l.Face * 0.5f, l.Face * 0.6f);
        start.Inflate(l.Face * 0.5f, l.Face * 0.6f);
        if (select.Contains(p))
        {
            return (null, null, JoypadButton.Select);
        }

        if (start.Contains(p))
        {
            return (null, null, JoypadButton.Start);
        }

        return (null, null, null);
    }

    protected override void OnTouch(SKTouchEventArgs e)
    {
        var id = e.Id;
        touches.TryGetValue(id, out var before);
        var after = e.ActionType is SKTouchAction.Pressed or SKTouchAction.Moved ? HitTest(e.Location) : default;
        if (e.ActionType is SKTouchAction.Pressed or SKTouchAction.Moved)
        {
            touches[id] = after;
        }
        else
        {
            touches.Remove(id);
        }

        Update($"touch:{id}:x", before.X, after.X);
        Update($"touch:{id}:y", before.Y, after.Y);
        Update($"touch:{id}:b", before.Other, after.Other);
        e.Handled = true;
    }

    private void Update(string source, JoypadButton? before, JoypadButton? after)
    {
        if (before == after)
        {
            return;
        }

        if (before is { } released)
        {
            Changed?.Invoke(source, released, false);
        }

        if (after is { } pressed)
        {
            Changed?.Invoke(source, pressed, true);
        }
    }

    // Lifts every finger, as when the app goes to the background.
    internal void ReleaseAll()
    {
        foreach (var (id, held) in touches.ToArray())
        {
            Update($"touch:{id}:x", held.X, null);
            Update($"touch:{id}:y", held.Y, null);
            Update($"touch:{id}:b", held.Other, null);
        }
        touches.Clear();
    }

    private bool Down(JoypadButton button) => (shownMask & (1 << (int)button)) != 0;

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(BodyColor);
        var l = Measure(e.Info.Width, e.Info.Height);
        using var fill = new SKPaint();
        fill.IsAntialias = true;
        float arm = l.Arm, thick = arm * 0.62f;
        void Arm(SKRect rect, JoypadButton button)
        {
            fill.Color = Down(button) ? PressedColor : CrossColor;
            canvas.DrawRoundRect(rect, thick * 0.15f, thick * 0.15f, fill);
        }
        Arm(SKRect.Create(l.Cross.X - (thick / 2), l.Cross.Y - arm, thick, arm), JoypadButton.Up);
        Arm(SKRect.Create(l.Cross.X - (thick / 2), l.Cross.Y, thick, arm), JoypadButton.Down);
        Arm(SKRect.Create(l.Cross.X - arm, l.Cross.Y - (thick / 2), arm, thick), JoypadButton.Left);
        Arm(SKRect.Create(l.Cross.X, l.Cross.Y - (thick / 2), arm, thick), JoypadButton.Right);
        fill.Color = CrossColor;
        canvas.DrawRect(SKRect.Create(l.Cross.X - (thick / 2), l.Cross.Y - (thick / 2), thick, thick), fill);

        using var font = new SKFont();
        font.Size = l.Face * 0.55f;
        font.Embolden = true;
        using var text = new SKPaint();
        text.IsAntialias = true;
        text.Color = TextColor;
        void Round(SKPoint at, JoypadButton button, string label)
        {
            fill.Color = Down(button) ? PressedColor : FaceColor;
            canvas.DrawCircle(at, l.Face, fill);
            canvas.DrawText(label, at.X, at.Y + (l.Face * 1.75f), SKTextAlign.Center, font, text);
        }
        Round(l.A, JoypadButton.A, "A");
        Round(l.B, JoypadButton.B, "B");
        void Long(SKRect rect, JoypadButton button, string label)
        {
            fill.Color = Down(button) ? PressedColor : PillColor;
            canvas.DrawRoundRect(rect, rect.Height / 2, rect.Height / 2, fill);
            canvas.DrawText(label, rect.MidX, rect.Bottom + (l.Face * 0.75f), SKTextAlign.Center, font, text);
        }
        Long(l.Select, JoypadButton.Select, "SELECT");
        Long(l.Start, JoypadButton.Start, "START");
    }
}
