namespace GonFox.GameBoy.Core;

using System.Security.Cryptography;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Video;

[Trait("Category", "Rom")]
public sealed class BackgroundDemoTests
{
    private static GameBoySystem Start()
    {
        var image = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "background-demo.gb"));
        Assert.Equal("9053de305c39aed9780a74057e1fc44e99d9cc8ac3a62b5eb7d4ccdd82e9c0f8", Convert.ToHexStringLower(SHA256.HashData(image)));
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        return system;
    }

    private static void NextFrame(GameBoySystem system)
    {
        var target = system.Video.CompletedFrameCount + 1;
        var limit = system.TotalTCycles + 5_000_000;
        while (system.Video.CompletedFrameCount < target && system.TotalTCycles < limit)
        {
            Assert.True(system.StepInstruction().ExecutedTCycles > 0);
        }

        Assert.Equal(target, system.Video.CompletedFrameCount);
    }

    private static byte[] Pixels(GameBoySystem system)
    {
        var pixels = new byte[VideoOutput.BufferSize];
        system.Video.CopyLatestFrame(pixels);
        return pixels;
    }

    [Fact]
    public void RealCpuProgramProducesExpectedBackgroundWithoutHostReconstruction()
    {
        var system = Start();
        system.Joypad.SetButtonState(JoypadButton.Start, true);

        // Skips the first frame after the LCD turns on, which is not shown.
        NextFrame(system);
        NextFrame(system);
        var pixels = Pixels(system);
        byte[] levels = [255, 170, 85, 0];
        for (var y = 0; y < 144; y++)
        {
            for (var x = 0; x < 160; x++)
            {
                var shade = ((x % 8) + (y % 8) - (x / 8)) & 3;
                var offset = (y * 640) + (x * 4);
                Assert.Equal(levels[shade], pixels[offset]);
                Assert.Equal(pixels[offset], pixels[offset + 1]);
                Assert.Equal(pixels[offset], pixels[offset + 2]);
                Assert.Equal(255, pixels[offset + 3]);
            }
        }
    }

    [Fact]
    public void JoypadChangesScrollAndBackgroundThroughNormalRomIo()
    {
        var system = Start();
        system.Joypad.SetButtonState(JoypadButton.Start, true);
        NextFrame(system);
        system.RunForTCycles(2048);
        Assert.Equal(0, system.GetDebugSnapshot().ScrollX);
        system.Joypad.SetButtonState(JoypadButton.Right, true);
        NextFrame(system);
        system.RunForTCycles(2048);
        Assert.Equal(1, system.GetDebugSnapshot().ScrollX);
        system.Joypad.SetButtonState(JoypadButton.Right, false);
        system.Joypad.SetButtonState(JoypadButton.B, true);
        NextFrame(system);
        system.RunForTCycles(2048);
        NextFrame(system);
        Assert.All(Pixels(system), value => Assert.Equal(255, value));
        system.Joypad.SetButtonState(JoypadButton.B, false);
        system.Joypad.SetButtonState(JoypadButton.Select, true);
        system.RunForTCycles(2048);
        NextFrame(system);
        system.RunForTCycles(2048);
        Assert.Equal(0, system.GetDebugSnapshot().ScrollX);
    }

    [Fact]
    public void InstructionAndBudgetExecutionProduceIdenticalStateAndPixels()
    {
        var stepped = Start();
        var budgeted = Start();
        while (stepped.TotalTCycles < 300_000)
        {
            stepped.StepInstruction();
        }

        budgeted.RunForTCycles((int)stepped.TotalTCycles);
        Assert.Equal(stepped.GetDebugSnapshot(), budgeted.GetDebugSnapshot());
        Assert.Equal(Pixels(stepped), Pixels(budgeted));
        byte[] left = new byte[65536], right = new byte[65536];
        stepped.CopyMemory(0, left);
        budgeted.CopyMemory(0, right);
        Assert.Equal(left, right);
        var before = stepped.GetDebugSnapshot();
        for (var i = 0; i < 120; i++)
        {
            Pixels(stepped);
        }

        Assert.Equal(before, stepped.GetDebugSnapshot());
    }
}
