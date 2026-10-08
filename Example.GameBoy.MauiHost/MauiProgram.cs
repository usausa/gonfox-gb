namespace Example.GameBoy.MauiHost;

using SkiaSharp.Views.Maui.Controls.Hosting;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<HostApp>().UseSkiaSharp().Build();
}
