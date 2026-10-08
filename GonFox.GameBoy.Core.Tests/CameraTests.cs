namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;

// Game Boy Camera (type FC): banking, the registers, capture timing and image processing.
[Trait("Category", "Unit")]
public sealed class CameraTests
{
    private const int Width = 128;
    private const int Height = 112;

    // Loads a camera cartridge with ROM bank n filled with n (default 1 MiB ROM, 128 KiB RAM).
    private static ICartridge Load(byte romCode = 5, byte ramCode = 4, params byte[] program) =>
        CartridgeLoader.Load(TestRom.CreateMbc1(0xFC, romCode, ramCode, program)).Cartridge;

    // Loads a cartridge attached to a bare console clock.
    private static (ICartridge Cart, Clock Clock) Attached()
    {
        var cart = Load();
        var clock = new Clock();
        ((IClockedCartridge)cart).AttachClock(clock);
        return (cart, clock);
    }

    // Advances the clock as a run without cartridge accesses would.
    private static void Elapse(Clock clock, long tcycles) => clock.RestoreState(clock.TotalTCycles + (ulong)tcycles);

    // Writes the three thresholds of matrix element (x, y); the registers must be mapped.
    private static void Element(ICartridge cart, int x, int y, (int First, int Second, int Third) thresholds)
    {
        var at = 0xA006 + (((y * 4) + x) * 3);
        cart.Write((ushort)at, (byte)thresholds.First);
        cart.Write((ushort)(at + 1), (byte)thresholds.Second);
        cart.Write((ushort)(at + 2), (byte)thresholds.Third);
    }

    private static void Matrix(ICartridge cart, (int First, int Second, int Third) thresholds)
    {
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                Element(cart, x, y, thresholds);
            }
        }
    }

    // Thresholds that give the value v a shade no other value gets, and that shade.
    private static (int First, int Second, int Third) Probe(int v) => v < 255 ? (v, v + 1, 255) : (255, 255, 255);
    private static int ProbeShade(int v) => v < 255 ? 2 : 0;

    // Starts a capture with these registers (A000 last), lets it end and maps RAM bank 0.
    private static void Capture(ICartridge cart, Clock clock, byte a000, byte a001 = 0, int exposure = 0x300, byte a004 = 0)
    {
        cart.Write(0x4000, 0x10);
        cart.Write(0xA001, a001);
        cart.Write(0xA002, (byte)(exposure >> 8));
        cart.Write(0xA003, (byte)exposure);
        cart.Write(0xA004, a004);
        cart.Write(0xA000, a000);
        Elapse(clock, CameraCartridge.MaxCaptureTCycles);
        Assert.Equal(a000 & 6, cart.Read(0xA000)); // Ended.
        cart.Write(0x4000, 0);
    }

    private static byte[] Picture(ICartridge cart)
    {
        cart.Write(0x4000, 0);
        var picture = new byte[0xE00];
        for (var i = 0; i < picture.Length; i++)
        {
            picture[i] = cart.Read((ushort)(0xA100 + i));
        }

        return picture;
    }

    // Decodes the picture's tiles into one shade per pixel, at [y * 128 + x].
    private static int[] Shades(ICartridge cart)
    {
        var picture = Picture(cart);
        var shades = new int[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                int at = ((y >> 3) * 0x100) + ((x >> 3) * 16) + ((y & 7) * 2), bit = 7 - (x & 7);
                shades[(y * Width) + x] = ((picture[at] >> bit) & 1) | (((picture[at + 1] >> bit) & 1) << 1);
            }
        }

        return shades;
    }

    [Theory]
    [InlineData(5, 4, 0x100000, 0x20000)] // The camera board.
    [InlineData(0, 2, 0x8000, 0x2000)]
    [InlineData(3, 3, 0x40000, 0x8000)]
    [InlineData(4, 5, 0x80000, 0x10000)]
    public void TheLoaderAcceptsTheCameraWithItsBatteryAndBankOne(byte romCode, byte ramCode, int romBytes, int ramBytes)
    {
        var image = TestRom.CreateMbc1(0xFC, romCode, ramCode);
        var loaded = CartridgeLoader.Load(image);
        Array.Fill(image, (byte)0xFF);
        Assert.Equal(new CartridgeInfo("P01 TEST", 0xFC, romBytes, ramBytes, HasBattery: true), loaded.Info);
        Assert.Empty(loaded.Warnings);
        Assert.True(loaded.Cartridge is IBatteryBackedCartridge and ICameraCartridge and IStatefulCartridge { TypeCode: 0xFC });
        Assert.Equal(1, loaded.Cartridge.Read(0x4000));
        Assert.Equal(0, loaded.Cartridge.Read(0x3FFF));
        Assert.Equal(ramBytes, ((IBatteryBackedCartridge)loaded.Cartridge).ExportRam().Length);
    }

    [Theory]
    [InlineData(6, 4)] // 2 MiB.
    [InlineData(5, 0)] // No RAM.
    [InlineData(5, 1)]
    [InlineData(5, 6)]
    public void TheLoaderRejectsSizesTheCameraCannotHave(byte romCode, byte ramCode) =>
        Assert.Throws<CartridgeLoadException>(() => CartridgeLoader.Load(TestRom.CreateMbc1(0xFC, romCode, ramCode)));

    // 2000-3FFF selects the 4000-7FFF bank with six bits, bank 0 included.
    [Fact]
    public void RomBankIsSixBitsWithBankZero()
    {
        var cart = Load();
        for (var value = 0; value < 256; value++)
        {
            cart.Write((ushort)(0x2000 + ((value * 37) % 0x2000)), (byte)value);
            Assert.Equal(value & 0x3F, cart.Read(0x4000));
            Assert.Equal(value & 0x3F, cart.Read(0x7FFF));
            Assert.Equal(0, cart.Read(0x0000));
            Assert.Equal(0, cart.Read(0x3FFF));
        }
        cart.Write(0x1FFF, 0x0A);
        cart.Write(0x5FFF, 0x10);
        cart.Write(0x6000, 0x3E);
        cart.Write(0x7FFF, 0x01); // Not 2000-3FFF.
        Assert.Equal(0x3F, cart.Read(0x4000));
        var small = Load(1, 2); // 64 KiB ROM.
        small.Write(0x2000, 0x3E);
        Assert.Equal(2, small.Read(0x4000));
    }

    // RAM always reads; a low nibble of $A at 0000-1FFF enables writes.
    [Theory]
    [InlineData(0x0A, true)]
    [InlineData(0x1A, true)]
    [InlineData(0xFA, true)]
    [InlineData(0x00, false)]
    [InlineData(0x0B, false)]
    [InlineData(0xA0, false)]
    public void RamIsAlwaysReadableAndWritableAfterA(byte value, bool writable)
    {
        var cart = Load();
        cart.Write(0, 0x0A);
        cart.Write(0xA000, 42);
        cart.Write(0, 0);
        Assert.Equal(42, cart.Read(0xA000));
        cart.Write(0x1000, value);
        cart.Write(0xA000, 7);
        Assert.Equal(writable ? 7 : 42, cart.Read(0xA000));
    }

    // 4000-5FFF selects the RAM bank with its low four bits.
    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 4)]
    [InlineData(5, 8)]
    [InlineData(4, 16)]
    public void RamBankIsFourBitsAndWrapsAtTheRamSize(byte ramCode, int banks)
    {
        var cart = Load(5, ramCode);
        cart.Write(0, 0x0A);
        for (var bank = 0; bank < 16; bank++)
        {
            cart.Write(0x5FFF, (byte)(0xE0 | bank));
            cart.Write(0xA000, (byte)(bank + 1));
            cart.Write(0xBFFF, (byte)(bank + 101));
        }
        for (var bank = 0; bank < 16; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            var last = (bank % banks) + 16 - banks; // The last write to this bank.
            Assert.Equal(last + 1, cart.Read(0xA000));
            Assert.Equal(last + 101, cart.Read(0xBFFF));
        }
    }

    // Mapped registers read 0 except A000 bits 0-2, and writes to them leave RAM alone.
    [Fact]
    public void RegistersAreMappedByBitFourAndMirroredEvery80H()
    {
        var cart = Load();
        cart.Write(0, 0x0A);
        cart.Write(0xA000, 0x11);
        cart.Write(0xA001, 0x33);
        cart.Write(0xBF80, 0x22);
        cart.Write(0, 0);
        foreach (var select in new byte[] { 0x10, 0x1F, 0x30, 0xF5 })
        {
            cart.Write(0x4000, select);
            cart.Write(0xA000, 0xFE); // Bit 0 clear starts nothing.
            for (var mirror = 0xA000; mirror < 0xC000; mirror += 0x80)
            {
                Assert.Equal(0x06, cart.Read((ushort)mirror));
                for (var index = 1; index < 0x80; index++)
                {
                    Assert.Equal(0, cart.Read((ushort)(mirror + index)));
                }
            }
            for (var index = 1; index < 0x80; index++)
            {
                cart.Write((ushort)(0xA000 + index), 0xFF);
            }

            Assert.Equal(0, cart.Read(0xA001));
            cart.Write(0xBF80, 0x02);
            Assert.Equal(0x02, cart.Read(0xA000));
            Assert.Equal(0x02, cart.Read(0xA480));
        }
        cart.Write(0x4000, 0x00);
        Assert.Equal((0x11, 0x33, 0x22), (cart.Read(0xA000), cart.Read(0xA001), cart.Read(0xBF80)));
    }

    // A000 bit 0 stays set for exactly the capture time; the registers are written via mirrors.
    [Theory]
    [InlineData(0x80, 0x00, 0x00, 32446)]
    [InlineData(0x00, 0x00, 0x00, 32958)] // N clear.
    [InlineData(0x9F, 0x00, 0x30, 33214)] // Gain bits do not count.
    [InlineData(0x60, 0x03, 0x00, 45246)] // VH bits do not count.
    [InlineData(0x80, 0xFF, 0xFF, 1081006)] // The longest.
    public void ACaptureTakesTheDocumentedTime(byte a001, byte a002, byte a003, int mCycles)
    {
        var (cart, clock) = Attached();
        cart.Write(0x4000, 0x10);
        cart.Write(0xBF81, a001);
        cart.Write(0xA102, a002);
        cart.Write(0xB383, a003);
        cart.Write(0xA080, 0x01);
        Elapse(clock, (4L * mCycles) - 1);
        Assert.Equal(0x01, cart.Read(0xA000));
        Elapse(clock, 1);
        Assert.Equal(0x00, cart.Read(0xA000));
        Elapse(clock, 4L * mCycles);
        Assert.Equal(0x00, cart.Read(0xA000));
    }

    // A capture keeps its start parameters, and its picture fills only A100-AEFF of RAM bank 0.
    [Fact]
    public void WhileCapturingRamReadsZeroAndIgnoresWrites()
    {
        var (cart, clock) = Attached();
        cart.Write(0, 0x0A);
        for (var bank = 0; bank < 16; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            foreach (var address in new ushort[] { 0xA000, 0xA0FF, 0xA100, 0xAEFF, 0xAF00, 0xBFFF })
            {
                cart.Write(address, (byte)(bank + 1));
            }
        }
        cart.Write(0x4000, 0x10);
        cart.Write(0xA001, 0x80); // N: 32446 M-cycles.
        Matrix(cart, (0x81, 0xFF, 0xFF)); // Black.
        cart.Write(0xA000, 0x05);
        Elapse(clock, 4 * 30000);
        Assert.Equal(0x05, cart.Read(0xA000));
        Assert.Equal(0, cart.Read(0xA001));
        Assert.Equal(0, cart.Read(0xA006));
        cart.Write(0xA002, 0xFF);
        Matrix(cart, (0, 0, 0)); // Too late for this capture.
        for (var bank = 0; bank < 16; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            Assert.Equal(0, cart.Read(0xA000));
            Assert.Equal(0, cart.Read(0xA100));
            Assert.Equal(0, cart.Read(0xBFFF));
            cart.Write(0xA000, 0xEE);
            cart.Write(0xBFFF, 0xEE);
        }
        cart.Write(0x4000, 0x10);
        Elapse(clock, (4 * 2446) - 1);
        Assert.Equal(0x05, cart.Read(0xA000));
        Elapse(clock, 1);
        Assert.Equal(0x04, cart.Read(0xA000));
        for (var bank = 0; bank < 16; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            var picture = bank == 0 ? 0xFF : bank + 1;
            Assert.Equal((bank + 1, bank + 1, picture, picture, bank + 1, bank + 1),
                (cart.Read(0xA000), cart.Read(0xA0FF), cart.Read(0xA100), cart.Read(0xAEFF), cart.Read(0xAF00), cart.Read(0xBFFF)));
        }
        Assert.All(Picture(cart), value => Assert.Equal(0xFF, value));
    }

    // Stopped, a capture frees RAM and counts no time; it continues with its old parameters.
    [Fact]
    public void WritingZeroStopsACaptureAndOneContinuesIt()
    {
        var (cart, clock) = Attached();
        cart.Write(0x4000, 0x10);
        Matrix(cart, (0x80, 0x80, 0x80));
        cart.Write(0xA000, 0x03); // 32958 M-cycles, black.
        Elapse(clock, 4 * 10000);
        cart.Write(0xA000, 0x00);
        Assert.Equal(0x00, cart.Read(0xA000));
        cart.Write(0, 0x0A);
        cart.Write(0x4000, 0);
        cart.Write(0xA100, 0x42);
        Assert.Equal(0x42, cart.Read(0xA100));
        Elapse(clock, 4 * 1_000_000);
        cart.Write(0x4000, 0x10);
        Matrix(cart, (0, 0, 0));
        cart.Write(0xA001, 0x80);
        cart.Write(0xA002, 0xFF);
        cart.Write(0xA000, 0x01); // Continues with the old parameters.
        Elapse(clock, (4 * 22958) - 1);
        Assert.Equal(0x01, cart.Read(0xA000));
        Elapse(clock, 1);
        Assert.Equal(0x00, cart.Read(0xA000));
        Assert.All(Picture(cart), value => Assert.Equal(0xFF, value));
        Capture(cart, clock, 0x01, 0x80, 0xFF00); // A new capture: white.
        Assert.All(Picture(cart), value => Assert.Equal(0x00, value));
    }

    // A known image under the simplest settings gives the documented tile bytes at A100-AEFF.
    [Fact]
    public void AKnownImageGivesTheDocumentedTiles()
    {
        var (cart, clock) = Attached();
        var image = new byte[Width * Height];
        Array.Fill(image, (byte)255);
        image[0] = 128;
        image[(17 * Width) + 9] = 0;
        image[(111 * Width) + 127] = 64;
        ((ICameraCartridge)cart).SetImage(image);
        cart.Write(0, 0x0A);
        for (var address = 0xA000; address < 0xC000; address++)
        {
            cart.Write((ushort)address, 0x5A);
        }

        cart.Write(0x4000, 0x10);
        Matrix(cart, (120, 128, 136));
        cart.Write(0xA002, 0x03);
        cart.Write(0xA000, 0x03);
        Elapse(clock, (4 * (32958 + (16 * 0x300))) - 1);
        Assert.Equal(0x03, cart.Read(0xA000));
        Elapse(clock, 1);
        Assert.Equal(0x02, cart.Read(0xA000));
        var expected = new byte[0x2000];
        Array.Fill(expected, (byte)0x5A);
        expected.AsSpan(0x100, 0xE00).Clear();
        expected[0x100] = 0x80; // (0, 0): shade 1.
        expected[0x312] = expected[0x313] = 0x40; // (9, 17): shade 3.
        expected[0xEFF] = 0x01; // (127, 111): shade 2.
        cart.Write(0x4000, 0);
        var actual = new byte[0x2000];
        for (var i = 0; i < actual.Length; i++)
        {
            actual[i] = cart.Read((ushort)(0xA000 + i));
        }

        Assert.Equal(expected, actual);
    }

    // Without an image the sensor sees the same flat grey in every capture of every cartridge.
    [Fact]
    public void WithoutAnImageTheSensorSeesGreyAndTheMatrixIsSortedByRows()
    {
        (int, int, int)[] thresholds = [(0x80, 0x80, 0x80), (0x80, 0x80, 0x81), (0x80, 0x81, 0xFF), (0x81, 0xFF, 0xFF)];
        byte[] Develop(ICartridge cart, Clock clock)
        {
            cart.Write(0x4000, 0x10);
            for (var y = 0; y < 4; y++)
            {
                for (var x = 0; x < 4; x++)
                {
                    Element(cart, x, y, thresholds[(x + (2 * y)) & 3]);
                }
            }

            Capture(cart, clock, 0x03);
            return Picture(cart);
        }
        var (first, firstClock) = Attached();
        var picture = Develop(first, firstClock);
        var tile = Convert.FromHexString("553355CC553355CC553355CC553355CC");
        for (var t = 0; t < 224; t++)
        {
            Assert.Equal(tile, picture[(t * 16)..((t * 16) + 16)]);
        }

        Assert.Equal(picture, Develop(first, firstClock));
        var (second, secondClock) = Attached();
        Assert.Equal(picture, Develop(second, secondClock));
        var camera = (ICameraCartridge)second;
        camera.SetImage(new byte[Width * Height]);
        Assert.NotEqual(picture, Develop(second, secondClock));
        camera.ClearImage();
        Assert.Equal(picture, Develop(second, secondClock));
        var grey = new byte[Width * Height];
        Array.Fill(grey, (byte)128);
        camera.SetImage(grey);
        Assert.Equal(picture, Develop(second, secondClock));
        Assert.Throws<ArgumentException>(() => camera.SetImage(new byte[(Width * Height) - 1]));
        Assert.Equal(picture, Develop(second, secondClock));
    }

    // A flat image goes through the sensor's exposure scaling, clamp and optional inversion.
    [Theory]
    [InlineData(200, 0x0300, false, 137)]
    [InlineData(200, 0x0180, false, 125)]
    [InlineData(200, 0x0600, false, 162)]
    [InlineData(200, 0x0000, false, 112)]
    [InlineData(200, 0xFFFF, false, 255)] // Clamped.
    [InlineData(119, 0x0300, false, 127)] // Truncated toward zero.
    [InlineData(127, 0x0300, false, 128)] // Truncated toward zero.
    [InlineData(136, 0x0300, false, 129)]
    [InlineData(0, 0x0300, true, 143)]
    [InlineData(255, 0x0300, true, 112)]
    public void TheSensorScalesByExposureIntoItsOutputRange(byte luminance, int exposure, bool invert, int expected)
    {
        var (cart, clock) = Attached();
        var image = new byte[Width * Height];
        Array.Fill(image, luminance);
        ((ICameraCartridge)cart).SetImage(image);
        cart.Write(0x4000, 0x10);
        Matrix(cart, Probe(expected));
        Capture(cart, clock, 0x03, 0x00, exposure, invert ? (byte)0x08 : (byte)0);
        Assert.All(Shades(cart), shade => Assert.Equal(ProbeShade(expected), shade));
    }

    // One bright pixel on grey: the values at it, its four neighbours and far off, per mode.
    [Theory]
    [InlineData(0x03, 0x00, 0x00, 143, 128, 128, 128, 128, 128)] // 1-D +P.
    [InlineData(0x03, 0x1F, 0x00, 143, 128, 128, 128, 128, 128)] // Gain ignored.
    [InlineData(0x01, 0x00, 0x00, 113, 128, 128, 128, 128, 128)] // -P.
    [InlineData(0x05, 0x00, 0x00, 143, 128, 128, 113, 128, 128)] // P - below.
    [InlineData(0x03, 0x00, 0x08, 112, 127, 127, 127, 127, 127)] // Inverted.
    [InlineData(0x03, 0x00, 0x80, 128, 128, 128, 128, 128, 128)] // Mode 1: blank.
    [InlineData(0x03, 0x20, 0x20, 173, 128, 128, 128, 128, 128)] // Mode 2, alpha 1.
    [InlineData(0x05, 0x20, 0x20, 173, 128, 128, 83, 128, 128)] // Mode 2, then P - below.
    [InlineData(0x03, 0xE0, 0x00, 173, 121, 121, 121, 121, 128)] // Mode E, alpha 0.5.
    [InlineData(0x03, 0xE0, 0x20, 203, 113, 113, 113, 113, 128)] // Mode E, alpha 1.
    [InlineData(0x03, 0xE0, 0x70, 255, 53, 53, 53, 53, 128)] // Mode E, alpha 5.
    [InlineData(0x03, 0xE0, 0xA0, 188, 113, 113, 113, 113, 128)] // Mode F.
    [InlineData(0x03, 0xC0, 0x20, 173, 128, 128, 113, 113, 128)] // Mode C.
    [InlineData(0x03, 0xC0, 0xA0, 158, 128, 128, 113, 113, 128)] // Mode D.
    [InlineData(0x03, 0x20, 0xA0, 158, 128, 128, 128, 128, 128)] // Mode 3.
    [InlineData(0x03, 0x40, 0x20, 143, 128, 128, 128, 128, 128)] // Mode 4: passed on.
    [InlineData(0x03, 0xA0, 0x20, 143, 128, 128, 128, 128, 128)] // Mode A: passed on.
    public void EdgeModesFollowTheSampleCode(byte a000, byte a001, byte a004, int centre, int west, int east, int north, int south, int far)
    {
        var (cart, clock) = Attached();
        var image = new byte[Width * Height];
        Array.Fill(image, (byte)128);
        image[(5 * Width) + 5] = 255;
        ((ICameraCartridge)cart).SetImage(image);
        (int X, int Y, int Value)[] probes = [(5, 5, centre), (4, 5, west), (6, 5, east), (5, 4, north), (5, 6, south), (7, 7, far)];
        cart.Write(0x4000, 0x10);
        foreach (var (x, y, value) in probes) Element(cart, x & 3, y & 3, Probe(value));
        Capture(cart, clock, a000, a001, 0x300, a004);
        var shades = Shades(cart);
        Assert.Equal(probes.Select(p => ProbeShade(p.Value)), probes.Select(p => shades[(p.Y * Width) + p.X]));
    }

    [Fact]
    public void ACaptureEndsOnTimeWhileTheCpuHalts()
    {
        var cart = Load(5, 4, 0xF3, 0x76); // DI; HALT.
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        system.RunForTCycles(100);
        Assert.True(system.IsHalted);
        cart.Write(0x4000, 0x10);
        cart.Write(0xA001, 0x80);
        cart.Write(0xA000, 0x01); // 32446 M-cycles.
        system.RunForTCycles((4 * 32446) - 4);
        Assert.Equal(0x01, cart.Read(0xA000));
        system.RunForTCycles(4);
        Assert.Equal(0x00, cart.Read(0xA000));
        Assert.True(system.IsHalted);
    }

    // A program polls A000 until its capture ends, then copies the first picture byte to WRAM.
    [Fact]
    public void AProgramWaitsForTheCaptureAndReadsItsPicture()
    {
        var cart = Load(5, 4,
            0x3E, 0x10, 0xEA, 0x00, 0x40, // LD A,$10; LD ($4000),A
            0x21, 0x06, 0xA0, 0x06, 0x30, 0x3E, 0x81, // LD HL,$A006; LD B,48; LD A,$81
            0x22, 0x05, 0x20, 0xFC, // LD (HL+),A; DEC B; JR NZ,-4
            0x3E, 0x81, 0xEA, 0x01, 0xA0, 0x3E, 0x01, 0xEA, 0x00, 0xA0, // A001 = $81; A000 = 1
            0xFA, 0x00, 0xA0, 0xE6, 0x01, 0x20, 0xF9, // LD A,($A000); AND 1; JR NZ,-7
            0xAF, 0xEA, 0x00, 0x40, 0xFA, 0x00, 0xA1, 0xEA, 0x00, 0xC0, 0xF3, 0x76); // RAM bank 0; ($C000) = ($A100); DI; HALT
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        system.RunForTCycles(4 * 32446);
        Assert.False(system.IsHalted); // Still polling.
        system.RunForTCycles(10_000);
        Assert.True(system.IsHalted);
        var memory = new byte[1];
        system.CopyMemory(0xC000, memory);
        Assert.Equal(0xFF, memory[0]);
    }

    [Fact]
    public void StateKeepsTheCaptureAndRejectsInvalidCameraFields()
    {
        var cart = Load(5, 4, 0xF3, 0x76);
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        system.RunForTCycles(100);
        cart.Write(0, 0x0A);
        for (var bank = 0; bank < 16; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            cart.Write(0xBFFF, (byte)(bank + 31));
        }
        cart.Write(0x2000, 0xEA);
        cart.Write(0x4000, 0xF0); // Registers mapped.
        cart.Write(0xA001, 0x80);
        Matrix(cart, (0x81, 0xFF, 0xFF)); // Black.
        cart.Write(0xA000, 0x07);
        system.RunForTCycles(40_000);
        var saved = system.CaptureState();
        var camera = saved.Cartridge.Camera!;
        var remaining = (4 * 32446) - 40_000;
        Assert.Equal((0x2A, 0x10, true), (saved.Cartridge.Bank, saved.Cartridge.RamBank, saved.Cartridge.RamEnabled));
        Assert.Equal((0x07, 0x80, 0x81, 0xFF, false, remaining),
            (camera.Registers[0], camera.Registers[1], camera.Registers[6], camera.Registers[0x35], camera.Paused, camera.Remaining));
        Assert.All(camera.Picture, value => Assert.Equal(0xFF, value));

        system.RunForTCycles(200_000); // The capture ends.
        cart.Write(0x4000, 0);
        Assert.Equal(0xFF, cart.Read(0xA100));
        cart.Write(0x2000, 3);
        cart.Write(0, 0);
        cart.Write(0x4000, 0x10);
        cart.Write(0xA000, 0x02);
        cart.Write(0xA001, 0);
        system.RestoreState(saved);
        Assert.Equal(0x2A, cart.Read(0x4000));
        Assert.Equal(0x07, cart.Read(0xA000)); // Capturing again.
        system.RunForTCycles(remaining - 4);
        Assert.Equal(0x07, cart.Read(0xA000));
        system.RunForTCycles(4);
        Assert.Equal(0x06, cart.Read(0xA000));
        cart.Write(0x4000, 0);
        Assert.Equal(0xFF, cart.Read(0xA100));
        cart.Write(0xA000, 0x99);
        Assert.Equal(0x99, cart.Read(0xA000)); // RAM writes enabled again.
        for (var bank = 0; bank < 16; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            Assert.Equal(bank + 31, cart.Read(0xBFFF));
        }

        cart.Write(0x4000, 3);
        byte[] With(IEnumerable<byte> array, int index, byte value)
        {
            var copy = array.ToArray();
            copy[index] = value;
            return copy;
        }
        GameBoyState Changed(CameraState? changed) => saved with { Cartridge = saved.Cartridge with { Camera = changed } };
        foreach (var invalid in new[]
        {
            saved with { Cartridge = saved.Cartridge with { Bank = 0x40 } },
            saved with { Cartridge = saved.Cartridge with { RamBank = 0x20 } },
            saved with { Cartridge = saved.Cartridge with { Upper = 1 } },
            saved with { Cartridge = saved.Cartridge with { Mode = 1 } },
            saved with { Cartridge = saved.Cartridge with { Ram = new byte[0x8000] } },
            saved with { Cartridge = saved.Cartridge with { Rtc = new RtcState(default, default, 0, false) } },
            Changed(null),
            Changed(camera with { Registers = new byte[0x35] }),
            Changed(camera with { Registers = With(camera.Registers, 0, 0x0F) }), // A000 has three bits.
            Changed(camera with { Picture = new byte[0xDFF] }),
            Changed(camera with { Remaining = 0 }), // Capturing with no time left.
            Changed(camera with { Remaining = CameraCartridge.MaxCaptureTCycles + 1 }),
            Changed(camera with { Paused = true }), // Capturing and stopped.
            Changed(camera with { Registers = With(camera.Registers, 0, 0x06) }), // Neither, with time left.
            saved with { FormatVersion = 6 }
        })
        {
            Assert.Throws<ArgumentException>(() => system.RestoreState(invalid));
            Assert.Equal(3, system.GetDebugSnapshot().RamBank); // The rejected state changed nothing.
        }

        // A stopped capture is kept and continues with the time it had left.
        cart.Write(0x4000, 0x10);
        cart.Write(0xA000, 0x01);
        system.RunForTCycles(1000);
        cart.Write(0xA000, 0x00);
        var stopped = system.CaptureState();
        Assert.Equal((0x00, true, (4 * 32446) - 1000), (stopped.Cartridge.Camera!.Registers[0], stopped.Cartridge.Camera.Paused, stopped.Cartridge.Camera.Remaining));
        cart.Write(0xA000, 0x01);
        system.RunForTCycles(200_000);
        Assert.Equal(0x00, cart.Read(0xA000));
        system.RestoreState(stopped);
        Assert.Equal(0x00, cart.Read(0xA000));
        system.RunForTCycles(200_000);
        cart.Write(0xA000, 0x01);
        system.RunForTCycles((4 * 32446) - 1000 - 4);
        Assert.Equal(0x01, cart.Read(0xA000));
        system.RunForTCycles(4);
        Assert.Equal(0x00, cart.Read(0xA000));
    }

    // Reset maps ROM bank 1, disables RAM writes and drops a capture's picture; RAM is kept.
    [Fact]
    public void ConsoleResetEndsTheCaptureAndClearsTheRegisters()
    {
        var cart = Load(5, 4, 0xF3, 0x76);
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        cart.Write(0, 0x0A);
        cart.Write(0xA100, 0x42);
        cart.Write(0x2000, 7);
        cart.Write(0x4000, 0x10);
        cart.Write(0xA001, 0x80);
        Matrix(cart, (0x81, 0xFF, 0xFF)); // Black.
        cart.Write(0xA000, 0x07);
        system.Reset();
        Assert.Equal(1, cart.Read(0x4000));
        Assert.Equal(0x42, cart.Read(0xA100));
        cart.Write(0xA100, 0x43);
        Assert.Equal(0x42, cart.Read(0xA100)); // Writes disabled.
        system.RunForTCycles((4 * 32446) + 100);
        Assert.Equal(0x42, cart.Read(0xA100)); // No picture comes later.
        cart.Write(0x4000, 0x10);
        Assert.Equal(0x00, cart.Read(0xA000));

        // A capture stopped before a Reset does not continue: the next start is new.
        cart.Write(0xA001, 0x80);
        Matrix(cart, (0x81, 0xFF, 0xFF));
        cart.Write(0xA000, 0x01);
        system.RunForTCycles(400);
        cart.Write(0xA000, 0x00);
        system.Reset();
        cart.Write(0x4000, 0x10);
        cart.Write(0xA000, 0x01);
        system.RunForTCycles((4 * 32958) - 4);
        Assert.Equal(0x01, cart.Read(0xA000));
        system.RunForTCycles(4);
        Assert.Equal(0x00, cart.Read(0xA000));
        Assert.All(Picture(cart), value => Assert.Equal(0x00, value));
    }

    // ExportRam first finishes a capture whose time has passed.
    [Fact]
    public void TheBatteryKeepsEveryBankWithAPictureThatHasEnded()
    {
        var (cart, clock) = Attached();
        var battery = (IBatteryBackedCartridge)cart;
        var data = new byte[0x20000];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i / 0x2000) + 10);
        }

        battery.ImportRam(data);
        cart.Write(0x4000, 0x10);
        cart.Write(0xA001, 0x80);
        Matrix(cart, (0x81, 0xFF, 0xFF));
        cart.Write(0xA000, 0x01);
        Elapse(clock, 4 * 32446);
        var exported = battery.ExportRam();
        Assert.Equal(data[..0x100], exported[..0x100]);
        Assert.Equal(data[0xF00..], exported[0xF00..]);
        Assert.All(exported[0x100..0xF00], value => Assert.Equal(0xFF, value));
        Assert.Throws<ArgumentException>(() => battery.ImportRam(new byte[0x8000]));
        Assert.Equal(exported, battery.ExportRam());
    }
}
