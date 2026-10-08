namespace GonFox.GameBoy.Core;

using System.Security.Cryptography;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Devices;

[Trait("Category", "Rom")]
public sealed class StateRomTests
{
    [Theory]
    [InlineData("background-demo.gb", "9053de305c39aed9780a74057e1fc44e99d9cc8ac3a62b5eb7d4ccdd82e9c0f8")]
    [InlineData("mbc1-demo.gb", "2bdc7882666d81731b8a9936670049e8a4e1b8feee07f1f73ffa27f12bb8c812")]
    [InlineData("dmg-acid2/dmg-acid2.gb", "464e14b7d42e7feea0b7ede42be7071dc88913f75b9ffa444299424b63d1dff1")]
    public void MidFrameStateReplaysInputsAndCompleteImages(string rom, string sha256)
    {
        var image = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", rom));
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(image)));
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        system.RunForTCycles(1_000_000);
        Assert.NotEqual(0, system.GetDebugSnapshot().PpuDot);
        StateTests.ReplayTwice(system, s =>
        {
            s.Joypad.SetButtonState(JoypadButton.Start, true);
            s.RunForTCycles(4096);
            s.Joypad.SetButtonState(JoypadButton.Right, true);
            s.RunForTCycles(140_448);
            s.Joypad.ReleaseAll();
            s.RunForTCycles(140_448);
        });
    }
}
