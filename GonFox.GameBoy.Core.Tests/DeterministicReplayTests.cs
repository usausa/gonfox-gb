namespace GonFox.GameBoy.Core;

using System.Security.Cryptography;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Video;

[Trait("Category", "Rom")]
public sealed class DeterministicReplayTests(ITestOutputHelper output)
{
    private static byte[] ReadRom(bool mbc1)
    {
        var image = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", mbc1 ? "mbc1-demo.gb" : "background-demo.gb"));
        Assert.Equal(mbc1 ? "2bdc7882666d81731b8a9936670049e8a4e1b8feee07f1f73ffa27f12bb8c812"
            : "9053de305c39aed9780a74057e1fc44e99d9cc8ac3a62b5eb7d4ccdd82e9c0f8", Convert.ToHexStringLower(SHA256.HashData(image)));
        return image;
    }

    private static (GameBoySystem System, ICartridge Cartridge) Start(byte[] image, byte[]? ram = null)
    {
        var cartridge = CartridgeLoader.Load(image).Cartridge;
        if (ram is not null)
        {
            ((IBatteryBackedCartridge)cartridge).ImportRam(ram);
        }

        var system = new GameBoySystem();
        system.InsertCartridge(cartridge);
        return (system, cartridge);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimestampedInputsMatchForStepsDifferentBudgetsAndReset(bool mbc1)
    {
        var image = ReadRom(mbc1);
        var stepped = Start(image);
        var budgeted = Start(image);
        var reset = Start(image);

        // Dirties the machine by running it, then restores the starting RAM and resets.
        reset.System.RunForTCycles(500_000);
        if (reset.Cartridge is IBatteryBackedCartridge battery)
        {
            battery.ImportRam(new byte[32768]);
        }

        reset.System.Reset();
        var resetSequence = reset.System.Video.Sequence - stepped.System.Video.Sequence;
        var resetFrames = reset.System.Video.CompletedFrameCount - stepped.System.Video.CompletedFrameCount;
        (int Time, JoypadButton Button, bool Pressed)[] inputs =
        [
            (0, JoypadButton.Start, true), (200_000, JoypadButton.Right, true),
            (400_000, JoypadButton.Right, false), (400_000, JoypadButton.A, true),
            (600_000, JoypadButton.A, false), (600_000, JoypadButton.Down, true),
            (800_000, JoypadButton.Down, false), (800_000, JoypadButton.B, true),
            (1_000_000, JoypadButton.B, false), (1_200_000, JoypadButton.Select, true),
            (1_400_000, JoypadButton.Select, false), (1_400_000, JoypadButton.Right, true),
            (1_600_000, JoypadButton.Right, false), (1_800_000, JoypadButton.Start, false)
        ];
        var images = new HashSet<string>();
        foreach (var (time1, button, pressed) in inputs)
        {
            while (stepped.System.TotalTCycles < (ulong)time1)
            {
                Assert.True(stepped.System.StepInstruction().ExecutedTCycles > 0, "Unexpected STOP before the recorded input.");
            }

            // Uses the instruction boundary reached, not the requested time.
            var time = stepped.System.TotalTCycles;
            AdvanceTo(budgeted.System, time, 4096);
            AdvanceTo(reset.System, time, 997);
            stepped.System.Joypad.SetButtonState(button, pressed);
            budgeted.System.Joypad.SetButtonState(button, pressed);
            reset.System.Joypad.SetButtonState(button, pressed);
            Compare(stepped, budgeted, 0, 0);
            Compare(stepped, reset, resetSequence, resetFrames);
            var pixels = new byte[VideoOutput.BufferSize];
            stepped.System.Video.CopyLatestFrame(pixels);
            var hash = Convert.ToHexStringLower(SHA256.HashData(pixels));
            images.Add(hash);
            output.WriteLine($"mbc1={mbc1} T={time} input={button}/{pressed} BGRA={hash}");
        }
        Assert.True(images.Count > 3, "The input replay must exercise changing images.");
        Assert.True(stepped.System.Video.CompletedFrameCount > 20);
        Assert.True(stepped.System.GetDebugSnapshot().ScrollX > 0);
    }

    private static void AdvanceTo(GameBoySystem system, ulong time, int chunk)
    {
        while (system.TotalTCycles < time)
        {
            Assert.True(system.RunForTCycles((int)Math.Min((ulong)chunk, time - system.TotalTCycles)).ExecutedTCycles > 0);
        }

        Assert.Equal(time, system.TotalTCycles);
    }

    private static void Compare((GameBoySystem System, ICartridge Cartridge) left,
        (GameBoySystem System, ICartridge Cartridge) right, ulong sequenceOffset, ulong frameOffset)
    {
        Assert.Equal(left.System.GetDebugSnapshot(), right.System.GetDebugSnapshot());
        byte[] a = new byte[65536], b = new byte[65536];
        left.System.CopyMemory(0, a);
        right.System.CopyMemory(0, b);
        Assert.Equal(a, b);
        a = new byte[VideoOutput.BufferSize];
        b = new byte[VideoOutput.BufferSize];
        var first = left.System.Video.CopyLatestFrame(a);
        var second = right.System.Video.CopyLatestFrame(b);
        Assert.Equal(first, second with { Sequence = second.Sequence - sequenceOffset });
        Assert.Equal(a, b);
        Assert.Equal(left.System.Video.CompletedFrameCount, right.System.Video.CompletedFrameCount - frameOffset);
        if (left.Cartridge is IBatteryBackedCartridge battery)
        {
            Assert.Equal(battery.ExportRam(), ((IBatteryBackedCartridge)right.Cartridge).ExportRam());
        }
    }

    [Fact]
    public void Mbc1DemoSavesInputPositionAndRestoresItIntoFreshCartridge()
    {
        byte[] image = ReadRom(true), seed = new byte[32768];
        seed[0] = 83;
        seed[1] = 19;
        var first = Start(image, seed);
        first.System.Joypad.SetButtonState(JoypadButton.Start, true);
        first.System.RunForTCycles(200_000);
        Assert.Equal(83, first.System.GetDebugSnapshot().ScrollX);
        Assert.Equal(19, first.System.GetDebugSnapshot().ScrollY);
        first.System.Joypad.SetButtonState(JoypadButton.Right, true);
        first.System.RunForTCycles(300_000);
        first.System.Joypad.ReleaseAll();
        first.System.Joypad.SetButtonState(JoypadButton.Start, true);
        first.System.RunForTCycles(100_000);
        var saved = ((IBatteryBackedCartridge)first.Cartridge).ExportRam();
        Assert.True(saved[0] > 83);
        Assert.Equal(first.System.GetDebugSnapshot().ScrollX, saved[0]);
        Assert.Equal(19, saved[1]);
        var restored = Start(image, saved);
        restored.System.Joypad.SetButtonState(JoypadButton.Start, true);
        restored.System.RunForTCycles(200_000);
        Assert.Equal(saved[0], restored.System.GetDebugSnapshot().ScrollX);
        Assert.Equal(saved[1], restored.System.GetDebugSnapshot().ScrollY);
        byte[] a = new byte[VideoOutput.BufferSize], b = new byte[VideoOutput.BufferSize];
        first.System.Video.CopyLatestFrame(a);
        restored.System.Video.CopyLatestFrame(b);
        Assert.Equal(a, b);
    }
}
