namespace GonFox.GameBoy.Core;

// RPG demo battles: a player follows a command plan and a referee checks every event by the rules.
public sealed partial class RpgDemoTests
{
    private enum Command
    {
        Fight,
        Magic,
        Guard,
        Run,
        Switch
    }

    private const int Won = 1;
    private const int Lost = 2;
    private const int RanAway = 3;
    private const int SongBattle = 3;
    private const int SongVictory = 4;
    private const int SongGameOver = 5;

    // A routine the program reached, with register A, the random state and the observation block then.
    private sealed record Event(string Label, byte RegisterA, ushort Random, byte[] O);

    private sealed class Outcome
    {
        internal int Result { get; set; }
        internal int Turns { get; set; }
        internal int Criticals { get; set; }
        internal int Refused { get; set; }
        internal int Halved { get; set; }
        internal int Specials { get; set; }
        internal int Faints { get; set; }
        internal int Switches { get; set; }
        internal int Escapes { get; set; }
    }

    [Fact]
    public void EasyBattleFightsToVictoryAndRunAlwaysGetsAway()
    {
        var fight = PlayBattle(0, 0, [.. Enumerable.Repeat(Command.Fight, 8)], out var end);
        Assert.Equal(Won, fight.Result);
        Assert.Equal((0, SongVictory, Enemies[0][5]), (end[OEnemyHp], end[OSong], end[ONumber])); // The EXP gained.

        // GUARD at full HP recovers nothing; RUN gets away on EASY without a random number.
        var run = PlayBattle(7, 0, [Command.Guard, Command.Run], out end);
        Assert.Equal((RanAway, 1, 1), (run.Result, run.Escapes, run.Halved));
        Assert.Equal((SongBattle, 7), (end[OSong], end[OHero]));
        output.WriteLine($"EASY: won in {fight.Turns} enemy turns with {fight.Criticals} critical hits; HANA got away after {run.Turns} turn");
    }

    [Fact]
    public void NormalBattleSpendsMpRefusesMagicWithoutItGuardsSwitchesAndRunsOnAnEvenDraw()
    {
        Command[] plan = [Command.Magic, Command.Magic, Command.Magic, Command.Magic, Command.Guard, Command.Switch,
            .. Enumerable.Repeat(Command.Run, 8)];
        var outcome = PlayBattle(1, 1, plan, out var end);
        Assert.Equal((1, 1, 1), (outcome.Refused, outcome.Halved, outcome.Switches)); // The fourth MAGIC finds no MP.
        Assert.Equal((RanAway, 0, 2), (outcome.Result, end[OMp], end[OHero])); // PINA's MP is gone; SHIRO got away.
        output.WriteLine($"NORMAL: got away after {outcome.Turns} enemy turns, {outcome.Specials} special moves");
    }

    [Fact]
    public void NightmareDoublesEveryThirdAttackSendsOutTheNextHeroAndNeverLetsTheHeroRun()
    {
        Command[] plan = [Command.Run, Command.Guard, .. Enumerable.Repeat(Command.Fight, 40)];
        var outcome = PlayBattle(2, 2, plan, out var end);
        Assert.Equal(Won, outcome.Result);
        Assert.True(outcome.Specials >= 2 && outcome.Faints >= 1, $"{outcome.Specials} specials, {outcome.Faints} faints");
        Assert.Equal((0, Enemies[2][5]), (end[OEnemyHp], end[ONumber]));
        output.WriteLine($"NIGHTMARE: won in {outcome.Turns} enemy turns; {outcome.Specials} special moves, {outcome.Faints} heroes fainted");
    }

    [Fact]
    public void NightmareEndsInGameOverWhenEveryHeroFaints()
    {
        var outcome = PlayBattle(5, 2, [.. Enumerable.Repeat(Command.Run, 80)], out var end);
        Assert.Equal((Lost, 8, 0), (outcome.Result, outcome.Faints, outcome.Escapes));
        Assert.All(end[OHeroHp..(OHeroHp + 8)], hp => Assert.Equal(0, hp));
        Assert.Equal((SongGameOver, Enemies[2][1]), (end[OSong], end[OEnemyHp]));
        output.WriteLine($"NIGHTMARE: every hero down after {outcome.Turns} enemy turns, {outcome.Specials} special moves");
    }

    [Fact]
    public void StartPausesTheBattleAndItsMusicUntilStartAgain()
    {
        var system = StartBattle(0, 0);
        var player = new Player(system);
        var menu = player.Play(new Queue<Command>()); // Stops at the first command menu.
        RunFrames(system, 1);
        ushort row = Symbols.Value["wRow"], paused = Symbols.Value["wMusicPaused"];
        Frame(system, Start);
        byte[] music = Picture(system, row, 1), tempo = Picture(system, Symbols.Value["wTempoAcc"], 1);
        Assert.Equal(1, Picture(system, paused, 1)[0]);
        Assert.Equal(0, system.CaptureState().Apu.Registers[0x15]); // NR51 routes nothing: silence.
        for (var frame = 0; frame < 60; frame++)
        {
            Frame(system, 0);
        }

        Assert.Equal("- PAUSE -", MapText(system, 5, 14, 9));
        Assert.Equal((music[0], tempo[0]), (Picture(system, row, 1)[0], Picture(system, Symbols.Value["wTempoAcc"], 1)[0]));
        Assert.Equal(menu[OEnemyHp], Observe(system)[OEnemyHp]);
        Frame(system, A); // A does nothing while paused.
        for (var frame = 0; frame < 10; frame++)
        {
            Frame(system, 0);
        }

        Assert.Equal(1, Picture(system, paused, 1)[0]);
        Frame(system, Start);
        Assert.Equal(0, Picture(system, paused, 1)[0]);
        Assert.Equal(0xFF, system.CaptureState().Apu.Registers[0x15]);
        for (var frame = 0; frame < 4; frame++)
        {
            Frame(system, 0);
        }

        Assert.Equal("FIGHT    MAGIC", MapText(system, 3, 15, 14)); // The commands are back.
        Assert.Equal(">FIGHT", MapText(system, 2, 15, 6));
        Assert.True(Picture(system, row, 1)[0] != music[0] || Picture(system, Symbols.Value["wTempoAcc"], 1)[0] != tempo[0]); // The song goes on.
    }

    // Steps the program, recording events and pressing the buttons a player would, one mask per frame.
    private sealed class Player(GameBoySystem system)
    {
        private static readonly string[] Labels = ["RandomBelow", "CommandMenu", "DoFight", "DoMagic", "DoGuard", "DoRun",
            "DoSwitch", "SendOut", "EnemyTurn", "Victory", "Defeat"];
        private readonly Dictionary<ushort, string> hooks = Labels.ToDictionary(label => Symbols.Value[label]);
        private readonly ushort waitButton = Symbols.Value["WaitButton"];
        private readonly Queue<byte> buttons = new();
        internal List<Event> Events { get; } = [];

        // Plays until the plan runs out, the battle ends or stopSong plays; returns the observation block.
        internal byte[] Play(Queue<Command> plan, int stopSong = -1, int frameLimit = 20_000)
        {
            var first = system.Video.CompletedFrameCount;
            var commanded = false;
            (ushort Pc, ushort Sp) interrupted = default;
            while (true)
            {
                var cpu = system.GetDebugSnapshot();
                if (cpu.IsHalted && cpu.Ly < 142)
                {
                    system.RunForTCycles((142 - cpu.Ly) * 456);
                    continue;
                }
                if (cpu.PC == waitButton && buttons.Count == 0)
                {
                    buttons.Enqueue(A);
                    buttons.Enqueue(0);
                }
                if (hooks.TryGetValue(cpu.PC, out var label))
                {
                    if ((cpu.PC, cpu.SP) == interrupted)
                    {
                        interrupted = default; // Recorded before the interrupt came.
                    }
                    else
                    {
                        var o = Observe(system);
                        var random = Picture(system, Symbols.Value["hRandom"], 2);
                        Events.Add(new Event(label, cpu.A, (ushort)(random[0] | (random[1] << 8)), o));
                        if (label == "CommandMenu")
                        {
                            commanded = true;
                            if (plan.Count == 0)
                            {
                                return o;
                            }

                            foreach (var mask in Presses(o[OCursor], plan.Dequeue()))
                            {
                                buttons.Enqueue(mask);
                            }
                        }
                    }
                }
                var frames = system.Video.CompletedFrameCount;
                Assert.True(system.StepInstruction().ExecutedTCycles > 0);
                if (hooks.ContainsKey(cpu.PC) && system.GetDebugSnapshot().PC == 0x40)
                {
                    interrupted = (cpu.PC, cpu.SP);
                }

                if (system.Video.CompletedFrameCount == frames)
                {
                    continue;
                }

                Hold(system, buttons.Count > 0 ? buttons.Dequeue() : (byte)0);
                var now = Observe(system);
                if (commanded && (now[OResult] != 0 || now[OSong] == stopSong))
                {
                    return now;
                }

                Assert.True(system.Video.CompletedFrameCount - first < (ulong)frameLimit, "the battle did not end");
            }
        }

        // Presses that move the cursor to the command, in rows FIGHT MAGIC and GUARD RUN, then choose it.
        private static List<byte> Presses(int cursor, Command command)
        {
            if (command == Command.Switch)
            {
                return [Select, 0];
            }

            var target = (int)command;
            var presses = new List<byte>();
            if (((cursor ^ target) & 2) != 0)
            {
                presses.AddRange([(target & 2) != 0 ? Down : Up, 0]);
            }

            if (((cursor ^ target) & 1) != 0)
            {
                presses.AddRange([(target & 1) != 0 ? Right : Left, 0]);
            }

            presses.AddRange([A, 0]);
            return presses;
        }
    }

    private static Outcome PlayBattle(int hero, int mode, Command[] plan, out byte[] end)
    {
        var system = StartBattle(hero, mode);
        var player = new Player(system);
        end = player.Play(new Queue<Command>(plan));
        var outcome = Referee(player.Events, plan, hero, mode, end);

        // The battle shows the difficulty's enemy and the back of the hero last sent out.
        Assert.Equal(EnemyTiles(mode), Picture(system, 0x9000, 576));
        Assert.Equal(Rom("HeroBackTiles", 8 * 576)[(end[OHero] * 576)..((end[OHero] * 576) + 576)], Picture(system, 0x9240, 576));
        return outcome;
    }

    // The ROM's 16-bit xorshift; R(n) is the new state's high byte modulo n.
    private static ushort Xorshift(ushort x)
    {
        x ^= (ushort)(x << 7);
        x ^= (ushort)(x >> 9);
        x ^= (ushort)(x << 8);
        return x;
    }

    private static int NextAlive(int[] hp, int hero)
    {
        for (var step = 1; step < 8; step++)
        {
            if (hp[(hero + step) & 7] > 0)
            {
                return (hero + step) & 7;
            }
        }

        return -1;
    }

    // Replays the events by the battle rules and checks every draw, damage, HP, MP, turn and result.
    private static Outcome Referee(List<Event> events, Command[] plan, int hero, int mode, byte[] end)
    {
        var enemy = Enemies[mode];
        int enemyHp = enemy[1], mp = HeroMaxMp, turn = 0, planned = 0, index = 0, number = -1;
        int[] hp = [.. Enumerable.Repeat(HeroMaxHp, 8)];
        var guard = false;
        var outcome = new Outcome();

        Event Take(string label)
        {
            Assert.True(index < events.Count, $"{label} missing after event {index}");
            var e = events[index++];
            Assert.Equal(label, e.Label);
            return e;
        }
        int Draw(int n, Event? previous = null)
        {
            var e = Take("RandomBelow");
            Assert.Equal(n, e.RegisterA);
            if (previous is not null)
            {
                Assert.Equal(Xorshift(previous.Random), e.Random); // Drawn right after the other.
            }

            return (Xorshift(e.Random) >> 8) % n;
        }
        void SendOut(int next)
        {
            var e = Take("SendOut");
            Assert.Equal(next, e.RegisterA);
            hero = next;
        }

        while (index < events.Count)
        {
            var e = events[index++];
            switch (e.Label)
            {
                case "CommandMenu":
                    Assert.Equal((enemyHp, enemy[1], hero, mode), (e.O[OEnemyHp], e.O[OEnemyMaxHp], e.O[OHero], e.O[OMode]));
                    Assert.Equal(hp, e.O[OHeroHp..(OHeroHp + 8)].Select(value => (int)value));
                    Assert.Equal((mp, turn, 0, 0), (e.O[OMp], e.O[OTurn], e.O[OGuard], e.O[OResult]));
                    if (planned == plan.Length)
                    {
                        // The player stopped here.
                        Assert.Equal(events.Count, index);
                        break;
                    }

                    Assert.Equal("Do" + plan[planned++], events[index].Label); // The buttons chose the planned command.
                    break;
                case "DoFight":
                {
                    var damage = 8 + Draw(5);
                    if (Draw(8, events[index - 1]) == 0)
                    {
                        damage *= 2;
                        outcome.Criticals++;
                    }
                    enemyHp = Math.Max(0, enemyHp - damage);
                    number = damage;
                    break;
                }
                case "DoMagic":
                    if (mp == 0)
                    {
                        outcome.Refused++;
                        Assert.Equal("CommandMenu", events[index].Label);
                        break;
                    }
                    mp--;
                    number = 14 + Draw(7);
                    enemyHp = Math.Max(0, enemyHp - number);
                    break;
                case "DoGuard":
                    guard = true;
                    number = Math.Min(HeroMaxHp, hp[hero] + 4) - hp[hero];
                    hp[hero] += number;
                    break;
                case "DoRun":
                    number = -1;
                    if (mode == 0 || (mode == 1 && Draw(2) == 0))
                    {
                        outcome.Escapes++;
                        outcome.Result = RanAway;
                        Assert.Equal(events.Count, index); // Nothing happens after getting away.
                    }
                    break;
                case "DoSwitch":
                    outcome.Switches++;
                    number = -1;
                    SendOut(NextAlive(hp, hero));
                    break;
                case "EnemyTurn":
                {
                    Assert.Equal((enemyHp, guard ? 1 : 0), (e.O[OEnemyHp], e.O[OGuard]));
                    if (number >= 0)
                    {
                        Assert.Equal(number, e.O[ONumber]);
                    }

                    turn++;
                    var hit = enemy[2] + Draw(enemy[3]);
                    if (enemy[4] > 0 && turn % enemy[4] == 0)
                    {
                        hit *= 2;
                        outcome.Specials++;
                    }
                    if (guard)
                    {
                        hit = Math.Max(1, hit / 2);
                        outcome.Halved++;
                        guard = false;
                    }
                    hp[hero] = Math.Max(0, hp[hero] - hit);
                    number = hit;
                    if (hp[hero] > 0)
                    {
                        break;
                    }

                    outcome.Faints++;
                    var next = NextAlive(hp, hero);
                    if (next >= 0)
                    {
                        SendOut(next);
                        break;
                    }
                    Take("Defeat");
                    outcome.Result = Lost;
                    Assert.Equal(events.Count, index);
                    break;
                }
                case "Victory":
                    Assert.Equal((0, 0, number), (enemyHp, e.O[OEnemyHp], e.O[ONumber]));
                    outcome.Result = Won;
                    Assert.Equal(events.Count, index);
                    break;
                default:
                    Assert.Fail($"{e.Label} at event {index - 1} was not expected");
                    break;
            }
        }
        outcome.Turns = turn;
        Assert.Equal(outcome.Result, end[OResult]);
        Assert.Equal((enemyHp, turn, mp, hero), (end[OEnemyHp], end[OTurn], end[OMp], end[OHero]));
        Assert.Equal(hp, end[OHeroHp..(OHeroHp + 8)].Select(value => (int)value));
        Assert.Equal(outcome.Result switch { Won => SongVictory, Lost => SongGameOver, _ => SongBattle }, end[OSong]);
        if (outcome.Result == Won)
        {
            Assert.Equal(enemy[5], end[ONumber]); // The EXP message.
        }

        return outcome;
    }
}
