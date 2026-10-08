namespace GonFox.GameBoy.Platform;

using System.Buffers.Binary;
using System.IO;

using GonFox.GameBoy.Core.Cartridge;

// Tests the MBC3 battery file layout and when a periodic save writes.
public sealed class BatteryFileTests
{
    private static readonly RtcSnapshot Clock = new(new(59, 58, 23, 0xFE, 0xC1), new(1, 2, 3, 4, 0x80));

    [Fact]
    public void ClockFooterFollowsTheSharedLayout()
    {
        byte[] ram = [1, 2, 3, 4];
        var file = BatteryFile.Encode(new(ram, Clock, 1_700_000_000_123));
        Assert.Equal(4 + 48, file.Length);
        Assert.Equal(ram, file[..4]);
        var values = Enumerable.Range(0, 10).Select(i => BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4 + (i * 4)))).ToArray();
        Assert.Equal(new uint[] { 59, 58, 23, 0xFE, 0xC1, 1, 2, 3, 4, 0x80 }, values);
        Assert.Equal(1_700_000_000_123, BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(44)));
        var decoded = BatteryFile.Decode(file, 4, hasClock: true, "x.sav");
        Assert.Equal(ram, decoded.Ram);
        Assert.Equal(Clock, decoded.Clock);
        Assert.Equal(1_700_000_000_123, decoded.SavedUnixSeconds);
        Assert.Equal(ram, BatteryFile.Encode(new(ram)));
    }

    [Fact]
    public void LegacyFooterAndFilesWithoutTheClockAreRead()
    {
        var file = new byte[8 + 44];
        file[8] = 7;
        file[8 + 12] = 9;
        file[8 + 16] = 0x41;
        file[8 + 20] = 6;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8 + 40), 3_000_000_000); // Unsigned 32-bit time.
        var legacy = BatteryFile.Decode(file, 8, hasClock: true, "x.sav");
        Assert.Equal(new RtcSnapshot(new(7, 0, 0, 9, 0x41), new(6, 0, 0, 0, 0)), legacy.Clock);
        Assert.Equal(3_000_000_000, legacy.SavedUnixSeconds);
        var plain = BatteryFile.Decode(new byte[8], 8, hasClock: true, "x.sav");
        Assert.Null(plain.Clock);
        Assert.Null(plain.SavedUnixSeconds);
        Assert.Empty(BatteryFile.Decode(new byte[48], 0, hasClock: true, "x.sav").Ram); // Type 0F: the clock only.
    }

    [Theory]
    [InlineData(8, true, 7)]
    [InlineData(8, true, 9)]
    [InlineData(8, true, 8 + 47)]
    [InlineData(8, true, 8 + 49)]
    [InlineData(8, false, 8 + 48)]
    [InlineData(8, false, 8 + 44)]
    public void OtherLengthsAreRejected(int ramLength, bool hasClock, int fileLength)
    {
        var error = Assert.Throws<InvalidDataException>(() => BatteryFile.Decode(new byte[fileLength], ramLength, hasClock, "x.sav"));
        Assert.Contains("x.sav", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PeriodicSaveWaitsUntilTheRamStaysTheSame()
    {
        var policy = new AutoSavePolicy();
        var start = DateTimeOffset.UnixEpoch;
        string rom = new('a', 64);
        // ReSharper disable once UseUtf8StringLiteral
        policy.Saved(rom, [0, 0]);
        // ReSharper disable once UseUtf8StringLiteral
        Assert.False(policy.ShouldSave(rom, [0, 0], start));
        Assert.False(policy.ShouldSave(rom, [1, 0], start + TimeSpan.FromSeconds(5)));
        Assert.True(policy.ShouldSave(rom, [1, 0], start + TimeSpan.FromSeconds(10)));
        Assert.True(policy.ShouldSave(rom, [1, 0], start + TimeSpan.FromSeconds(15)));
        policy.Saved(rom, [1, 0]);
        Assert.False(policy.ShouldSave(rom, [1, 0], start + TimeSpan.FromSeconds(20)));
        // ReSharper disable once UseUtf8StringLiteral
        Assert.False(policy.ShouldSave(rom, [0, 0], start + TimeSpan.FromSeconds(25)));
        Assert.False(policy.ShouldSave(rom, [1, 0], start + TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void PeriodicSaveWritesAMinuteAfterTheFirstChangeEvenIfTheRamKeepsChanging()
    {
        var policy = new AutoSavePolicy();
        var start = DateTimeOffset.UnixEpoch;
        string rom = new('a', 64);
        policy.Saved(rom, [0]);
        for (var check = 1; check < 13; check++)
        {
            Assert.False(policy.ShouldSave(rom, [(byte)check], start + TimeSpan.FromSeconds(check * 5)));
        }

        Assert.True(policy.ShouldSave(rom, [13], start + TimeSpan.FromSeconds(65))); // 60 s after the first change.
        Assert.False(policy.ShouldSave(new string('b', 64), [0], start));
    }
}
