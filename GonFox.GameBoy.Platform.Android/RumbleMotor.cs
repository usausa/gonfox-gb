namespace GonFox.GameBoy.Platform.Android;

using global::Android.Content;
using global::Android.Media;
using global::Android.OS;

// Vibrates for a rumble cartridge's motor in short pulses, renewed by each status report.
public sealed class RumbleMotor : IDisposable
{
    private const long PulseMilliseconds = 150;
    private readonly Vibrator? vibrator;
    private readonly VibrationAttributes? media;
    private readonly AudioAttributes? game;
    private bool running;

    public RumbleMotor()
    {
        var context = Application.Context;
        var found = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? (context.GetSystemService(Context.VibratorManagerService) as VibratorManager)?.DefaultVibrator
            : context.GetSystemService(Context.VibratorService) as Vibrator;
        vibrator = found?.HasVibrator == true ? found : null;
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            media = VibrationAttributes.CreateForUsage((int)VibrationAttributesUsageType.Media);
        }
        else
        {
            using var builder = new AudioAttributes.Builder();
            game = builder.SetUsage(AudioUsageKind.Game)!.Build();
        }
    }

    public bool Available => vibrator is not null;

    // Returns whether the motor's state changed.
    public bool Update(bool run)
    {
        if (vibrator is null)
        {
            return false;
        }

        if (run)
        {
            using var effect = VibrationEffect.CreateOneShot(PulseMilliseconds, VibrationEffect.DefaultAmplitude)!;
            Pulse(vibrator, effect);
        }
        else if (running)
        {
            vibrator.Cancel();
        }

        var changed = run != running;
        running = run;
        return changed;
    }

    private void Pulse(Vibrator device, VibrationEffect effect)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            device.Vibrate(effect, media!);
        }
        else
        {
            device.Vibrate(effect, game);
        }
    }

    public void Dispose()
    {
        Update(false);
        media?.Dispose();
        game?.Dispose();
    }
}
