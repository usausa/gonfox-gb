# ソースの読み方

GonFox.GameBoyのソースを読むための道案内である。どのファイルに何があり、どこから読めばよいかをまとめる。挙動の定義は[現行仕様](Specification.md)と[CPU命令表](CpuInstructions.md)、検証の方法と結果は[互換性と検証](Compatibility.md)にある。

## 全体の構成

| 場所 | 中身 | 読むとき |
| --- | --- | --- |
| `GonFox.GameBoy.Core` | エミュレーター本体：CPU・バス・時間・周辺回路・PPU・APU・カートリッジ・状態の保存。外部パッケージを使わず、スレッド・実時計・ファイルを持たない | エミュレーションそのもの |
| `GonFox.GameBoy.Platform.Shared` | ホストの共通部品：実行スレッド、ROMの読み込みと電池セーブ、状態の枠、画像と音声の受け渡し | アプリに組み込むとき |
| `GonFox.GameBoy.Platform.Windows`・`GonFox.GameBoy.Platform.Android` | WASAPI・AudioTrackの音声出力、ファイルの置き場所、設定、触覚と振動 | プラットフォームごとの部分 |
| `Example.GameBoy.WpfHost`・`Example.GameBoy.MauiHost` | WPFとMAUI（Android）のサンプル | 画面と入力のつなぎ方 |
| `*.Tests` | xunit.v3のテスト | 挙動の確かめ方 |
| `Benchmark` | BenchmarkDotNetの計測 | 性能 |
| `Roms` | 自作ROM（デモ・テストROM集）と公開テストROM | テストの入力 |

参照は一方向である。Coreはどのプロジェクトも参照せず、Platform.SharedはCoreを、Platform.WindowsとPlatform.AndroidはPlatform.Sharedを、サンプルはそれらを参照する。

## 最初に読むファイル

1. [README](../README.md)のUsage：Coreの使い方。ROMを入れて時間を進め、画像と音をコピーで受け取る。
2. [GameBoySystem.cs](../GonFox.GameBoy.Core/GameBoySystem.cs)：公開の入口。コンストラクターが全ブロックを作ってつなぐので、部品の関係がここで分かる。`RunForTCycles`・`StepInstruction`・`Reset`・`CaptureState`・`RestoreState`。
3. [Clock.cs](../GonFox.GameBoy.Core/Clock.cs)：時間の進め方。CPUのM-cycleごとにTimer・OAM DMA・PPUをT-cycle単位で進め、何も起きない区間はまとめて進める。
4. [Cpu/Sm83Cpu.cs](../GonFox.GameBoy.Core/Cpu/Sm83Cpu.cs)：`Run`から`StepInstruction`、命令の`switch`へ。メモリーアクセスごとの`ReadCycle`・`WriteCycle`・`IdleCycle`がClockを1 M-cycle進めるため、命令の時間はアクセスの並びで決まる。割り込みの受理（`ServiceInterrupt`）、HALT（`WaitHalted`）、STOP（`Stop`）もここにある。
5. [Memory/MemoryBus.cs](../GonFox.GameBoy.Core/Memory/MemoryBus.cs)：アドレスマップ（`ReadMapped`・`WriteByte`）、OAM DMA中の読み出し（`ReadByte`）、Boot ROMの重ね合わせ、デバッグ用の`PeekByte`。

ここまでで全体の流れが分かる。後は知りたいブロックへ進む（[目的別の読み方](#目的別の読み方)）。

## 時間の単位と1回の実行の流れ

- T-cycle：4,194,304 Hzの基本の時間（`GameBoySystem.CyclesPerSecond`）。APIの時間（`TotalTCycles`・`RunForTCycles`）はすべてT-cycleで数える。
- M-cycle：CPUの1回のメモリーアクセスの時間で、4 T-cycle。
- dot：PPUの1 T-cycle。1ラインは456 dot、1フレームは154ライン（70,224 T-cycle）。

```text
GameBoySystem.RunForTCycles(budget)
 ├ Sm83Cpu.Run ─ 命令ごとにStepInstruction（割り込みの受理・HALT・STOPを含む）
 │   └ ReadCycle / WriteCycle / IdleCycle ─ MemoryBus ─ 各ブロックのレジスター
 │       └ Clock.AdvanceMachineCycle
 │           ├ Timer.Tick ─ DIVの立下り：TIMAの増加、Serial.ClockEdge、Apu.ClockFrameSequencer
 │           ├ OamDma.Tick ─ 1 M-cycleに1 byteをOAMへ
 │           └ Ppu.Tick ─ 仕事のあるdotだけ処理し、描き終えたラインをVideoOutput.WriteLineへ
 └ Apu.Synchronize ─ APUを今の時刻まで進め、AudioMixerが48 kHzのPCMをAudioOutputへ書く
ホスト ← Video.CopyLatestFrame（完成した最新の画像） / Audio.ReadFrames（PCM）
```

速さのための仕組みが3つあり、読むときに知っておくと迷わない。

- **何も起きない区間の一括の進め**：TimerとPPUは`QuietTCycles`で次の仕事までのT-cycleを返し、Clockはその間を`Skip`でまとめて進める。HALT中のCPUも`QuietMachineCycles`の分を一度に進める。
- **APUの追いつき**：APUはT-cycleごとには動かず、レジスターのアクセスと実行の終わりに`CatchUp`で今の時刻まで進む。音が変わりうるチャネルだけを1つずつ進め、ほかはまとめて進める。
- **描画の先読み**：PPUはMode 3（描画）の画素の流れをラインの終わりまで先に計算し（`Speculate`）、描画中にレジスターが書かれるとその時点までを確定して計算し直す（`Commit`・`Respeculate`）。書き込みのないラインは`TryDrawWholeLine`で一度に描く。

## Coreの機能ブロック

| ブロック | ファイル（`GonFox.GameBoy.Core/`） | 要点 | 仕様 |
| --- | --- | --- | --- |
| 入口 | `GameBoySystem.cs`・`RunResult.cs`・`DebugSnapshot.cs` | 部品の生成と接続、実行、Reset、状態、観測 | [実行APIと時間](Specification.md#実行apiと時間) |
| 時間 | `Clock.cs` | M-cycle・T-cycleの進行と一括の進行 | [実行APIと時間](Specification.md#実行apiと時間) |
| CPU | `Cpu/Sm83Cpu.cs`・`Cpu/Sm83Cpu.Alu.cs`・`Cpu/CpuFault.cs` | 命令の`switch`とアクセスの時刻、ALUとCB命令、割り込み・HALT・STOP、未定義命令での停止 | [CPU命令表](CpuInstructions.md) |
| バス | `Memory/MemoryBus.cs` | アドレスマップ、WRAM・HRAM、DMAとの競合、Boot ROMの重ね合わせ | [Bus・I/O・起動](Specification.md#busio起動) |
| 割り込み・Timer | `Devices/Interrupts.cs`・`Devices/Timer.cs` | IF・IE、DIVとTIMA、DIVの立下りの配信（`DividerFell`） | [Bus・I/O・起動](Specification.md#busio起動) |
| 入力・シリアル | `Devices/Joypad.cs`・`Devices/Serial.cs` | P1の行選択と立下り、内部クロックの転送 | [Joypad・Serial・APU](Specification.md#joypadserialapu) |
| OAM DMA | `Devices/OamDma.cs` | 転送の開始と再開、バスの競合、HALT中の停止 | [PPU・DMA](Specification.md#ppudma) |
| PPU | `Video/Ppu.cs`・`Video/Ppu.Pipeline.cs` | `Ppu.cs`はラインとモード・STAT・LY=LYC・OAM走査・レジスター・OAMの破損、`Ppu.Pipeline.cs`はMode 3の画素の流れと一括の描画 | [PPU・DMA](Specification.md#ppudma) |
| 画像 | `Video/VideoOutput.cs`・`Video/VideoFrameInfo.cs` | 二重のバッファー、完成した画像のコピー、LCD有効化の最初の画像 | [共通画像とホスト描画](Specification.md#共通画像とホスト描画) |
| APU | `Devices/Apu.cs`・`Devices/PulseChannel.cs`・`Devices/WaveChannel.cs`・`Devices/NoiseChannel.cs`・`Devices/VolumeEnvelope.cs`・`Devices/LengthCounter.cs` | レジスターと電源、フレームシーケンサー、4チャネル、混合 | [Joypad・Serial・APU](Specification.md#joypadserialapu) |
| PCM | `Audio/AudioMixer.cs`・`Audio/AudioOutput.cs` | 混合レベルから48 kHzのPCM（区間の平均と高域通過）、キュー | [PCMとAudioOutput](Specification.md#pcmとaudiooutput) |
| カートリッジ | `Cartridge/CartridgeLoader.cs`・`Cartridge/ICartridge.cs`と機能ごとのインターフェース・各MBCのクラス | ヘッダーの検査と種別の選択、バンク、電池RAM、時計（`Mbc3Rtc.cs`・`Huc3Cartridge.cs`）、カメラ | [カートリッジと電池RAM](Specification.md#カートリッジと電池ram) |
| 起動直後の状態 | `DmgBootProfile.cs` | Boot ROMを省いたときのレジスターとPPU・APUの位相 | [Bus・I/O・起動](Specification.md#busio起動) |
| 状態の保存 | `GameBoyState.cs`・`StateSerializer.cs`・各ブロックの`State` | 状態の集約と検証、バイト列への変換 | [観測・実行状態](Specification.md#観測実行状態) |

### Coreの書き方の決まり

- 各ブロックは`State`レコードと`CaptureState`・`ValidateState`・`RestoreState`を持つ。`GameBoySystem.RestoreState`は全ブロックを検証してから書き換えるため、不正な状態では何も変わらない。状態の構成を変えたら`GameBoyState.CurrentFormat`を上げる。
- 予定のdotや先読みした画素の流れのように、保存した値から計算し直せるものは状態に入れない。
- `Reset`はBoot ROMを省いた起動後の状態（`DmgBootProfile`）にし、`PowerOn`はBoot ROMを使うときの電源投入の状態にする。
- ホストが使う型だけを公開し、テストとベンチマークは`InternalsVisibleTo`で内部を使う。
- コメントは処理の単位に英語で1行だけ書く。挙動の根拠と詳細はdocsにある。

## プラットフォーム層とサンプル

| ファイル | 役割 |
| --- | --- |
| `GonFox.GameBoy.Platform.Shared/EmulationRunner.cs` | 実行スレッド（`Work`）。UIからの操作をコマンドのキューで受け、実時間に合わせた予算（`EmulationPacer`）で`RunForTCycles`を呼ぶ。PCMを`AudioBuffer`へ、画像を`FrameExchange`へ渡し、巻き戻し用の状態を記録する |
| `GonFox.GameBoy.Platform.Shared/EmulationSession.cs` | ROMの読み込みと電池セーブ（ロック、前の世代、時計の加算）、定期保存、状態の枠。結果は`Notice`の値で返す |
| `BatterySaveStore.cs`・`BatteryFile.cs`・`StateSlotStore.cs`・`RecordStore.cs` | 電池セーブと状態の形式、保存の抽象（`IRecordStore`。ファイル版とメモリー版） |
| `FrameExchange.cs`・`FrameLayout.cs`・`DisplayPolicy.cs` | 3枠の画像の受け渡し、整数倍の配置、描画の頻度 |
| `Audio/AudioBuffer.cs`・`Audio/AudioPlayback.cs` | 目標の量を自動で調整する音声のキューと、出力デバイス（`IAudioDevice`）の開閉 |
| `ButtonInputState.cs`・`MemoryView.cs` | 複数の入力元の合成、メモリーの表示の範囲 |
| `GonFox.GameBoy.Platform.Windows/` | `WasapiAudioDevice`（WASAPIの出力）、`HostSettings`（設定ファイル）、`WindowsStorage`（置き場所）、`WindowsThread`（スレッドの名前） |
| `GonFox.GameBoy.Platform.Android/` | `AudioTrackDevice`（AudioTrackの出力）、`AndroidStorage`（置き場所）、`ButtonHaptics`（触覚）、`RumbleMotor`（振動） |

サンプルは、上の部品を画面につなぐだけである。

- WPF：[MainWindow.xaml.cs](../Example.GameBoy.WpfHost/MainWindow.xaml.cs)がRunner・Session・AudioPlaybackを作る。入力（`HandleKeyDown`・`Pad*`）、描画（`OnRenderingFrame`から`ShowLatestFrame`、`Rendering/`の`IFrameDisplay`）、ROMの読み込み、状態の枠、定期保存（`AutoSave`）をつなぐ。画面の文言は`HostText.cs`と`StatusText.cs`にある。
- MAUI：[MainPage.cs](../Example.GameBoy.MauiHost/MainPage.cs)が同じ部品をつなぐ。画像は`ScreenView`（`SKCanvasView`）、画面のボタンは`PadView`、表示と定期保存は`Tick`、背景に回ったときの保存は`SaveForBackgroundAsync`、起動の値は`LaunchRequest`が扱う。

## 目的別の読み方

| 知りたいこと | 読む順 | 対応するテスト |
| --- | --- | --- |
| CPU命令と時間 | `Sm83Cpu.StepInstruction`の該当の`case`、`ReadCycle`・`WriteCycle`、`Sm83Cpu.Alu.cs` | `Cpu*Tests`、テストROM集の`cpu-instrs`・`instr-timing`・`mem-timing` |
| 割り込み・HALT・STOP | `Sm83Cpu.ServiceInterrupt`・`WaitHalted`・`Stop`、`Interrupts.cs` | `InterruptTests`・`HaltSkipTests`・`StopTests`・`StatInterruptTimingTests`、`irq-timing`・`halt-bug` |
| Timer | `Timer.Tick`、`DividerFell`、`TickReload` | `TimerTests`、Mooneyeの`timer/` |
| PPUのタイミング | `Ppu.Tick`、`RunEventDot`、`StartLine`・`EnterLineMode`・`EndMode3`、`UpdateStat` | `PpuTests`・`PpuBoundaryTests`・`StatInterruptTimingTests`、gbmicrotest、`ppu-timing` |
| 画素の描画 | `Ppu.Speculate`、`Ppu.Pipeline.cs`の`Run`・`Iterate`・`TryDrawWholeLine`、描画中の書き込みは`WriteRegister`から`WriteWhileDrawing` | `PpuPipelineTests`・`PpuWriteTimingTests`・`PpuWholeLineTests`・`PpuRendererReferenceTests`、mealybug・dmg-acid2 |
| OAM DMAとOAMの破損 | `OamDma.cs`、`MemoryBus.ReadByte`、`Ppu.CorruptOamByWrite`・`CorruptOamByRead` | `OamDmaTests`・`OamCorruptionTests`、`oam-dma`・`oam-bug` |
| APU | `Apu.WriteRegister`・`ReadRegister`、`CatchUp`、各チャネルの`Advance`・`Elapse`、`Mix`、`AudioMixer` | `Apu*Tests`・`AudioOutputTests`、`apu` |
| カートリッジ | `CartridgeLoader.Load`、種別ごとのクラス | `CartridgeLoaderTests`・`Mbc*Tests`・`Mmm01Tests`・`HuC1Tests`・`Huc3Tests`・`CameraTests`、Mooneyeの`emulator-only/` |
| 状態の保存 | `GameBoySystem.CaptureState`・`RestoreState`、各ブロック、`StateSerializer` | `StateTests`・`StateSerializationTests`・`StateRomTests`・`DeterministicReplayTests` |
| ホストへの組み込み | `EmulationRunner.Work`、`EmulationSession.LoadAsync`、サンプルの`MainWindow.xaml.cs` | Platform.Testsの`EmulationRunnerTests`・`BatterySaveTests`・`StateSlotTests`・`AudioBufferTests` |

## テストの読み方

- `GonFox.GameBoy.Core.Tests`は、ブロックごとの単体テスト（`Category=Unit`）と、ROMを実行するテスト（`Category=Rom`）に分かれる。ROMのテストは、公開テストROMの`MooneyeTests`・`GbMicrotestTests`・`MealybugTests`・`DmgAcid2Tests`・`Rtc3TestTests`・`GameBoyTestRomsTests`・`SameBoyBootRomTests`、テストROM集の`TestSuiteTests`と、自作のデモを使うテストである。
- 単体テストの土台：`TestRom.cs`は手で組み立てた命令列をROMの形にし、`TestRom.Start`で実行を始める。`CpuTestMachine.cs`はCPUとバスだけの機械で、アクセスの時刻を記録する。`PeripheralTestMachine.cs`はCPU・Timer・Serial・Joypad・APUとバスをつないだ、PPUのない機械である。`TestRomRunner.cs`はMooneyeとgbmicrotestのROMを、それぞれの判定の手順で実行する。
- `GonFox.GameBoy.Platform.Tests`はPlatform.SharedとPlatform.Windows、`Example.GameBoy.WpfHost.Tests`はWPFのサンプルを確かめる。
- テストが使うROMは`Roms`にあり、各フォルダーのREADMEが中身と期待値の作り方を説明する。
