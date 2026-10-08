namespace GonFox.GameBoy.Core.Audio;

// Converts the mixed level to 48 kHz PCM: a mean per sample, then the DMG output high-pass.
internal sealed class AudioMixer(AudioOutput output)
{
    // PCM units per mixed level step (the loudest level, 480, gives 30,720).
    internal const int Scale = 64;
    private const long Unit = (long)Scale << 16; // One level step in Q16.
    private const int ClockRate = GameBoySystem.TCyclesPerSecond;
    private const int SampleRate = AudioOutput.SampleRate;

    // Capacitor charge kept per 48 kHz sample (0.999958 per T-cycle), in Q16.
    private const long Charge = 65_296;
    private int phase; // (T-cycles x 48,000) mod 4,194,304.
    private int cycles;

    // Level x T-cycles summed over the current sample.
    private long left;
    private long right;
    private long capacitorLeft;
    private long capacitorRight;

    internal sealed record State(int Phase, int Cycles, long Left, long Right, long CapacitorLeft, long CapacitorRight);

    internal State CaptureState() => new(phase, cycles, left, right, capacitorLeft, capacitorRight);

    internal static bool IsValid(State? s, ulong totalTCycles) => s is not null &&
        s.Phase == (int)(((totalTCycles % ClockRate) * SampleRate) % ClockRate) && s.Cycles == s.Phase / SampleRate &&
        Math.Abs(s.Left) <= 480L * s.Cycles && Math.Abs(s.Right) <= 480L * s.Cycles;

    internal void RestoreState(State s) =>
        (phase, cycles, left, right, capacitorLeft, capacitorRight) = (s.Phase, s.Cycles, s.Left, s.Right, s.CapacitorLeft, s.CapacitorRight);

    internal int CyclesUntilSample => (ClockRate - phase + SampleRate - 1) / SampleRate;

    // Integrates any number of T-cycles at one level, writing each sample it completes.
    internal void Advance(long duration, int levelLeft, int levelRight, bool dacsOn)
    {
        for (var step = CyclesUntilSample; duration >= step; step = CyclesUntilSample)
        {
            duration -= step;
            phase += (step * SampleRate) - ClockRate;
            long inputLeft = levelLeft * Unit, inputRight = levelRight * Unit;
            if (cycles != 0)
            {
                var total = cycles + step;
                inputLeft = (left + ((long)levelLeft * step)) * Unit / total;
                inputRight = (right + ((long)levelRight * step)) * Unit / total;
                (left, right, cycles) = (0, 0, 0);
            }
            if (!dacsOn)
            {
                output.Write(0, 0); // Capacitors keep their charge.
            }
            else
            {
                output.Write(HighPass(ref capacitorLeft, inputLeft), HighPass(ref capacitorRight, inputRight));
            }
        }
        left += levelLeft * duration;
        right += levelRight * duration;
        cycles += (int)duration;
        phase += (int)duration * SampleRate;
    }

    private static short HighPass(ref long capacitor, long input)
    {
        var result = input - capacitor;
        capacitor = input - (((result * Charge) + 0x8000) >> 16); // Q16, rounded half up.
        return (short)Math.Clamp((result + 0x8000) >> 16, short.MinValue, short.MaxValue);
    }

    // Restarts sampling with the high-pass settled on the given levels.
    internal void Reset(int levelLeft, int levelRight)
    {
        (phase, cycles, left, right) = (0, 0, 0, 0);
        capacitorLeft = levelLeft * Unit;
        capacitorRight = levelRight * Unit;
    }
}
