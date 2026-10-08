namespace GonFox.GameBoy.Core.Video;

using System.Runtime.CompilerServices;

using GonFox.GameBoy.Core.Devices;

// DMG PPU, timed in dots from the LY change; Mode 3 drawing is in Ppu.Pipeline.cs.
internal sealed partial class Ppu(Interrupts interrupts, VideoOutput video)
{
    // An object found by the OAM scan, kept in drawing order: smaller X first, then lower OAM index.
    internal readonly record struct LineSprite(byte X, byte Y, byte Tile, byte Flags);

    private const int Unscheduled = int.MaxValue;
    private readonly byte[] vram = new byte[0x2000];
    private readonly byte[] oam = new byte[160];
    private readonly LineSprite[] sprites = new LineSprite[10];
    private int spriteCount;
    private int scanIndex;
    private readonly byte[] lineShades = new byte[VideoOutput.Width];

    // Scratch buffers of the whole-line drawing (a scrolled line spans 21 tiles), never saved.
    private readonly byte[] colors = new byte[VideoOutput.Width];
    private readonly byte[] objColors = new byte[VideoOutput.Width];
    private readonly byte[] objFlags = new byte[VideoOutput.Width];
    private readonly byte[] tileRows = new byte[VideoOutput.Width + 16];
    private static readonly ulong[] BitSpread = CreateBitSpread();
    private Pipeline pipe = Idle(); // Committed up to pipe.Dot.
    private Pipeline ahead; // pipe run to the line's end.
    private bool windowYTriggered;
    private bool dmaActive;
    private bool wxJustChanged;
    private bool windowEnableHeld;
    private bool lcdOnLine; // Line 0 begun by LCD enable.
    private byte lcdc;
    private byte stat;
    private byte scy;
    private byte scx;
    private byte lyc;
    private byte bgp;
    private byte obp0;
    private byte obp1;
    private byte wy;
    private byte wx;

    // SCX and LYC as the fine scroll and LY=LYC see them, a dot after a write; never saved.
    private byte scxFine;
    private byte lycCompared;

    // LCD off freezes these three.
    private bool statLine;
    private bool lycFlag;
    private bool lycSignal;
    private byte line; // Scan line 0-153, not LY.

    // Scheduled dots: Tick's next work, mode 0, the HBlank source and a written LYC; never saved.
    private int eventDot;
    private int mode3End = Unscheduled;
    private int hblankDot = Unscheduled;
    private int lycDot = Unscheduled;

    // A copy that validation runs saved pipelines on, never reaching interrupts or video output.
    private Ppu? probe;
    internal byte Ly => line == 153 && Dot >= 3 ? (byte)0 : line;
    internal byte Mode { get; private set; }
    internal int Dot { get; private set; }
    private bool Enabled => (lcdc & 0x80) != 0;

    // Mode 3 after the pipeline start: PPU register writes reach the pixels.
    private bool Drawing => Mode == 3 && Dot >= StartDot && line < 144 && Enabled;

    internal sealed record State(byte[] Vram, byte[] Oam, LineSprite[] Sprites, int SpriteCount, int ScanIndex,
        bool WindowYTriggered, bool DmaActive, byte Lcdc, byte Stat, byte Scy, byte Scx, byte Lyc,
        byte Bgp, byte Obp0, byte Obp1, byte Wy, byte Wx, bool StatLine, bool LycFlag, bool LycSignal,
        byte Line, byte Mode, int Dot, bool LcdOnLine, Pipeline Pipe, byte[] LineShades);

    internal State CaptureState() => new(vram.ToArray(), oam.ToArray(), sprites.ToArray(), spriteCount, scanIndex,
        windowYTriggered, dmaActive, lcdc, stat, scy, scx, lyc, bgp, obp0, obp1, wy, wx,
        statLine, lycFlag, lycSignal, line, Mode, Dot, lcdOnLine, pipe, lineShades.ToArray());

    internal void ValidateState(State? state)
    {
        StateValidation.Require(state is not null, "PPU");
        StateValidation.Length(state.Vram, 0x2000, "VRAM");
        StateValidation.Length(state.Oam, 160, "OAM");
        StateValidation.Length(state.Sprites, 10, "line sprites");
        StateValidation.Length(state.LineShades, VideoOutput.Width, "line shades");
        var enabled = (state.Lcdc & 0x80) != 0;
        int savedLine = state.Line, dot = state.Dot, compare = CompareLine(savedLine, dot);
        var p = state.Pipe;
        var start = state.LcdOnLine ? LcdOnPipelineStart : PipelineStart;
        var drawingLine = enabled && savedLine < 144 && dot >= start;
        StateValidation.Require(savedLine < 154 && dot is >= 0 and < 456 && (enabled || (savedLine == 0 && dot == 0)) &&
            (state.Stat & ~0x78) == 0 && state.SpriteCount is >= 0 and <= 10 && DrawingOrder(state.Sprites, state.SpriteCount) &&
            state.ScanIndex is >= 0 and <= 40 &&
            !state.LineShades.AsSpan().ContainsAnyExceptInRange((byte)0, (byte)3) &&
            p.Fetcher <= 6 && p.BgCount <= 8 && p.ObjCount <= 8 && p.LcdX is >= 0 and <= VideoOutput.Width &&
            p.WindowTileX < 32 && p.TileAddress < 0x2000 && p.DataAddress < 0x2000 &&
            (!state.LcdOnLine || (enabled && savedLine == 0 && state.SpriteCount == 0)) &&
            (p.Step == StepDone ? p.Position == -16 : p.Step < StepDone && drawingLine && p.Dot >= start &&
                p.Dot <= dot + 2 && p.Position is >= -16 and < 160 && p.LcdX <= Math.Max(p.Position, 0) &&
                p.NextObject >= 0 && p.NextObject <= state.SpriteCount && (p.LastPixelDot == 0 || p.Position == 159)) &&
            (p.LastPixelDot == 0 || (p.LastPixelDot >= start && p.LastPixelDot <= p.Dot)) &&
            (!p.LateWindowStart || (enabled && savedLine < 144 && dot >= 84)), "PPU pipeline");

        // Runs a copy of the saved pipeline to find where mode 0 and the HBlank source begin.
        int expectedMode3End = Unscheduled, expectedHblankDot = Unscheduled;
        if (drawingLine)
        {
            probe ??= new Ppu(interrupts, video);
            probe.Load(state);
            probe.Speculate();
            expectedMode3End = probe.mode3End;
            expectedHblankDot = probe.hblankDot;
        }

        // Expected mode: line start 0, OAM scan 2, drawing 3, HBlank 0; VBlank 1 from Dot 2 of line 144.
        var mode = !enabled ? 0 : savedLine >= 144 ? (savedLine == 144 && dot < 2 ? 0 : 1)
            : dot < 2 ? 0 : dot < 84 ? 2 : dot < expectedMode3End ? 3 : 0;
        var firstLine = state.LcdOnLine && dot is >= 4 and < 84 && state.Mode == 0; // No OAM scan after LCD on.
        StateValidation.Require((state.Mode == mode || firstLine) && (!drawingLine || expectedMode3End <= LastDot) &&
            (enabled ? state.LycFlag == (compare == state.Lyc) && (compare < 0 || state.LycSignal == state.LycFlag) &&
                state.StatLine == StatLevel(state.Stat, state.LycSignal, savedLine, dot, state.Mode, expectedHblankDot)
                : state.StatLine == ((state.Stat & 0x40) != 0 && state.LycSignal)), "PPU phase"); // LCD off: the LYC source alone.
    }

    private static bool DrawingOrder(LineSprite[] sprites, int count)
    {
        for (var i = 1; i < count; i++)
        {
            if (sprites[i - 1].X > sprites[i].X)
            {
                return false;
            }
        }

        return true;
    }

    internal void RestoreState(State state)
    {
        Load(state);
        if (Enabled && line < 144 && Dot >= StartDot)
        {
            Speculate();
        }
        else
        {
            mode3End = hblankDot = Unscheduled;
        }

        eventDot = NextEventDot();
    }

    private void Load(State state)
    {
        state.Vram.CopyTo(vram, 0);
        state.Oam.CopyTo(oam, 0);
        state.Sprites.CopyTo(sprites, 0);
        spriteCount = state.SpriteCount;
        scanIndex = state.ScanIndex;
        windowYTriggered = state.WindowYTriggered;
        dmaActive = state.DmaActive;
        lcdc = state.Lcdc;
        stat = state.Stat;
        scy = state.Scy;
        scx = scxFine = state.Scx;
        lyc = lycCompared = state.Lyc;
        lycDot = Unscheduled;
        bgp = state.Bgp;
        obp0 = state.Obp0;
        obp1 = state.Obp1;
        wy = state.Wy;
        wx = state.Wx;
        statLine = state.StatLine;
        lycFlag = state.LycFlag;
        lycSignal = state.LycSignal;
        line = state.Line;
        Mode = state.Mode;
        Dot = state.Dot;
        lcdOnLine = state.LcdOnLine;
        pipe = state.Pipe;
        state.LineShades.CopyTo(lineShades, 0);
    }

    internal void Tick()
    {
        if (!Enabled || ++Dot != eventDot)
        {
            return;
        }

        RunEventDot();
    }

    // Dots before the next one with work; all of them while the LCD is off.
    internal int QuietTCycles => !Enabled ? int.MaxValue : Math.Max(0, eventDot - Dot - 1);

    internal void Skip(int dots)
    {
        if (Enabled)
        {
            Dot += dots;
        }
    }

    // Runs the work scheduled at this dot; kept out of the inlined per-dot Tick.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RunEventDot()
    {
        if (Dot == lycDot)
        {
            CompareWrittenLyc();
        }

        if (Dot == 456)
        {
            Dot = 0;
            StartLine();
        }
        else if (Dot == 2)
        {
            EnterLineMode();
        }
        else if (line < 144)
        {
            // The line begun by enabling the LCD has no OAM scan, so it draws no objects.
            if (Dot == 84)
            {
                if (!lcdOnLine)
                {
                    ScanOam(int.MaxValue);
                }
                Mode = 3;
            }
            else if (Dot == StartDot)
            {
                StartPipeline();
            }
            else if (Dot == mode3End)
            {
                EndMode3();
            }
            else
            {
                UpdateStat(); // hblankDot, or the dot after an LYC write.
            }
        }
        else
        {
            UpdateStat(); // Line 153 at Dot 6 and 10.
        }

        eventDot = NextEventDot();
    }

    // The next dot with work: 2, the drawing events, 6 and 10 on line 153, 456, or an LYC compare.
    private int NextEventDot()
    {
        var next = Dot < 2 ? 2 : line < 144 ? NextDrawingEvent() : line == 153 ? (Dot < 6 ? 6 : Dot < 10 ? 10 : 456) : 456;
        return lycDot < next ? lycDot : next;
    }

    private int NextDrawingEvent()
    {
        if (Dot < 84)
        {
            return 84;
        }

        if (Dot < StartDot)
        {
            return StartDot;
        }

        int first = Math.Min(mode3End, hblankDot), second = Math.Max(mode3End, hblankDot);
        return Dot < first ? first : Dot < second ? second : 456;
    }

    // Dot 0: outputs the finished line and moves to the next; LY=LYC is withheld until Dot 2.
    private void StartLine()
    {
        if (line < 144)
        {
            video.WriteLine(line, lineShades);
        }

        line = (byte)(line == 153 ? 0 : line + 1);
        mode3End = hblankDot = Unscheduled;
        lcdOnLine = false;
        if (line == 0)
        {
            Mode = 0;
            windowYTriggered = false;
        }
        else if (line < 144 && pipe.HoldWindow && pipe.WindowActive)
        {
            // A WX=166 window held from the line before counts another window line.
            pipe.WindowY++;
        }

        if (line < 144)
        {
            BeginLine();
        }

        UpdateStat();
    }

    // Dot 2: OAM scan or VBlank begins and LY=LYC is compared.
    private void EnterLineMode()
    {
        if (line == 144)
        {
            // LY=LYC is compared before mode 1 begins, with the HBlank source and the OAM source's VBlank pulse.
            UpdateStat(oamPulse: true);
            Mode = 1;
            video.CompleteFrame();
            interrupts.Request(InterruptSource.VBlank);
        }
        else if (line < 144)
        {
            Mode = 2;
            CheckWindowY();
        }
        if (line == 153)
        {
            UpdateStat();
            return;
        }
        UpdateStat(oamPulse: line is 0 or 144);
        if (line is 0 or 144)
        {
            UpdateStat();
        }
    }

    private void BeginLine()
    {
        spriteCount = scanIndex = 0;
    }

    // Latches the window's Y condition (WY equals the line, window enabled) until the frame ends.
    private void CheckWindowY()
    {
        if (line < 144 && wy == line && (lcdc & 0x20) != 0)
        {
            windowYTriggered = true;
        }
    }

    private void StartPipeline()
    {
        ref var p = ref pipe;
        p.Dot = StartDot;
        p.Step = StepIteration;
        p.Fetcher = 0;
        p.NoInsertionGlitch = false;
        p.BgLow = p.BgHigh = 0;
        p.BgCount = 8; // Eight junk pixels.
        p.ObjLow = p.ObjHigh = p.ObjPalette = p.ObjPriority = p.ObjCount = 0;
        p.LcdX = 0;
        p.NextObject = 0;
        p.ObjectFetch = p.Aborted = false;
        p.LastPixelDot = 0; // Ends a WX=166 hold.
        Speculate();
    }

    // Runs the committed pipeline ahead to the end of the line, in one go when possible.
    private void Speculate()
    {
        ahead = pipe;
        if (!TryDrawWholeLine(ref ahead))
        {
            Run(ref ahead, int.MaxValue);
        }

        mode3End = ahead.Dot;
        hblankDot = (ahead.LastPixelDot != 0 ? ahead.LastPixelDot : mode3End) + 1;
    }

    // Mode 0 begins: the line's pipeline becomes final.
    private void EndMode3()
    {
        pipe = ahead;
        Mode = 0;
    }

    // Runs the committed pipeline up to the dot where a Mode 3 register write takes effect.
    private void Commit(int dot) => Run(ref pipe, dot);

    // Runs ahead again after a Mode 3 write, beginning mode 0 now if the write ended Mode 3 earlier.
    private void Respeculate()
    {
        Speculate();
        if (mode3End <= Dot)
        {
            EndMode3();
        }

        if (hblankDot <= Dot)
        {
            UpdateStat();
        }

        eventDot = NextEventDot();
    }

    // OAM DMA writes through its own port, ignoring the CPU's access restrictions.
    internal void WriteOamDma(int index, byte value) => oam[index] = value;

    // While OAM DMA runs, the OAM scan reads the entries it reaches as off screen.
    internal void SetDmaActive(bool active)
    {
        if (active != dmaActive && Enabled && Mode == 2)
        {
            ScanOam(Dot + 1);
        }

        dmaActive = active;
    }

    // When the CPU is locked out of OAM and VRAM; VRAM writes lock only while drawing.
    private bool OamReadLocked => Mode >= 2 || (line < 144 && Dot < 4);
    private bool OamWriteLocked => Mode == 3 || (Mode == 2 && Dot < 80);
    private bool VramReadLocked => Mode == 3 || (Mode == 2 && Dot >= 80);

    internal byte ReadMemory(ushort address)
    {
        if (!Enabled)
        {
            return PeekMemory(address);
        }

        if (address < 0xA000)
        {
            return VramReadLocked ? (byte)0xFF : vram[address - 0x8000];
        }

        if (!OamReadLocked)
        {
            return oam[address - 0xFE00];
        }

        if (OamWriteLocked)
        {
            CorruptOamByRead();
        }

        return 0xFF;
    }

    // FEA0-FEFF reads 00, or FF while OAM is locked; during the OAM scan it corrupts OAM.
    internal byte ReadUnusable()
    {
        if (!Enabled || !OamReadLocked)
        {
            return 0x00;
        }

        if (OamWriteLocked)
        {
            CorruptOamByRead();
        }

        return 0xFF;
    }

    internal void WriteUnusable()
    {
        if (Enabled && OamWriteLocked)
        {
            CorruptOamByWrite();
        }
    }

    internal byte PeekMemory(ushort address) => address < 0xA000
        ? vram[address - 0x8000] : oam[address - 0xFE00];

    internal void WriteMemory(ushort address, byte value)
    {
        if (address < 0xA000)
        {
            if (!Enabled || Mode != 3)
            {
                vram[address - 0x8000] = value;
            }
        }
        else if (!Enabled || !OamWriteLocked)
        {
            if (Mode == 2)
            {
                ScanOam(Dot); // Passed entries keep what was read.
            }

            oam[address - 0xFE00] = value;
        }
        else
        {
            CorruptOamByWrite();
        }
    }

    // The OAM row a CPU access would corrupt now (the DMG OAM bug), or 0 for none.
    private int CorruptedRow => Mode == 2 && Dot is >= 4 and < 80 && !dmaActive && Enabled ? Dot >> 2 : 0;

    private int OamWord(int offset) => oam[offset] | (oam[offset + 1] << 8);

    private void SetOamWord(int offset, int value)
    {
        oam[offset] = (byte)value;
        oam[offset + 1] = (byte)(value >> 8);
    }

    // Corrupts OAM like a write or a 16-bit increment/decrement during the OAM scan.
    internal void CorruptOamByWrite()
    {
        var row = CorruptedRow;
        if (row == 0)
        {
            return;
        }

        int at = row * 8, a = OamWord(at), b = OamWord(at - 8), c = OamWord(at - 4);
        SetOamWord(at, ((a ^ c) & (b ^ c)) ^ c);
        for (var i = 2; i < 8; i++)
        {
            oam[at + i] = oam[at - 8 + i];
        }
    }

    // Corrupts OAM like a read during the OAM scan; some rows also overwrite rows further back.
    private void CorruptOamByRead()
    {
        var row = CorruptedRow;
        if (row == 0)
        {
            return;
        }

        var at = row * 8;
        if ((row & 3) == 2)
        {
            int a = OamWord(at - 16), b = OamWord(at - 8), c = OamWord(at), d = OamWord(at - 4);
            SetOamWord(at - 8, (b & (a | c | d)) | (a & c & d));
            for (var i = 0; i < 8; i++)
            {
                oam[at - 16 + i] = oam[at - 8 + i];
            }
        }
        else if ((row & 3) == 0)
        {
            int a = OamWord(at), b = OamWord(at - 4), c = OamWord(at - 8), d = OamWord(at - 16), e = OamWord(at - 32);
            var value = row switch
            {
                4 => (c & (a | b | d | e)) | (a & b & d & e),
                8 => (c & (e | d | (~OamWord(at - 6) & OamWord(at - 14)) | b | a)) | (b & d & e),
                12 => (c & (a | b | d | e)) | (b & d & e),
                _ => c | (a & b & d & e)
            };
            SetOamWord(at - 8, value);
            for (var i = 0; i < 8; i++)
            {
                oam[at - 16 + i] = oam[at - 32 + i] = oam[at - 8 + i];
            }
        }
        else
        {
            var value = OamWord(at - 8) | (OamWord(at) & OamWord(at - 4));
            SetOamWord(at - 8, value);
        }
        for (var i = 0; i < 8; i++)
        {
            oam[at + i] = oam[at - 8 + i];
        }

        if (row == 16)
        {
            for (var i = 0; i < 8; i++)
            {
                oam[i] = oam[at + i];
            }
        }
    }

    internal byte ReadRegister(ushort address) => address switch
    {
        0xFF40 => lcdc,
        0xFF41 => (byte)(0x80 | stat | (lycFlag ? 4 : 0) | Mode),
        0xFF42 => scy,
        0xFF43 => scx,
        0xFF44 => Ly,
        0xFF45 => lyc,
        0xFF47 => bgp,
        0xFF48 => obp0,
        0xFF49 => obp1,
        0xFF4A => wy,
        0xFF4B => wx,
        _ => 0xFF
    };

    // Writes a PPU register; while drawing, writes that reach the pixels go to WriteWhileDrawing.
    internal void WriteRegister(ushort address, byte value)
    {
        if (Drawing && address is 0xFF40 or 0xFF42 or 0xFF43 or (>= 0xFF47 and <= 0xFF4B) &&
            (address != 0xFF40 || ((lcdc ^ value) & 0x80) == 0))
        {
            WriteWhileDrawing(address, value);
            return;
        }
        switch (address)
        {
            case 0xFF40:
                // Entries the OAM scan has passed keep the old OBJ size.
                if (Mode == 2 && ((lcdc ^ value) & 4) != 0)
                {
                    ScanOam(Dot);
                }

                if (((lcdc ^ value) & 0x20) != 0)
                {
                    HoldWindowStart((value & 0x20) != 0);
                }

                var wasEnabled = Enabled;
                if (lycDot != Unscheduled && (value & 0x80) == 0)
                {
                    CompareWrittenLyc(); // Seen before the LCD stops.
                }

                lcdc = value;
                if (wasEnabled == Enabled)
                {
                    if (Enabled)
                    {
                        CheckWindowY();
                    }
                    break;
                }

                // Switching the LCD resets to line 0; turning it on starts at Dot 4 without an OAM scan.
                line = 0; Dot = Enabled ? 4 : 0; Mode = 0;
                windowYTriggered = false; spriteCount = 0; pipe = Idle(); lcdOnLine = Enabled;
                mode3End = hblankDot = Unscheduled; eventDot = NextEventDot();
                if (Enabled)
                {
                    BeginLine();
                    CheckWindowY();
                }
                video.SetLcdEnabled(Enabled);
                UpdateStat();
                break;
            case 0xFF41:
                // DMG: every source is enabled for the write's first T-cycle, then the value applies.
                if (line is > 0 and < 144 && Dot < 2 && (stat & 0x08) != 0)
                {
                    statLine = true;
                }

                stat = 0x78; UpdateStat();
                stat = (byte)(value & 0x78); UpdateStat();
                break;
            case 0xFF42: scy = value; break;
            case 0xFF43: scx = scxFine = value; break;
            case 0xFF44: break; // LY is read-only.
            case 0xFF45:
                // LY=LYC uses the new LYC from the next dot; with the LCD off, the result stays frozen.
                lyc = value;
                if (Enabled)
                {
                    lycDot = Dot + 1;
                    eventDot = NextEventDot();
                }
                else
                {
                    lycCompared = value;
                    UpdateStat();
                }
                break;
            case 0xFF47: bgp = value; break;
            case 0xFF48: obp0 = value; break;
            case 0xFF49: obp1 = value; break;
            case 0xFF4A:
                wy = value; if (Enabled)
                {
                    CheckWindowY();
                }
                break;
            case 0xFF4B: wx = value; break;
        }
    }

    private void WriteWhileDrawing(ushort address, byte value)
    {
        switch (address)
        {
            case 0xFF40:
            {
                // The dot before sees a mix of the old and new LCDC; the write's dot sees the new value.
                Commit(Dot - 1);
                var old = lcdc;
                var first = (value & 0x5C) | (old & value & 0x20) | ((old | value) & 0x01) | (old & 0x82);
                if (pipe.Position == 0)
                {
                    first &= value | 0xFC;
                }

                if (pipe.ObjectFetch)
                {
                    first &= value | 0xFD;
                }

                SetLcdcWhileDrawing((byte)first, Dot - 1);
                windowEnableHeld = (old & ~value & 0x20) != 0;
                Commit(Dot);
                windowEnableHeld = false;
                SetLcdcWhileDrawing(value, Dot);
                CheckWindowY();
                break;
            }
            case 0xFF4A: Commit(Dot + 3); wy = value; CheckWindowY(); break;
            case 0xFF42: Commit(Dot - 1); scy = value; break;
            case 0xFF43: Commit(Dot - 1); scx = value; Commit(Dot); scxFine = value; break;
            case 0xFF4B:
                Commit(Dot + 1);
                wx = value; wxJustChanged = true;
                Commit(Dot + 2);
                wxJustChanged = false;
                break;
            default: // BGP, OBP0, OBP1.
                Commit(Dot - 1);
                SetPalette(address, pipe.Position == 0 ? value : (byte)(ReadRegister(address) | value));
                Commit(Dot);
                SetPalette(address, value);
                break;
        }
        Respeculate();
    }

    // Sets LCDC while drawing; clearing OBJ display aborts an object fetch in progress.
    private void SetLcdcWhileDrawing(byte value, int dot)
    {
        if ((lcdc & ~value & 2) != 0 && pipe.ObjectFetch && pipe.Step != StepDone)
        {
            pipe.Aborted = true;
            pipe.Dot = dot;
        }
        if ((lcdc & ~value & 0x20) != 0 && pipe.WindowFetching)
        {
            pipe.NoInsertionGlitch = true;
        }

        lcdc = value;
    }

    private void SetPalette(ushort address, byte value)
    {
        if (address == 0xFF47)
        {
            bgp = value;
        }
        else if (address == 0xFF48)
        {
            obp0 = value;
        }
        else
        {
            obp1 = value;
        }
    }

    // Starts or stops the window of a WX=166 match held from the line's end as its enable changes.
    private void HoldWindowStart(bool enable)
    {
        if (pipe.Step != StepDone || !pipe.HoldWindow)
        {
            return;
        }

        if (!enable)
        {
            pipe.WindowActive = false;
            return;
        }
        if (pipe.WindowActive)
        {
            return;
        }

        pipe.WindowActive = true;
        pipe.WindowTileX = 1;
        if (Mode == 3)
        {
            pipe.LateWindowStart = true;
        }
        else
        {
            pipe.WindowY++;
        }
    }

    private void CompareWrittenLyc()
    {
        lycDot = Unscheduled;
        lycCompared = lyc;
        UpdateStat();
    }

    // The line LY=LYC compares at a dot, or -1 for none; line 153 compares 153, none, then 0.
    private static int CompareLine(int line, int dot) => line switch
    {
        0 => 0,
        153 => dot < 2 ? -1 : dot < 6 ? 153 : dot < 10 ? -1 : 0,
        _ => dot < 2 ? -1 : line
    };

    // The STAT line level; HBlank stays a source into line 144 and VBlank into line 0 until Dot 2.
    private static bool StatLevel(byte stat, bool lycSignal, int line, int dot, byte mode, int hblankDot) =>
        ((stat & 0x40) != 0 && lycSignal) || ((stat & 0x20) != 0 && line is > 0 and <= 144 && dot < 2) ||
        ((stat & 0x10) != 0 && (mode == 1 || (line == 0 && dot < 2))) ||
        ((stat & 0x08) != 0 && (line < 144 ? dot >= hblankDot : line == 144 && mode == 0));

    private void UpdateStat(bool oamPulse = false)
    {
        if (!Enabled)
        {
            // LCD off: only the frozen LYC source drives the STAT line.
            var lycSource = (stat & 0x40) != 0 && lycSignal;
            if (lycSource && !statLine)
            {
                interrupts.Request(InterruptSource.LcdStat);
            }

            statLine = lycSource;
            return;
        }
        var compare = CompareLine(line, Dot);
        lycFlag = compare == lycCompared;
        if (compare >= 0)
        {
            lycSignal = lycFlag; // Held while nothing is compared.
        }

        var level = StatLevel(stat, lycSignal, line, Dot, Mode, hblankDot) || (oamPulse && (stat & 0x20) != 0);
        if (level && !statLine)
        {
            interrupts.Request(InterruptSource.LcdStat);
        }

        statLine = level; // All sources share one line.
    }

    // Scans the OAM entries checked before untilDot (entry i at Dot 2 + 2i) for up to ten objects.
    private void ScanOam(int untilDot)
    {
        // Works on locals: the whole scan runs every line.
        int index = scanIndex, count = spriteCount, end = Math.Min(40, (untilDot - 1) >> 1);
        if (dmaActive)
        {
            if (index < end)
            {
                scanIndex = end; // Off screen during DMA.
            }

            return;
        }
        var height = (lcdc & 4) != 0 ? 16 : 8;
        for (; index < end && count < 10; index++)
        {
            var i = index * 4;
            if ((uint)(line + 16 - oam[i]) >= (uint)height)
            {
                continue;
            }

            var sprite = new LineSprite(oam[i + 1], oam[i], oam[i + 2], oam[i + 3]);
            var j = count++;
            for (; j > 0 && sprites[j - 1].X > sprite.X; j--)
            {
                sprites[j] = sprites[j - 1]; // Equal X keeps OAM order.
            }

            sprites[j] = sprite;
        }
        scanIndex = index;
        spriteCount = count;
    }

    internal void Reset()
    {
        Array.Clear(vram);
        Array.Clear(oam);
        Array.Clear(lineShades);
        lcdc = DmgBootProfile.LcdControl;
        bgp = DmgBootProfile.BackgroundPalette;
        obp0 = obp1 = 0xFF;
        stat = scy = scx = scxFine = lyc = lycCompared = wy = wx = 0;
        line = DmgBootProfile.PpuLine;
        Dot = DmgBootProfile.PpuDot;
        Mode = 1;
        statLine = false;
        mode3End = hblankDot = lycDot = Unscheduled;
        eventDot = NextEventDot();
        windowYTriggered = false;
        dmaActive = false;
        pipe = Idle();
        lcdOnLine = false;
        BeginLine(); // No objects yet.
        video.SetLcdEnabled(true, showFirstFrame: true);
        UpdateStat(); // LY=LYC=0.
    }

    // Before a boot ROM: the LCD off and BGP 0.
    internal void PowerOn()
    {
        lcdc = bgp = 0;
        line = 0;
        Dot = 0;
        Mode = 0;
        mode3End = hblankDot = lycDot = Unscheduled;
        eventDot = NextEventDot();
        video.SetLcdEnabled(false);
        UpdateStat();
    }
}
