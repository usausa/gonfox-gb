// ReSharper disable CheckNamespace
#pragma warning disable IDE0130
namespace Example.GameBoy.MauiHost;

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

// Runs the app in portrait and posts the extras of every start as a LaunchRequest.
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
        ConfigChanges.SmallestScreenSize | ConfigChanges.Density | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden)]
public class MainActivity : MauiAppCompatActivity
{
    // Marks a restored activity before the page is made, then posts the extras of a fresh start.
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        LaunchRequest.Restored = savedInstanceState is not null;
        base.OnCreate(savedInstanceState);
        AppLog.Info($"create restored={LaunchRequest.Restored} files={GetExternalFilesDir(null)?.AbsolutePath}");
        if (savedInstanceState is null && Intent is { } intent && (intent.Flags & ActivityFlags.LaunchedFromHistory) == 0)
        {
            Post(intent);
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent is not null)
        {
            Post(intent);
        }
    }

    private static void Post(Intent intent) => LaunchRequest.Post(new LaunchRequest(
        intent.HasExtra("demo") ? intent.GetIntExtra("demo", 0) : null,
        intent.GetStringExtra("rom"),
        intent.GetStringExtra("bootrom"),
        intent.HasExtra("muted") ? intent.GetBooleanExtra("muted", false) : null,
        intent.HasExtra("volume") ? intent.GetIntExtra("volume", 0) : null));
}
