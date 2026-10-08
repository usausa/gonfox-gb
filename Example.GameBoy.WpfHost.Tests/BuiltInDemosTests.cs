namespace Example.GameBoy.WpfHost;

using GonFox.GameBoy.Core.Cartridge;

public sealed class BuiltInDemosTests
{
    // Checks that the demo list loads the committed ROMs in list order.
    [Fact]
    public void BuiltInDemosAreTheCommittedRomsInListOrder()
    {
        string[] files = ["background-demo.gb", "megademo.gb", "sound-check.gb", "rpgdemo.gb"];
        Assert.Equal(files.Length, BuiltInDemos.All.Length);
        Assert.Equal(["Background demo", "Mega demo", "Sound check", "RPG demo"], BuiltInDemos.All.Select(demo => demo.Name));
        for (var i = 0; i < files.Length; i++)
        {
            var image = BuiltInDemos.Load(BuiltInDemos.All[i].Resource);
            Assert.Equal(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, files[i])), image);
            Assert.Equal((byte)0, CartridgeLoader.Load(image).Info.TypeCode); // ROM Only, 32 KiB.
        }
        Assert.Throws<InvalidOperationException>(() => BuiltInDemos.Load("Missing.gb"));
    }
}
