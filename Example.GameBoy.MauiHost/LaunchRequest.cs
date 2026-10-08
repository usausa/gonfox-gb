namespace Example.GameBoy.MauiHost;

// Passes the intent extras of a start (demo, rom, bootrom, muted, volume) to the page.
internal sealed record LaunchRequest(int? Demo, string? Rom, string? BootRom, bool? Muted, int? Volume)
{
    private static LaunchRequest? pending;
    internal static event Action? Arrived;

    // Set when Android brings back an activity whose process ended, so the page resumes its session.
    internal static bool Restored { get; set; }

    internal bool IsEmpty => Demo is null && Rom is null && BootRom is null && Muted is null && Volume is null;

    internal static void Post(LaunchRequest request)
    {
        if (request.IsEmpty)
        {
            return;
        }

        AppLog.Info($"launch demo={request.Demo} rom={request.Rom} bootrom={request.BootRom} muted={request.Muted} volume={request.Volume}");
        Interlocked.Exchange(ref pending, request);
        Arrived?.Invoke();
    }

    internal static LaunchRequest? Take() => Interlocked.Exchange(ref pending, null);
}
