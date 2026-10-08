namespace Example.GameBoy.MauiHost;

// Gives each window a page of its own that suspends on leaving and resumes on return.
public sealed class HostApp : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var page = new MainPage();
        var window = new Window { Page = page, Title = "GonFox GameBoy" };
        window.Stopped += async (_, _) => await page.SuspendAsync();
        window.Resumed += async (_, _) => await page.ResumeAsync();
        window.Destroying += async (_, _) => await page.ShutdownAsync();
        return window;
    }
}
