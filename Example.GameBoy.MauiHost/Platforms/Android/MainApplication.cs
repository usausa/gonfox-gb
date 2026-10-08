// ReSharper disable CheckNamespace
#pragma warning disable IDE0130
namespace Example.GameBoy.MauiHost;

using Android.App;
using Android.Runtime;

[Application]
public class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
