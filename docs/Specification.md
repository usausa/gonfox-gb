# Game Boy エミュレーター 現行仕様

対象はGonFox.GameBoy 1.0.0の実装で、機種はDMGだけである（CGB・SGBは別のハードウェアの仕様のため対象外。CGB専用のMBC6・MBC7も扱わない）。保存状態の形式は12である。サンプル（WPF・MAUI）の使い方は[サンプルの使い方](Examples.md)、ROMでの検証・既知の近似・未対応の項目は[互換性と検証](Compatibility.md)、性能の方針と測り方は[ベンチマーク](Benchmark.md)、CPUの命令は[CPU命令表](CpuInstructions.md)にまとめる。

## 構成と所有権

| プロジェクト | ターゲット・責務 |
| --- | --- |
| GonFox.GameBoy.Core | net10.0。CPU・Bus・Clock・周辺回路・カートリッジ・ビットマップ。外部パッケージなし。トリミング・AOTの解析を有効にし（`IsAotCompatible`）、警告0 |
| GonFox.GameBoy.Core.Tests | net10.0。Coreのみを参照する検証（xunit.v3。Microsoft.Testing.Platformの実行ファイル） |
| GonFox.GameBoy.Platform.Shared | net10.0。プラットフォームに依存しない共通の実装（WPFのサンプルも、MAUI Androidのサンプルも使う）：実行スレッド（`EmulationRunner`）・セッション・フレームの受け渡し（`FrameExchange`）・画像の配置（`FrameLayout`）・描画の頻度の判定・音声のバッファーと再生の制御（`IAudioDevice`の手前まで）・電池セーブ・状態の枠・保存の抽象（`IRecordStore`とファイル版の`FileRecordStore`。置き場所はホストが渡す）・入力の合成。画面に出す文言は持たず、実行の状態（`EmulationStatus`。CPUの停止は`Fault`）・電池セーブの結果（`EmulationSession.Notice`の`SessionNotice`）・音声の出力（`AudioStreamInfo`）を値で返し、ホストが言葉にする。例外のメッセージは英語。環境変数・既定のパス・ウィンドウ・Win32の呼び出しを持たない。AndroidとiOSも対象にした互換性の解析（CA1416）と、トリミング・AOTの解析（`IsAotCompatible`）で警告0 |
| GonFox.GameBoy.Platform.Windows | net10.0-windows10.0.19041.0。Windows向けの共通の実装：WASAPIの音声出力（`WasapiAudioDevice`、NAudio.Wasapi）、設定ファイル（音量・ミュート・ウィンドウの位置・描画方式。`HostSettingsStore`）、既定の置き場所（`%LOCALAPPDATA%/GonFox.GameBoy`と、確認のために置き場所を替える環境変数。`WindowsStorage`）、実行スレッドの説明（`SetThreadDescription`。`WindowsThread`）。UIの枠組みに依存せず、WPF以外のWindowsのホスト（WinForms・WinUI・MAUIのWindows版）も使える |
| Example.GameBoy.WpfHost | net10.0-windows10.0.19041.0。WPFのホストのサンプル：画面（XAML）・ファイルダイアログ・画像の表示（`IFrameDisplay`。SkiaSharpとWriteableBitmapを実行中に切り替える）・ウィンドウの配置・組み込みデモ |
| GonFox.GameBoy.Platform.Tests | net10.0-windows10.0.19041.0。Platform.SharedとPlatform.Windowsの検証：実行スレッド・時間・入力・電池セーブ・状態の枠・保存の抽象・フレームの受け渡し・状態表示の文字・設定と置き場所・音声のバッファーと再生制御 |
| Example.GameBoy.WpfHost.Tests | 同Windowsターゲット。WPFのサンプルの検証：Skia転送・WriteableBitmapの表示・画像の配置・組み込みデモ・ウィンドウの設定 |
| GonFox.GameBoy.Platform.Android | net10.0-android（Android 8.0、API 26以上）。Android向けの共通の実装：AudioTrackの音声出力（`AudioTrackDevice`）、アプリの置き場所（`AndroidStorage`）、画面のボタンの触覚（`ButtonHaptics`）、振動カートリッジのモーターを端末の振動へ（`RumbleMotor`）。UIの枠組みに依存せず、MAUI以外のAndroidのホストも使える。トリミングの解析（`IsTrimmable`）で警告0 |
| Example.GameBoy.MauiHost | net10.0-android。MAUIのAndroidのホストのサンプル（縦向き）：画面と画面のボタン（SkiaSharp.Views.Maui.Controls 4.153.1の`SKCanvasView`）、コマンド・メッセージ・設定（MAUIのコントロール）、ファイルの選択（`FilePicker`）、設定の保存（`Preferences`）、起動の値（intentのextra）・組み込みデモ。ビルドには.NET MAUIのAndroidのワークロードが要る。Release版はMonoのランタイムで全メソッドをLLVMで事前コンパイルする（既定の、起動時のメソッドだけの事前コンパイルとJITより実行スレッドの負荷が約半分） |
| Benchmark | net10.0。BenchmarkDotNetによるCoreの測定（[ベンチマーク](Benchmark.md)） |

GonFox.GameBoy.Core・GonFox.GameBoy.Platform.Shared・GonFox.GameBoy.Platform.Windows・GonFox.GameBoy.Platform.AndroidはNuGetパッケージとして公開する（パッケージ名はプロジェクト名と同じ）。サンプルとテスト・ベンチマークは公開しない。

`GameBoySystem`が具象ブロックを組み立てる。コアは同期・単一所有者で、同じシステムへの呼び出しをホストが直列化する。カートリッジも複数システムで共有しない。コアはスレッド・実時計・ファイル・WPF・SkiaSharp・音声デバイスを扱わない。汎用デバイス基底クラス、イベントバス、DI、プラグイン機構は設けない。

**三層の構成と別のプラットフォーム：** (1) Core：環境中立のエミュレーター（MAUIからも使う）。(2) 共通の実装：プラットフォームに依存しないGonFox.GameBoy.Platform.Sharedと、Windows向けのGonFox.GameBoy.Platform.Windows。サンプル以外のホストもこれを使う。Coreは同期・単一所有者でスレッド・実時計・ファイルを持たないため、実行スレッド・実時間の調整・音声のキュー・保存の手順はCoreの外に要る。これらはどのプラットフォームでも同じ.NETのコードなので、プラットフォームごとの層ではなくGonFox.GameBoy.Platform.Sharedに置き、Androidのホストと共有する。環境変数・既定のパス・ウィンドウ・Win32の呼び出しなど、Windowsのホストだけが前提にするものはGonFox.GameBoy.Platform.Windowsに置く。ホストが使う型は公開し、NuGetパッケージとして配る。(3) ホストのサンプル：WPFの画面・表示・ファイルの選択・ウィンドウだけを持つExample.GameBoy.WpfHostと、MAUIの画面・画面のボタン・ファイルの選択・設定だけを持つExample.GameBoy.MauiHost。Android向けの共通の部分（AudioTrackの`IAudioDevice`、アプリの置き場所、触覚と振動）はGonFox.GameBoy.Platform.Androidに置き、MAUIのホストはCoreとGonFox.GameBoy.Platform.Sharedを変えずに使う（[MAUI Androidのホスト](#maui-androidのホスト)）。MAUIのWindows版を作るなら、GonFox.GameBoy.Platform.WindowsのWASAPIを使える。実行スレッドの名前のようなプラットフォームの処理は、ホストが`EmulationRunner`の作成時に渡す（Windowsは`WindowsThread`。Androidでは.NETが`Thread.Name`をそのまま渡す）。CoreとGonFox.GameBoy.Platform.Sharedは`IsAotCompatible`で、トリミングとNativeAOTの解析の警告がない。

```text
ホスト ─ 実行/入力 ─> GameBoySystem
                       CPU ─> MemoryBus ─> Cartridge / RAM / 各I/O（APUを含む）
                        └─> Clock ─> Timer（DIVの立下り ─> Serial / APU） / DMA / PPU
                                     Timer ─> APU ─> AudioOutput
                                                        PPU ─> VideoOutput
ホスト <─ コピー ── DebugSnapshot / メモリー / 状態 / BGRA32 / PCM ─┘
  GonFox.GameBoy.Platform.Shared: EmulationRunner ─> FrameExchange（3枠） ─> IFrameDisplay（SkiaSharp / WriteableBitmap） ─> WPF画面
                │                                    └──> ScreenView（SKCanvasView） ─> MAUI Androidの画面
                └─ AudioBuffer ─> IAudioDevice（GonFox.GameBoy.Platform.Windows: WASAPI、既定の出力デバイス）
                                 IAudioDevice（GonFox.GameBoy.Platform.Android: AudioTrack、メディアの音量）
```

## 実行APIと時間

```csharp
var loaded = CartridgeLoader.Load(image); // ファイルはホストで読み込む
var system = new GameBoySystem();
system.InsertCartridge(loaded.Cartridge);
RunResult result = system.RunForTCycles(4096);
DebugSnapshot registers = system.GetDebugSnapshot();
system.CopyMemory(0xC000, memory);
VideoFrameInfo frame = system.Video.CopyLatestFrame(pixels);
int frames = system.Audio.ReadFrames(pcm); // 48 kHzステレオ、L/R交互のshort
GameBoyState saved = system.CaptureState();
system.StepInstruction();
system.RestoreState(saved);
byte[] bytes = saved.Serialize();                      // ホストが保存するバイト列（Coreはファイルを扱わない）
system.RestoreState(GameBoyState.Deserialize(bytes));
```

- `StepInstruction()`は1命令、割り込み受理1回、またはHALTの4 T待機を実行する。命令表は[CPU命令表](CpuInstructions.md)。
- `RunForTCycles(int minimumTCycles)`は指定量以上を命令境界まで進める。実消費を`RunResult.ExecutedTCycles`（long）、STOPを`IsStopped`で返す。超過分の調整はホストが行う。
- `Audio.ReadFrames`は生成済みのPCMをコピーして取り除く。エミュレーションは進めず、各実行呼び出しの終わりまでのPCMが読める（[PCMとAudioOutput](#pcmとaudiooutput)）。
- 接続済みで予算0なら不変・消費0。負数は引数エラー。未接続の実行は予算0でも状態エラー。
- `InsertCartridge`は接続後にResetする。Resetは内部RAM・周辺・CPUを初期化し、カートリッジRAMを消さない。`UseBootRom`でBoot ROMを渡していれば、Resetは電源投入の状態にしてBoot ROMから始める（[Bus・I/O・起動](#busio起動)）。
- 通常245コード（CBプレフィックスを含む）・CB側256コードを実装する。未定義11コードはフェッチの4 TとPC進行を残し、実機と同じくCPUだけが止まる（ロックアップ）。以後も実行APIは時間を進め（`StepInstruction`は4 T）、PPU・Timer・APU・DMA・Serialは動き続け、割り込みでは再開しない。`IsFaulted`と`Fault`（`CpuFault`：アドレスとコード、説明文）で知らせ、Reset・ROM交換・止まる前の状態への復元で戻る。

クロックは4,194,304 T-cycles/秒。CPUのバスアクセスを先に適用し、その後4回、**累積時間 → Timer（TIMA・シリアルのクロック・DIV-APUの立下りを含む） → APU → DMA → PPU**の順に進める。Serialは毎Tの処理を持たず、Timerが渡すDIVの立下りで進む。MBC3の時計も毎Tの処理を持たず、時計のレジスタ・ラッチへのアクセス、状態保存、ホストの取得の時点で、累積Tの差からまとめて進める（Reset・状態復元・カートリッジの交換の前後で基準を付け替え、Resetの間も止めない）。APUは毎Tの処理を持たず、レジスタアクセス・DIV-APUの立下り・観測・状態保存・各実行呼び出しの終わりの時点で、そこまでの4チャネルの進行と混合・PCM生成をまとめて計算する。結果はこの順で毎T進めた場合と同じで、同じTではDIV-APUの立下りがチャネルの進行より先になる。内部待機も同じ経路を使い、命令末尾で二重加算しない。命令の途中では外部へ制御を返さない。実行API（`RunForTCycles`）でHALT中のCPUは、どの周辺も動かないM-cycle（DIVの立下りで聞き手のあるbit（TIMA・シリアルのクロック・APUのフレームシーケンサー）が落ちない、TIMAの再ロードがない、OAM DMAがない、PPUの次の処理のdotの前、割り込みの要求が標本化を待っていない）をまとめて進める。結果は1 M-cycleずつ進めた場合と同じで、実行予算の終わりも同じM-cycleになる（`StepInstruction`は1 M-cycleずつ）。命令のM-cycleでも、その4 Tにどの周辺も動かない（上と同じ条件で、OAM DMAの転送と開始待ちもない）ときは、4回のTickの代わりに一度に進める（結果は同じ）。

HALTでは周辺時間は進み、各待機M-cycleの2 T目にIF & IEを標本化して、要求があればそのM-cycleの終わりで復帰する（後半2 Tの要求は1 M-cycle遅れる）。HALT直後の最初の待機M-cycleだけは始まりで標本化するため、その前半2 Tの要求も1 M-cycle遅れる（SameBoyの`just_halted`、Gambatte `late_m0irq_halt_m0stat_scx3`）。IMEが有効なら続けて割り込みを受理する。HALT実行時に要求があれば停止せず、IME=0ならHALT bug、IME=1またはEI直後なら受理後の戻り先をHALT自身にする。STOPは、フェッチ（4 T）の後に、P1で選んだ行のボタンが押されているか（P10〜P13のどれかが低い）と割り込みの要求（IE & IF）があるかで4通りに分かれる（Pan Docs（Reducing Power Consumption）の分岐図、SameBoy `c458e7c5`の`stop`）。押されていて要求もあれば1バイトのNOP。押されていて要求がなければ、次のバイトを読むM-cycle（時間は進む）の後にHALTする（HALT直後として扱う）。押されていなくて要求があればSTOPに入り、次のバイトは復帰の後に命令として実行する（IMEが有効なら割り込みの後）。どちらもなければSTOPに入り、次のバイトを読み飛ばす。STOPに入るとDIVをリセットして時間を凍結し、追加実行は消費0で戻る（DIVリセットによる立下りは、通常のDIV進行と同じ経路でAPUへ伝える。HALT中はAPUも進み、STOP中は止まる）。STOPに入った後に選んだ行の線が下がると（押下か行選択の変更。Joypadの割り込みと同じ立下り）、次の正の予算/ステップで復帰する。立下りは記録するため、次の実行までに離しても復帰する。復元した状態では、選んだ行の線が低ければ復帰する。行を選んでいない（P1=$30）ときはResetだけが戻す。入りと復帰に余分なTはかけない（SameBoyの復帰後の8 TとIME=0のDIVの保持は、確かめるテストがなく入れていない）。

## Bus・I/O・起動

| 論理アドレス | 接続先 |
| --- | --- |
| 0000–7FFF / A000–BFFF | カートリッジROM・MBC制御 / 選択RAMバンク。Boot ROMの対応付け中は0000–00FFの読み出しをBoot ROMが返す（下記） |
| 8000–9FFF / FE00–FE9F | PPUのVRAM 8 KiB / OAM 160 bytes |
| C000–DFFF / E000–FDFF | WRAM 8 KiB / C000–DDFFのミラー |
| FF80–FFFE | HRAM 127 bytes |
| FF00 / FF01–FF02 | Joypad / Serial |
| FF04–FF07 / FF0F / FFFF | Timer / IF / IE |
| FF10–FF26 / FF30–FF3F | APUレジスタ / Wave RAM |
| FF40–FF45 / FF47–FF4B / FF46 | LCDレジスタ / OAM DMA |
| FEA0–FEFF | 使用禁止の領域。DMGはOAMが止められていない間は00、止められている間（Mode 2・3とOAM DMA）はFFを読み、書き込みは無視（Pan Docs）。Mode 2の読み書きはOAMを壊す（下記） |
| FF50 | Boot ROMの切り離し（bit 0が1の書き込み）。対応付け中はFE、切り離し後とBoot ROMなしではFFを読む |
| その他 | 読み出しFF、書き込み無視 |

I/OをRAM配列で代用せず、各ブロックがマスクと副作用を処理する。CPU経路、DMA経路、デバッグ参照を分ける。`CopyMemory`はクロック・I/Oを変えず、CPUのDMA/PPUアクセス制限を迂回する。現在のバンクとRAM有効化は維持し、FFFF超過はコピー前に拒否する。`ICartridge.Read`にも副作用を持たせない。

割り込みはVBlank、STAT、Timer、Serial、Joypadの5系統。CPUが命令境界でIE・IMEと照合して5 M-cycleで受理する（待機3 M-cycle、PCの上位・下位バイトのpush）。上位バイトのpush後のIEと、下位バイトをpushする時点（受理の開始から16 T、書き込みの前）のIFから優先順位順に1件選び、その要求を2 T後に消す（Gambatte `late_m0irq_vs_tima`・`lycint143_m1irq_late_retrigger`）。pushがIEを書き換えると選び直し、該当なしならIFを変えずPC=0000になる。CPUのIF書き込みはM-cycleの1 T目の後に効き、同じ1 T目に立った要求も書いた値で上書きする（読み出しはM-cycleの始まりの値。SameBoy、Gambatte `m2int_m0irq_scx3_ifw`）。Timerは16-bit DIVの選択bit（9/3/5/7）の立下りでTIMAを増やす。DIV/TAC書き込みの立下りも扱う。オーバーフローは4 T後にTMA再ロードとIRQを行い、待ち中のTIMA書き込みで取消、再ロード衝突窓でのTIMA/TMA書き込みを区別する。

既定では、Boot ROMを省略した固定プロファイル `BootBypass-v2`で起動する。値の定義元は[DmgBootProfile.cs](../GonFox.GameBoy.Core/DmgBootProfile.cs)。Boot ROMの実行後の状態を写したもので、実機の電源投入状態の完全な再現ではない。

| 初期状態 | 値 |
| --- | --- |
| AF / BC / DE / HL / SP / PC | 01B0 / 0013 / 00D8 / 014D / FFFE / 0100 |
| T / WRAM・HRAM・VRAM・OAM | 0 / 全て00 |
| DIV内部 / TIMA・TMA・TAC | ABCC / 00（TAC読み出しはF8） |
| IF / IE / JOYP / SB / SC（読み出し） | E1 / 00 / CF / 00 / 7E（JOYPは両方の行を選んだ状態。Pan Docs、Mooneye `boot_hwio-dmgABCmgb`） |
| LCDC / BGP / OBP0・1 / LY・Dot・Mode | 91 / FC / FF / ライン153のDot 400・Mode 1（LYは0を読み、LY=LYCの一致が立つ。ライン0の始まりの56 dot前。DotはLY更新点を0とする）。Gambatteの起動後の状態（153×456+396 video cycle）、Gambatte `display_startstate`とgbmicrotest `poweron_*`のDMG-CPU-08の結果に合わせた |
| APU | 電源ON、Wave RAM=00、NR52=F1、NR50=77、NR51=F3。起動音2音目のCH1（duty 2、周期値7C1、長さ無効）が動作中のまま音量0・包絡線停止で、duty位置3、次の位置まで276 T（以後252 Tごと）。CH2・CH4は停止、CH3はDAC OFF（NR30=7F）・出力0%（NR32=9F）。フレームシーケンサーは段0の後で、次の段は1。CH1の位相と段はGambatteの起動後の状態で、そのDMG-08の結果（`ch1_init_pos_*`・`ch2_init_env_counter_timing_*`）に合う。高域通過は起動時の出力（左右とも+7,680）に落ち着いた状態 |

**実Boot ROM:** ホストが256 bytesのDMGのBoot ROMを`GameBoySystem.UseBootRom`で渡すと、次のResetとカートリッジの挿入から電源投入の状態にしてBoot ROMを実行する（`UseBootBypass`で既定に戻す。`UsesBootRom`・`IsBootRomMapped`で観測）。エミュレーターはBoot ROMを同梱しない（任天堂のものは使えないため、確認にはMITのSameBoyのBoot ROMを使う。[互換性と検証](Compatibility.md#sameboyのboot-rom)）。規則はPan Docs（Power Up Sequence）とSameBoy（`memory.c`）に従う。

- 対応付け中は0000–00FFの読み出し（CPU・OAM DMA・デバッグ参照）をBoot ROMが返し、0100以降はカートリッジが返す。書き込みはカートリッジへ届き、MBCの制御も変わる。
- FF50へbit 0が1の値を書くと切り離す。対応付けに戻すのは次のResetだけ（再びFF50へ書いても戻らない）。
- 電源投入の状態：PCとレジスタは全て0、LCD OFF（LCDC 00、BGP 00、LY・Dot・Modeは0）、APUの電源OFF（レジスタは0で、NR52は70を読む）、DIV内部0、IF E0。WRAM・HRAM・VRAM・OAM・Wave RAMは実機では不定だが、このモデルでは0のまま。ほかはBootBypassの表と同じ。
- 状態の保存は、対応付け中のBoot ROMの内容を持つ（起動の方式は`BootRom-v1`。復元するシステムがBoot ROMを持たなくてもよい）。

## Joypad・Serial・APU

Joypadは8ボタンとFF00の2行選択をactive-lowで表す。両行を選ぶと入力線を合成する。押下変更・行選択変更で下位4bitの1→0を検出しIRQを要求する。同じ押下の再通知では新しいエッジを作らない。`SetButtonState`、`ReleaseAll`を外部入力に使い、復元後の入力源の切り替えには`SynchronizeButtons`を使う。

Serialの内部クロック（8,192 Hz）はDIVのカウンターから分周する。カウンターのbit 7が立ち下がるたびに（256 Tごと）クロックが反転し、内部クロックの転送はクロックが下がるたびに（512 Tごと）1 bitずつ進めて、8 bitで完了する。未接続の受信値はFF。SCへの書き込みは、まずクロックが高ければ下げる（このとき前の制御で転送中なら1 bit進む）。このため最初のbitは書き込みの257〜512 T後、完了は3,841〜4,096 T後で、bitの時刻は書き込みではなくリセットからのカウンターの位相に従う（SameBoy `c458e7c`、Mooneye `boot_sclk_align-dmgABCmgb`）。DIVの書き込みとSTOPのリセットによるbit 7の立下りもクロックを反転する。再要求は数え直す。外部クロックは未入力のまま待機する。`TryReadTransmittedByte`は完了した送信値を取り出す。観測キューは256 bytes、超過時は古い値を落として`DroppedByteCount`へ加算する。

APUは電源（NR52 bit 7）、読み取りマスク、16 bytesのWave RAM、DIV-APUのフレームシーケンサー、CH1/CH2のPulse、CH3のWave、CH4のNoise、NR50/NR51の左右の混合、48 kHzのPCM生成を持つ。各チャネルの出力はデジタル値0〜15（Core内部で観測）で、DACが左右のレベルへ変える。

| 項目 | DMGの動作 |
| --- | --- |
| フレームシーケンサー | Timerが通知するDIV bit 12（DIVレジスタのbit 4）の立下りごとに1段進む（512 Hz）。通常の進行・FF04書き込み・STOPのDIVリセットを同じ経路で扱う。段0/2/4/6で全チャネルの長さ、段2/6でCH1スイープ（そのクロックは立下りの4 T後）、段7でCH1/CH2/CH4の包絡線。電源ONで次の段を0にし、書き込んだM-cycleの終わり（4 T後）にDIV bit 12が1なら最初の立下りを無視する |
| 周期・duty | 11 bitの周期値から(2048−周期値)×4 Tごとにduty位置を1つ進める。NRx3/NRx4の変更は進行中の周期の後に反映する。トリガーは周期を読み直してduty位置を保ち（電源OFFで0）、最初に位置を進めるのを停止中のチャネルでは周期+8 T後、動作中のチャネルでは周期+4 T後にする。dutyは位置0から00000001・10000001・10000111・01111110 |
| 出力 | dutyのbitは位置を進めたときに決まり、1なら音量、0なら0。NRx1でdutyを変えても次の位置まで出力は変わらない。停止中のチャネルを開始すると最初の1周期は0。停止中・DAC OFFは0 |
| Wave（CH3） | 32個の4 bitサンプル（Wave RAMの各byteの上位が先）を(2048−周期値)×2 Tごとに1つ読む。周期の変更は次の読み込みから。トリガーで位置を0にし、1周期+6 T後に位置1（先頭byteの下位）を読む。それまでは最後に読んだbyte（電源OFFで0）の上位を出す。NR32の出力レベル（0%・100%・50%・25%＝右シフト4・0・1・2）はすぐに反映する |
| 再生中のWave RAM | CH3の動作中、CPUの読み書きはCH3がサンプルを読むTだけ届き、アドレスによらずそのとき読んだbyteが対象になる。ほかのTの読み出しはFF、書き込みは無視。デバッグ参照（`CopyMemory`）は制限なくWave RAM自体を返す。次の読み込みの2 T前に再トリガーすると、次に読むbyteが先頭4 bytes内ならbyte 0をそのbyteで、それ以外ならそのbyteを含む4 bytes境界の4 bytesで先頭4 bytesを上書きする |
| Noise（CH4） | 15 bitのLFSR。トリガーで0にし、クロックごとにbit 0とbit 1のXNORをbit 14（NR43 bit 3が1ならbit 6にも）へ入れて右シフトする。bit 0が1なら音量、0なら0。クロック間隔は除数8・16・32・48・64・80・96・112 T（NR43 bit 0〜2）をシフト量（bit 4〜7）だけ左シフトした値で、シフト量14/15ではクロックしない。NR43の変更は次の再読み込みから |
| DAC | NRx2の上位5 bit（CH3はNR30 bit 7）が0ならDAC OFFでチャネルを止める。DAC OFFではトリガーしても開始しない |
| 長さ | NRx1の書き込みで64−値（CH3は256−値）。段0/2/4/6で有効なら1減らし、0で停止。トリガー時に0なら64（CH3は256）。次の段が長さを進めない時期に長さを有効にすると1回余分に減らし、0になればトリガーがない限り停止する。その時期に0からトリガーすると63（CH3は255） |
| 包絡線 | トリガーで初期音量と周期を読み込む（周期0は8として数えるが音量を変えない）。次の段が7なら1周期分遅らせる。この判断は書き込んだM-cycleの終わりの時点で行い、その4 Tの間に立下りがあればその段の後の次の段で決める。段7ごとに数え、0と15の端で止まる。動作中のNRx2書き込みは、旧周期が0で包絡線が動作中なら音量を1増やす場合だけを再現する |
| スイープ（CH1） | トリガーで周期値を影へ写し、タイマーを間隔（0は8）にし、間隔かシフトが0以外なら有効にする。シフトが0以外なら即座に溢れを確認する。段2/6の立下りの4 T後に数え、間隔が0以外なら新周期を計算して2047超えで停止、シフトが0以外なら書き戻してもう一度確認する。トリガーは、書き込んだM-cycleの4 Tの間の段2/6と、立下りの後でまだ来ていないそのクロックを、読み込み直したタイマーに数えない。トリガー後に減算で計算した後、減算をやめるとCH1を止める |
| 混合 | DACはデジタル値dを15−2d（チャネルが停止中でもDAC ONなら+15）、DAC OFFは0にする。NR51のbit 4〜7/0〜3でCH1〜CH4を左/右の和へ入れ、NR50のbit 4〜6/0〜2の値+1（1〜8倍）を掛ける。VIN（NR50 bit 7/3）は未接続。各側のレベルは−480〜+480 |
| 電源 | OFFでNR10〜NR51を0にし、全チャネルの状態（duty位置・長さ・CH3の読み込み済みbyte・LFSRを含む）を消す。Wave RAMは保つ。OFF中に書けるのはNR52・Wave RAM・長さレジスタ（NR11/NR21/NR31/NR41）だけ（NR11/NR21のduty bitは書かれない）。ONでは長さを保ち、次の段を0にする |
| NR52 | bit 7=電源、bit 0〜3=CH1〜CH4の動作（読み出しの時点まで追いつき、来ているスイープのクロックを反映する）。チャネルbitへの書き込みは無視する |

再現しない特殊ケース（シフト量に応じたスイープ計算の遅れ、その他の"zombie"書き込み、Wave RAMの書き換えの機体差、DAC OFFの緩やかな変化、PCMの近似等）と参照資料は[互換性と検証のAPU](Compatibility.md#apu)と[既知の近似と未対応](Compatibility.md#既知の近似と未対応)に記す。

### PCMとAudioOutput

- 48,000 Hz・ステレオ・signed 16 bit、左右交互。Reset時の累積T=0から数えてk番目のフレームは、T=⌈(k−1)×4,194,304/48,000⌉から⌈k×4,194,304/48,000⌉の直前まで（87か88 T）を受け持つ。位相は整数（T×48,000 mod 4,194,304）で持ち、1エミュレーション秒でちょうど48,000フレームになる。
- 各フレームは、受け持つT-cycleの各側のレベルの平均（区間平均。24 kHzを超える成分は折り返す）を64倍し、Q16の固定小数点（端数切り捨て）にする。次にDMGの高域通過でDC成分を除く：out = in − c、c ← in − out × 65,296/65,536（Pan Docsの毎T 0.999958を4,194,304/48,000 T分まとめた0.99634）。出力はoutを四捨五入（0.5は正の向き）し、−32,768〜32,767に切り詰める。定常レベルの最大は±30,720で、切り詰めは高域通過の行き過ぎ（反対側への急変など）で起きる。
- フレームの最後のT-cycleで全DACがOFFなら、そのフレームは0で、コンデンサーの値を変えない（DMGでは増幅部から切り離される）。
- 整数演算だけなので、実行の分割・APUの同期の時点・読み出しの時期によらず同じPCMになる。
- 形式は`AudioOutput`の定数`SampleRate`（48,000）・`ChannelCount`（2）・`CapacityFrames`（2,048）で示す。サンプルは`short`の列（L, R, L, R…、ネイティブのバイト順で、x64/Arm64ではs16le）で、バイト列が必要なホストは`MemoryMarshal.AsBytes`でそのまま渡せる。リサンプリング・形式変換・遅延の調整・デバイスの扱いはプラットフォームごとのホストの役割で、Coreはどの環境でも同じPCMを返す。
- `system.Audio`（`AudioOutput`）は2,048フレームの固定リング。満杯で新しいフレームができると最古の1フレームを捨てて`DroppedFrameCount`へ加え、CPUを止めない。`QueuedFrameCount`は未読のフレーム数。`ReadFrames(Span<short> destination)`は未読PCMを古い順に最大`destination.Length / 2`フレームコピーして取り除き、フレーム数を返す。L/Rの組を分けず、奇数長は何も変えずに`ArgumentException`。Reset（ROM接続を含む）でキューと破棄数を空にする。デバイス型・スレッド・時計は持たず、システムと同じ所有者から呼ぶ。

## PPU・DMA

1ラインは456 dot、1周期は154ライン=70,224 T。DotはLYが変わる点を0とし、内部の走査ライン（0〜153）と公開LYを分ける。CPUは4 dotごと（Dot 0, 4, …）にPPUを観測するため、下表はその境界で見える値を示す。Mode・LY=LYC・VBlankの変化とHBlank要求は内部では境界の直後（主に2 dot目）に起き、HALT中のCPUは同じM-cycleで気付く（HALT直後の最初のM-cycleを除く。[CPU命令表](CpuInstructions.md#実行状態と割り込み)）。

| CPUから見える位置 | モデルの動作 |
| --- | --- |
| ライン1〜143のDot 0 | LY更新。STATはMode 0のまま、LY=LYC bitは0（比較なし）。OAM読み出しを拒否し、OAM源（STAT bit 5）が立つ |
| Dot 4〜76 | Mode 2。OAMの読み書きを拒否し、LY=LYCとWindowのY条件を比較。OAMの項目iをDot 2+2iで判定してSpriteを選ぶ。CPUのOAMへのアクセスと16 bitの増減はOAMを壊す（下記） |
| Dot 80 | Mode 2のままVRAM読み出しを拒否し、OAM書き込みは通す |
| Dot 84〜252（＋延長） | Mode 3。VRAM/OAMの読み書きを拒否。SCX mod 8・Windowの開始・OBJの取得の分だけ延びる（下記） |
| Dot 256（＋延長）〜452 | Mode 0。HBlank源は、最後の画素の位置に着いた1 dot後に立つ（そこでのOBJ（X=167）の取得を待たない。ほかはMode 0の始まりの1 dot後） |
| ライン144 | Dot 0でLY=144、OAM源（DMG）とHBlank源はMode 1の始まりまで続く。Mode 1の直前にLY=LYCを144と比較する。Dot 4からMode 1、画像完成、VBlank要求 |
| ライン153 | LYはDot 0だけ153、Dot 4から0。LY=LYCはDot 4で153、Dot 8で比較なし、Dot 12から0と比較する。LYC=0の一致と要求はライン153の途中で起きる |
| 次のライン0 | Dot 0でMode 0・OAM読み出し拒否（VBlank源はまだ有効）、Dot 4からMode 2とOAM源のパルス。0との比較を続け、新しい一致は作らない |

STAT要求は、LYC源・OAM源・VBlank源・HBlank源をORした1本の信号の立上りで出す。LYC源は比較なしの間も前回の結果を保持し、HBlank源はライン144のMode 1の始まりまで、VBlank源はライン0のDot 0まで続く。OAM走査の途中でbit 5を立てても要求しない。ライン144ではMode 1の直前に、HBlank源とOAM源をつないだままLY=LYCを比べる。ライン143の一致はここで終わり、OAM源とHBlank源がどちらも無効なら信号が一度下がって、Mode 1のVBlank源で再び立つ（Gambatte `lycint143_m1irq`・`m1irq_m2enable_lyc`・`m2m1irq_ifw`）。ライン153は内部のDot 2〜5で153、Dot 10から0と比べる（どちらもM-cycleの境界の2 dot後に始まる。Gambatte `lycint152_lyc153irq_late_retrigger`・`lycint152_lyc0irq_ifw`）。LYCの書き込みは1 dot後から比較に効く：SCX=3のHBlank源が立つM-cycleに書くと、LYC源はその立上りと同時に下がり、要求は出ない（Gambatte `lycdisable_ff45_scx3_3`）。153の比較がDot 5まで続くので、ライン153のDot 4の書き込みはまだ153と比べる（`lyc153_late_ff45_enable_*`。CPUの読み出しは4 dotごとで、Dot 5の比較は読み出しでは見分けられない）。DMGではSTAT書き込みの最初の1 Tだけ全源が有効に見え、書いた値は1 T後に効く。成立中の源があり信号が低ければ要求する（Pan Docs、SameBoy、gbmicrotest `stat_write_glitch_*`）。ライン1〜143の始まり（Dot 0）の書き込みは前のラインのHBlank源がまだ信号を保つ間に始まるため、HBlankが有効なら何も要求しない（Gambatte `ff41_disable`・`late_enable_m0disable`・`lycstatwirq_trigger_m0_late`）。LYは読み取り専用。

LCD有効化はライン0のDot 4から始め、そのラインはOAM走査なし（Mode 0、Mode 2割り込みなし、OAM/VRAMは描画開始まで利用可、Spriteを選ばない。SameBoy、Gambatte `enable_display_ly0_sprites_m0stat`）、画素の流れは2 dot遅く始まり（SameBoyの最初のラインと同じ）、452 dot後にLY=1となる（Mooneye `lcdon_timing-GS`）。LCD無効化はLY/Dot/Modeを0にして走査を止め、両画像を白にする。LY=LYC bitは無効中に凍結してLYC書き込みでも変えず、再有効化時にLY=0と比較し直す（Mooneye `stat_lyc_onoff`）。無効中もSTAT書き込みは凍結した結果でLYC源を信号へつなぐ。一致が凍結されていて信号が低ければ書き込みの最初の1 T（全源が有効）で要求し、その後の信号は書いた値のbit 6に従う（Gambatte `lcdoff_lycirqen`）。有効化した後の最初のフレームは表示しない：完成数には数えるが公開せず、白を2枚目の完成まで保つ（Pan Docs、SameBoy、little-things-gb `firstwhite`）。電源投入とResetの起動状態は起動ROMの表示の続きなので、1枚目から公開する。

Mode 3はDMGの画素の流れで描く（Pan Docsの「Pixel FIFO」とSameBoyのDMGの経路）。取得器は背景かWindowのタイル番号・下位・上位を各2 dotで読み、BGのFIFOが空のときに8画素を積む。1 dotに1画素を出し、先頭の8画素（捨てる取得の分）とSCX mod 8の画素は画面に出さない。SCXは取得のたびに、SCYとLCDCのタイル番号の範囲（bit 4）は下位・上位を読むたびに読み直し、SCXの下位3 bitは行の始めだけ使う。背景は2枚の32×32タイルマップ、符号付き/なしタイル番号、2bpp、256画素の折り返しを扱う。Mode 0はDot 253（LCD有効化のラインは255）に、SCX mod 8、Windowの開始の6 dot、OBJの待ちと取得を足した位置で始まり、CPUからは次の4の倍数から見える（Mooneye `hblank_ly_scx_timing-GS`・`intr_2_mode0_timing_sprites`、gbmicrotestのSCX群）。

OBJは、Xの小さい順（同じXはOAM順）に、その位置（画面のX−8）の画素の前で取得する。取得器がいまの背景タイルを終えるまで待ち（FIFOに残るc画素ならc−3 dot。同じタイルで前のOBJが待っていれば待たない）、6 dotで取得する。X=0は最初の画素より前に取得し、待ちを含め11 dot。X≥168は取得しない。OBJの画素はOBJ用のFIFOのうち透明な画素だけを埋めるため、小さいX・OAM順のOBJが勝つ。8×16の行とタイルは、下位・上位を読む時点のLCDCで決める。

Window：Y条件は、Window表示が有効なときにWYがラインと一致すると立ち（Mode 2の始まり（LCD有効化のラインは有効化のとき）と、LCDC・WYの書き込みのときに判定。ラインの開始の2 dotの間にWYを書き換えてもまだ間に合う。Gambatte `window/arg/late_wy*`）、フレームの終わりまで続く（SameBoy。Pan DocsはWindow表示の要否を書かない）。Y条件とWindow表示があれば、位置がWX−7に来たときにBGのFIFOを捨ててWindowの行を取り直し（6 dot）、行カウンターを進める。DMGの特例（Pan Docs）：WX=0は細かいスクロールの前に始まる（画面の左端の列はSameBoyに従う）。WX=166はそのラインに出ず、次のラインから全幅に出る。そのラインの終わりにY条件があれば、Window表示によらず次の画素の流れの開始までWindowの開始を保ち、その間にWindow表示を有効にすると次のラインから出し、無効にすると止める（Gambatte `window/on_screen/wxA6_*`）。行カウンターは開始ごとに進み、保たれたWindowは次のラインの始めにもう1つ進む（Mode 2で止めて再び開始すると1ラインで2行進む）。Mode 3に入ってから（Dot 84〜85）開始したWindowは、そのラインを描いた後で数える。Window表示が無効でY条件があり、Windowの開始位置が背景の取得の境目に当たると、色0の1画素が入って以降が1画素ずれる（nitro2k01の実機の記録。WX mod 8 = 7 − SCX mod 8のとき）。WXを書き換えて同じ位置にもう一度来たときも色0の1画素が入る。LCDC bit 0が無効なら背景/Windowは色0として優先順位を判定し、白で描く（SameBoyはBGPの色0で描く。実機で区別できるテストがないため白を保つ）。

Mode 3中の書き込みは、WXが書いた次のdotから（書いた直後の1 dotだけ、1画素遅れの開始を起こさない）、WYが3 dot後から画素に効く（Gambatte `late_wy*`）。取得器が読むSCX・SCY・LCDCのタイル/マップ/OBJサイズのbitは書き込みの1 dot前から効き、細かいスクロールのSCXの比較は書き込みのdotから（1 dot前の比較を逃すと一周して8 dot延びる。Gambatte `scx_during_m3`・`scx_m3_extend`）。BGP・OBP0・OBP1は1 dot前の1 dotだけ旧値とのORになる。ただしその1 dotが行の最初の画素なら新しい値になる（AGE `m3-bg-bgp`）。LCDCのBG表示は付けるのが1 dot前（OR）、Window表示は消すのが1 dot前（AND）。ただしWindowの開始の判定だけは1 dot前まで古い値を見るので、消す書き込みに捕まった開始はWindowの画素を出さず（取得器はそのdotで背景を読み、色0の挿入もない）、取得のやり直しの6 dotだけMode 3を延ばす（Gambatte `late_disable*`、mealybug `m3_lcdc_win_en_change_multiple_wx`）。最初の画素でBG/OBJ表示を消す書き込みと、OBJの取得中にOBJ表示を消す書き込みも1 dot前から効き、後者は取得をやめて描画を続ける。これらはSameBoyの競合表を、mealybug-tearoom-testsの期待画像（DMG-blobとDMG-CPU Bで違う2件はCPU B）とGambatteのDMG-08の結果で確かめた位置である。起動直後のまま（LCDを入れ直さずに）BGPを書くと、DMG-08では1 dot前の画素が新しい値になるが、このモデルはORのままである（Gambatte `dmgpalette_during_m3`の11件。[既知の失敗](Compatibility.md#既知の失敗)）。

実行の方式：取得器を毎dot回さない。描画の始め（Dot 86、LCD有効化のラインは88）に行の終わりまで計算し、Mode 3中に上のレジスタが書かれたときだけ、確定した位置から書き込みの位置まで計算し直して先を計算し直す。途中で書かれない行は、WindowとOBJが同時にない限り、タイル行の一括展開とOBJの合成で描き、長さを上の規則で求める（取得器との一致は差分テストで確かめる）。Mode 0が始まった後に届く書き込み（Mode 3が終わったM-cycleでのSCX・LCDC・パレット）は、そのラインの最後の1〜2画素に反映しない近似である。完成した行は次のラインの開始で画像へ渡す。

SpriteはY範囲に入るOAM順の先頭10個を選ぶ（画面外Xも枠を消費）。Mode 2のOAM走査は項目iをDot 2+2iで、そのときのOBJサイズで判定する（SameBoy、Gambatte `late_sizechange*`。走査の途中のLCDCとOAMの書き込みは、走査済みの項目に効かない）。8×8/8×16、反転、切り取り、2パレット、色番号0の透明を扱う。小さいX、同じXならOAM順で勝者を決め、その後に背景の**パレット適用前の色番号**と優先順位を判定する。背景に隠れる勝者も下位Spriteを隠す。OAM DMAがOAMを使う間（最初のbyteから最後のbyteまで）に走査した項目は画面外として扱う（Pan Docs。Gambatte `late_sp*`の16件で、転送の始まりと終わりがどの項目の間に来るかを確かめた）。

DMAはFF46を保持し（Reset時FF）、160 bytesを4 Tごとに転送する。FF46へのCPU書き込み時点をt=0とすると、0〜4は書き込みM-cycle、4〜8は開始待ち、t=8でactive、t=12で最初のbyte、t=648で完了。DMGのバスは2系統（カートリッジ/WRAM：0000–7FFF・A000–FDFF、VRAM：8000–9FFF）で、転送中のDMAはOAMと転送元の側を使う。CPUが同じ側を読むと（命令の取得も）、そのM-cycleにDMAが運ぶbyteが見える。書き込みは書いた先へ届かず、そのbyteの代わりにOAMへ入る。転送元がWRAMとその写し（C0–FFのページ）なら読んだbyteとのAND、カートリッジ（ROM・RAM）とVRAMなら書いた値になる（Gambatte `oamdma`の`busyread*`・`busypush*`・`busypop*`・`busywrite*`・`busydelay`のDMG-08の結果）。OAMとFEA0–FEFFは読み出しFF・書き込み無視。もう一方のバス・I/O（FF46を含む）・HRAMは使える（Mooneye `call_timing`等がDMA中にWRAMから命令を読む）。転送中にFF46を書き直すと、新しい転送が始まるまで（開始待ちの間）古い転送を続けるが、読むページと使うバスは書き込みの時点で新しいものに替わる（Gambatte `oamdma_src8000_srcchange0000_*`）。CPUがHALTで待つ間は転送も止まる：HALT直後の最初の待機M-cycleまでは転送し、2つ目から復帰するM-cycleまで止まる（Gambatte `oamdma_late_halt_stat`・`oamdmasrc80_halt_*_read8000`。SameBoyは待機中に止めて復帰時に1 M-cycle分を足すが、HALTの時点で残り1 byteの`late_halt_stat_2`と合わない）。DMA元のE0–FFページはC0–DFへ折り返し（FE・FFはWRAMのDE・DF）、CPUとは別経路で読み、PPUのOAMへ直接書く。

OAMの破損（DMGのOAM bug）：Mode 2のDot 4〜79に、CPUがFE00–FEFFを読み書きするか、16 bitの増減の回路（IDU）がFE00–FEFFの値をアドレスバスへ出すと、PPUがそのM-cycleに読む8 byteの行（Dot 4k〜4k+3は行k）を壊す（Pan Docs、blargg `oam_bug`）。行1〜19が変わり、行0（Dot 0〜3）とDot 80以降は変わらない（`4-scanline_timing`の最初と最後の位置）。IDUを使うのはINC/DEC rr、PUSH・CALL・RST・割り込みの受理のSPの減算、LD SP,HL（SameBoy）で、ADD HL,rr・LD HL,SP+e・ADD SP,eは使わない（`3-non_causes`）。POP・RET・LD A,(HL±)は読み出しとして壊す。書き込み（とIDU）は行の最初のwordを`((a ^ c) & (b ^ c)) ^ c`（aはその行、bとcは前の行の1つ目と3つ目のword）にし、残りの3 wordへ前の行を写す。読み出しは前の行の最初のwordを先に変え、その前の行で、行2・6・10・14・18なら2行前を、行4・8・12・16なら2行前と4行前を上書きしてから、行自身へ写す。行16は行0へも写す。式はPan DocsとSameBoy（`c458e7c5`）のDMGのもので、SameBoyは行4・8・12・16の読み出しが機体で違うと記す。CPUのアドレスと値は結果に関わらない。OAM DMAの転送中は壊れない（実機では確かめていない）。LCD有効化のラインにはMode 2がない。

描画中（Mode 3）のDMAで、OBJの取得がDMAの書いているwordを読む挙動（Pan Docs）と、VRAMからのDMAとPPUのVRAMの読み出しの競合は再現しない（OBJのタイル番号と属性はOAM走査の時点の値を使う。DMGで確かめたテストROMがない）。OAMの破損のうち、Mode 2の直前と終わり（Dot 0〜3・80〜83）の読み出しでCPUのアドレスに依る形（SameBoy）も再現しない。描画の最適化では、取得器と同じ時間と画素を保つ。

## 共通画像とホスト描画

| 契約 | 内容 |
| --- | --- |
| 寸法・配置 | 160×144、左上原点、左→右・上→下、BGRA32、A=255 |
| Stride・長さ | 640 bytes、92,160 bytes。短いコピー先は事前拒否、余分な末尾は保持 |
| 濃淡 | 0/1/2/3をRGB各成分255/170/85/0へ展開。出力パレットは生成時に固定 |
| メタデータ | Width / Height / StrideBytes / Sequence / LcdEnabled |
| 所有権 | 内部配列を公開せずコピーを返す。描画中/完成済みの2バッファを再利用 |

PPUは描き終えた1ライン分の濃淡（0〜3）を次のラインの開始で内部の`WriteLine`へ渡し、VideoOutputが固定パレットの4色表でBGRAへ書く。行番号・長さ・値は書く前に検証する。PPUが全画素を完成させてから`CompleteFrame`で交換する。LCD切り替え・Reset・状態復元でも`Sequence`は巻き戻さない。`CompletedFrameCount`は実際の完成だけで増え、これをFPSに使う。VideoOutput単体はOFF・白、GameBoySystemは起動時にONになる。

実行スレッドと画面のスレッドの間は3枠のスロット（`FrameExchange`、トリプルバッファー）で受け渡す（lib-Platform.LinuxDotNetのAvaloniaの例のスロットプールを参考にした、ロックのない形）。実行スレッドは新しい画像があるときだけ自分の枠へコピーして共有の枠と入れ替え（`Interlocked.Exchange`）、画面のスレッドは共有の枠に新しい画像があるときだけ自分の枠と入れ替えて、その枠から表示へ直接写す。どちらも相手を待たず、取られなかった画像は次の画像で置き換わる。画素のコピーは片側1回ずつ（Coreから枠へ、枠から表示へ）で、共通のlockもUI側の配列も持たない。画面の側は次の画像を取るまで自分の枠を持ち続けるため、写す前にコピーしない。`TryCopyFrame`（自分の配列へ写す形）も同じ枠の上に残し、テストが使う。

表示は`IFrameDisplay`（WPFの要素と`Show`）の二つの実装で、側欄の「Renderer」で実行中に差し替える（既定はSkiaSharp、選択は設定に残す）。SkiaSharpは`SkiaFrameRenderer`で、`SKBitmap(Bgra8888, Opaque)`を再利用し、RowBytes一致なら一括、不一致なら各行の画素部分を`Marshal.Copy`し、`NotifyPixelsChanged`を呼ぶ。サイズ・形式・長さ・確保状態を転送前に検証する。PaintSurfaceは既存画像を描くだけでモデルを進めない。物理ピクセル寸法を使い、最近傍・10:9・整数倍を優先する。WriteableBitmapは160×144の`WriteableBitmap`（Bgr32）のバックバッファーへ写し（待ち時間0の`TryLock`・`AddDirtyRect`）、SkiaSharpと同じ整数倍の矩形（`FrameLayout`）に置いてWPFの合成で最近傍に拡大する。描画スレッドが前の画像を写していてバックバッファーをすぐに取れないときは、`Show`がfalseを返し、ウィンドウは次のフレーム（停止中は0.1秒ごとのタイマー）で最新の画像を渡し直す（UIスレッドは描画スレッドを待たない）。CPUが写すのはウィンドウの大きさによらず92,160 bytes。差し替えでは古い表示を破棄し、新しい表示へ受け渡しの枠にある現在の画像を渡す。実行スレッドと枠は変わらない。画面はUI Automationに名前付きの画像（「Game Boy screen」）として出す。画像境界にSkia型やネイティブポインターを持ち込まない。

生成・更新・描画・破棄はUIスレッドで行う。画面の要素は、枠の中で160×144の整数倍の物理ピクセルに収まる最大の大きさにし、画像が表示の全面を占める。実行中で見えている間だけ毎フレームの描画の呼び出し（CompositionTarget.Rendering）を受け、一時停止・STOP・最小化の間は外して0.1秒ごとのタイマーで状態欄と、1命令・Reset・復元で変わった画像を出す（WPFは変わらない画面を毎フレーム描かない）。元に戻した直後は最新の画像を渡す。DPIだけが変わった場合も配置し直して再描画する。

**画面の構成：** 左は携帯機の本体に似せた部分で、画面とその枠（電源ランプは実行中に赤）、名前、十字キー・SELECT/START・B/A、スピーカーの溝だけを置く（メーカーの名前とロゴは使わない）。ボタンはマウス・タッチ・UI Automationの起動とキーボードで押せ、押している間は色が明るくなる。右は本体にない操作の欄で、ROM（Open ROM・Run demo・Boot ROM）・Run（Resume・Pause・Reset・Rewind）・Save（Save RAM・Save state・Load state・枠）・Sound（Mute・音量・Retry audio）・Display（Renderer）・Message・キーの割り当てを置く。表示は英語の短い語句だけで、説明の文章は出さない。Debug（Step・レジスタ・PPU・周辺・時間・音声のキュー・Memory）はこの欄の閉じた区画にあり、閉じている間はその文字を整形しない。

## ホストの音声出力

音声デバイスを扱うのはホストだけ（Windowsでは、GonFox.GameBoy.Platform.WindowsのWASAPIの実装）。Coreの`AudioOutput`から読んだ48 kHz・ステレオのPCMを、ホストのキュー（`AudioBuffer`）を通して出力する。

```text
EmulationRunner（実行スレッド）── 実行区間ごとにReadFrames ──> AudioBuffer（目標40〜120 ms、上限は目標の2倍）
                                                                 └─ 音声スレッド（WASAPI）が読み出す ──> 既定の出力デバイス
```

| 項目 | 内容 |
| --- | --- |
| 出力 | NAudio 3.1.0（`NAudio.Wasapi`、MIT）のWASAPI共有モード。既定のデバイスへの自動ルーティングを使い、切り替え・取り外しではWindowsが新しい既定のデバイスへ移す。48 kHz・ステレオ・32 bit floatで渡し、デバイスの形式・レートへの変換はオーディオエンジンが行う。要求バッファ30 ms、MMCSSの「Audio」 |
| 受け渡し | 実行スレッドは`RunForTCycles`のたびにCoreのPCMをすべて読み、キューへ書く（Coreのキューは溢れない）。音声スレッドはキューを読むだけで、モデル・UI・ファイルに触れない。両側は1つの短いlockを使い、定常の読み出しでは配列を確保しない |
| 開始と不足 | 再生はキューの目標の量がたまってから始める（プライミング）。目標は40 ms（1,920フレーム）から始まる。不足は無音で埋めて回数とフレーム数を数え、目標を20 ms上げて（最大120 ms）再びプライミングする。不足のないまま30秒再生するごとに目標を10 ms下げる（最小40 ms）。実行スレッドが止まりがちな環境でだけ、その間だけ遅延が増える。目標の2倍を超えた分は古い方から捨てて数える（目標40 msで80 ms） |
| 時計のずれ | 実行速度の基準はStopwatchのまま。デバイスの時計とのずれは、キューの量（約0.5秒で平滑）を目標に保つよう読み出しの速さを最大±0.2%変えて吸収する（4点の3次補間。補正が0なら入力をそのまま出す）。CPU/PPUのTは飛ばさない |
| 停止と再開 | 一時停止・STOP・Reset・ROM切り替え・状態保存/復元・1命令でキューを空にする（未定義命令でCPUが止まっても音は続く）。再生していない間はデバイスを止め、新しいPCMを受け取らない。再開ではCoreに残ったPCM（1命令の分や復元した未取得PCM）を捨て、その地点以降の音をプライミングから鳴らす。読み出しは再生ごとの世代を持ち、前の再生の読み出しには無音だけを返す |
| 音量・ミュート | 音量スライダー0〜100%の2乗を倍率としてサンプルへ掛ける（50%で0.25）。ミュートは倍率0で、読み出しは続ける。どちらもAPUの進行とPCMの生成を変えない。通常終了時に保存し、次の起動で戻す（[ホスト実行と終了](#ホスト実行と終了)） |
| 失敗 | デバイスを開けない、または再生中に止まった（切断など）場合は理由を表示して無音で続け、エミュレーションとPCMの排出は変えない。「Retry audio」で開き直す。自動の再試行はしない |
| 表示 | 再生中/開始待ち（その時点の目標）/停止中、遅延の目安（平滑したキューの量とデバイスのバッファの合計、10 ms単位）、速度補正（100 ppm単位）、不足回数、破棄フレーム数、出力形式。値が変わったときだけ書き換える |

## カートリッジと電池RAM

ホストがROMを読み、`CartridgeLoader.Load(ReadOnlySpan<byte>)`が検証・所有コピー・部品生成を行う。`ICartridge`は実機アドレスの`Read`/`Write`と`ResetController`。不正長・未対応タイプ/容量・CGB専用は例外、ロゴ/ヘッダーチェックサム不一致は警告にする。

| タイプ | 対応容量・保存 |
| --- | --- |
| 00 | ROM Only、ROM 32 KiB、RAMなし |
| 01 | 標準MBC1、ROM 32 KiB〜2 MiB、RAMなし |
| 02 / 03 | 標準MBC1、同ROM容量、RAM 8 / 32 KiB。03だけ電池保存 |
| 05 / 06 | MBC2、ROM 32 KiB〜256 KiB、内蔵RAM 512×4 bit（RAMサイズコードは0）。06だけ電池保存 |
| 0B / 0C / 0D | MMM01、ROM 32 KiB〜8 MiB。0BはRAMなし、0C / 0DはRAM 8 / 32 / 64 / 128 KiB。0Dだけ電池保存。種別・容量・タイトルは最後の32 KiB（起動時のメニュー）のヘッダーで読む |
| 19 | MBC5、ROM 32 KiB〜8 MiB、RAMなし |
| 0F / 10 | MBC3と時計、ROM 32 KiB〜2 MiB（MBC30は4 MiB）。0FはRAMなし、10はRAM 8 / 32 / 64 KiB（64 KiBはMBC30）。どちらも電池保存（RAMと時計） |
| 11 / 12 / 13 | MBC3（時計なし）、同ROM容量。11はRAMなし、12 / 13はRAM 8 / 32 / 64 KiB。13だけ電池保存 |
| 1A / 1B | MBC5、同ROM容量、RAM 8 / 32 / 64 / 128 KiB（RAMサイズコード2 / 3 / 5 / 4）。1Bだけ電池保存 |
| 1C / 1D / 1E | 振動付きMBC5、同ROM容量。1CはRAMなし、1D / 1EはRAM 8 / 32 / 64 KiB。1Eだけ電池保存 |
| FC | ポケットカメラ、ROM 32 KiB〜1 MiB、RAM 8 / 32 / 64 / 128 KiB（写真はRAMバンク0に入るためRAMは必須）。電池保存 |
| FE | HuC3、ROM 32 KiB〜2 MiB、RAMなし / 8 / 32 KiB。電池保存（RAMと時計のMCU） |
| FF | HuC1、ROM 32 KiB〜1 MiB、RAMなし / 8 / 32 KiB。RAMがあれば電池保存 |

ROMは2のべき乗。MBC1は512 KiBを超えるROMに最大8 KiB RAMを組み合わせる。2 KiB RAMなど他の構成、RAMのない種別（01・0B・0F・11・19・1C）でのRAMの宣言、RAMサイズコードが0でないMBC2、4 MiBを超えるか128 KiB RAMのMBC3、128 KiB RAMの振動付きMBC5は拒否する。RAMのある種別（02・03・0C・0D・10・12・13・1A・1B・1D・1E・FF）でRAMサイズが0のROMは、RAMなしとして警告付きで読み込む（blarggの`halt_bug`はMBC1+RAMでサイズ0のまま実機で動く）。そのとき電池はMBC3の時計のためだけに残り、状態の記録には実際の基板の種別（02なら01）を書く。

MBC1：0000–1FFFで下位4bit=AhならRAM有効、2000–3FFFでROM下位5bit、4000–5FFFで上位2bit、6000–7FFFでモードbit 0を設定する。上位ROM窓は上位2bitと下位5bit（ゼロだけ1に補正）を結合し、その後容量マスクを適用する。下位ROM窓はモード0でbank 0、モード1で上位bitを容量マスクする。32 KiB RAMはモード1だけ上位bitで選び、8 KiBは常にbank 0。無効/なしのRAMはFF、書き込み無視。

MBC1M：種別の番号を持たないため、1 MiBのMBC1でbank 10hに2つ目のロゴを持つROMをMBC1Mとして読み込む（Pan Docs。警告に記す。完全な識別は保証しない。MBC5には適用しない）。上位2bitをA18〜A19へつなぎ、下位レジスタのbit 4はつながない：上位ROM窓は上位2bit×10h＋下位4bit、下位ROM窓はモード1で上位2bit×10h。ゼロの補正は下位レジスタの5bit全体で判定するため、10hを書くとそのゲームのbank 0を選ぶ（Mooneye `multicart_rom_8Mb`。本物のMBC1B1のフラッシュカートで確かめたテスト）。状態の記録は標準のMBC1と同じ形。

MBC2：0000–3FFFへの書き込みは、アドレスのbit 8が0ならRAM有効（値の下位4bit=Ahで有効）、1ならROMバンク（値の下位4bit。0は1に補正し、その後容量マスク）を設定する。4000–7FFFにレジスタはない。A000–BFFFは内蔵の512個の4bitのRAMを512 bytesごとに繰り返し、読むと上位4bitは1（SameBoy、MBC2Aの実機で確かめたMooneye `mbc2/ram`）。無効のRAMはFF、書き込み無視。

MBC5：0000–1FFFで下位4bit=AhならRAM有効（Pan Docs・binjgb。SameBoyは0Ahだけ）、2000–2FFFでROMバンクの下位8bit、3000–3FFFで9bit目（bit 0だけ）、4000–5FFFでRAMバンク（下位4bit）を設定する。6000–7FFFにレジスタはない。0000–3FFFは常にbank 0、4000–7FFFは9bitのバンク（0も選べる）を容量マスクした位置。RAMは8 KiBのバンクを容量マスクして選ぶ。無効/なしのRAMはFF、書き込み無視。振動付き（1C〜1E）では、RAMバンクのレジスタのbit 3がモーターで、bit 0〜2だけがRAMバンクを選ぶ（Pan Docs）。レジスタは4bitのまま持つため、状態の記録にモーターも入る（形式は変わらない）。Resetでモーターは止まる。モーターは環境中立の`IRumbleCartridge.MotorOn`で読め（`CartridgeInfo.HasRumble`で振動付きか分かる）、WPFのサンプルは状態の行に「Rumble」と出す（MAUI Androidなら端末の振動へ割り当てられる）。判定できるテストROMはなく、Pan Docsの規則の単体テストで確かめる。

MMM01（Pan Docsのtauwasser氏の調査に従う）：複数のゲームを載せるためのMBC1で、4つの7 bitのレジスタがMBC1のbitにゲームを選ぶbitを足す。0000はbit 0〜3がRAM有効（下位4bit=Ah）、bit 4〜5がRAMバンクのマスク、bit 6が対応付けの開始。2000はbit 0〜4がROMバンクの下位、bit 5〜6が中位。4000はbit 0〜1がRAMバンクの下位、bit 2〜3が上位（Pan Docsの図とSameBoy。見出しの「1〜2」は図と食い違う）、bit 4〜5がROMバンクの上位、bit 6がモードの書き込みの禁止。6000はbit 0がMBC1のモード、bit 2〜5がROMバンクのマスク、bit 6が多重化。電源投入とResetで全レジスタを0にし、対応付けの前は0000–7FFFが最後の32 KiB（メニュー）を見せる。対応付けの前は全bitを書け、後はRAM有効・ROMとRAMのバンクの下位・モードだけを書ける。マスクのbitが立つと対応する下位のバンクのbitを固定する（対応付けの前後とも）。対応付け後の4000–7FFFは上位:中位:下位のバンク（固定されていない下位のbitがすべて0なら1を足す）、0000–3FFFは同じバンクでその下位のbitを0、A000–BFFFはRAMバンクの上位:下位（モード0では固定されていないbitを0）。多重化はRAMバンクの下位とROMバンクの中位を入れ替える（大容量のMBC1の配線と同じ）。Pan Docsが決めていない点は、対応付けの前もRAMが使える（SameBoy・MAME。対応付け後と同じバンク）、対応付けの前の4000–7FFFは最後のバンク（Pan Docsの図）、対応付けを始める書き込みでRAMバンクのマスクも設定する（SameBoy）とした。メニューが先頭にあるダンプ（SameBoyは並べ替える）と、0100だけに0B〜0Dのヘッダーがあるイメージは拒否する。

HuC1（Pan Docs）：2000–3FFFで4000–7FFFのROMバンク（6 bit）、4000–5FFFでRAMバンク（2 bit）を選び、どちらもカートリッジの容量で折り返す。0は1に補正しない（SameBoyと同じく0のバンクも見える）。RAM有効のレジスタはなく、0000–1FFFへ0Ehを書くとA000–BFFFが赤外線のレジスタに、ほかの値でRAMになる（値の全体で判定。SameBoyは下位4bitだけ）。6000–7FFFにレジスタはない。赤外線のレジスタは、読むと受光なしのC0h（相手の機器を接続しない）、書くとbit 0でLEDを点ける（環境中立の`IInfraredCartridge.LedOn`で観測できるだけで、ほかへは届かない）。電源投入とResetではROMバンク1・RAM・LED消灯（SameBoy）。

ポケットカメラ（Pan Docs Gameboy_Camera）：2000–3FFFで4000–7FFFのROMバンク00〜3Fh（0も選べ、電源投入とResetでは1。上位2bitは使わない、Gambatte）、4000–5FFFでRAMバンク00〜0Fh、bit 4が1ならA000–BFFFがカメラのレジスタ（80hごとに繰り返す）。RAMはいつでも読め、0000–1FFFへ下位4bit=Ahで書き込みが有効になる（SameBoy・Gambatte）。レジスタはいつでも書け、A000はbit 0〜2を持ち、A001〜A07Fは00を読み、A036〜A07Fへの書き込みは無視する（SameBoy）。A000のbit 0へ1を書くと撮影を始め、終わるまで1を読み、その間のRAMは00を読んで書き込みを無視する。0を書くと撮影を止め（時間も止まる）、次の1で残りの時間と始めたときの設定で続ける（Gambatteは最初からやり直し、SameBoyは止められない）。撮影の時間は4×（32,446＋（A001のbit 7が0なら512）＋16×露光）Tで、始めたアクセスから数える（SameBoy・Gambatteはさらに2 M-cycle、奇数のM-cycleで始めると1 M-cycle長い）。HALT中も数え（Gambatte。SameBoyはHALTで止める）、STOPでは本体の時間とともに止まる。画像は撮影を始めたときに、ホストが渡した画像とその時点のレジスタから、Pan Docsのエミュレーター向けのサンプルコードの手順（露光・ディザの行列・縁の強調。サンプルが扱わない縁の強調の3・C・D・Fの方式はGambatteに合わせ、ほかの扱わない方式はそのまま通す。利得とA005は使わない、Pan Docsの勧め）で2 bitのタイルにし、撮影の終わりにRAMバンク0のA100〜AEFFへ書く。毎Tの処理は持たず、A000–BFFFへのアクセス・`ExportRam`・状態の保存の時点で本体の累積Tから進める。センサーが見る画像は環境中立の`ICameraCartridge.SetImage`（128×112の輝度、0が黒・255が白）でホストが渡し、渡さないか`ClearImage`の後は一様な灰色（128）を見る（ノイズにはしない）。`EmulationRunner.SetCameraImageAsync`がこれを実行スレッドで渡す。WPFのサンプルは画像を渡さない。

HuC3（Pan Docs HuC3）：0000–1FFFの下位4bitでA000–BFFFに見せるものを選ぶ。0はRAM（読み出しだけ）、AhはRAM、Bhは時計のMCUへの命令の受け口（書き込み）、Chはその命令と結果（読み出しは`80h｜命令<<4｜結果`）、Dhはセマフォ（bit 0を0にして書くと命令を実行し、読むと81h：MCUは忙しくならない、SameBoy）、Ehは赤外線（読むとC0h、LEDは書き込みのbit 0で、何にもつながない）、ほかの値は読むとFFで書き込みを無視する。レジスタを見せている間はA12〜A0とD7を見ない（窓のどのアドレスも同じレジスタで、bit 7は書いても失われ読むと1）。2000–3FFFでROMバンク（7 bit、0も選べる）、4000–5FFFでRAMバンク（2 bit）を選び、どちらも容量で折り返す。6000–7FFFにレジスタはない。電源投入とResetではROMバンク1（SameBoy・mGBA・Gambatte）、選択0。MCUは256個の4 bitのメモリーを持ち、命令は1（読み出して番地を進める）・3（書き込んで番地を進める）・4/5（番地の下位/上位）・6（拡張：0は時計の分・日を00〜05へ写し、1は10〜15から時計へ戻してイベントの時刻（58〜5D）を同じ分だけずらし、2は1を返す）。命令2は番地を進めずに書く。0・7とほかの拡張（音を含む）は何もしない（SameBoyの扱い）。時計は1日の分（12 bit、1440で翌日へ。1440以上を書くと日を進めずにFFFまで数えて0へ戻る）と日（12 bit）で、写す幅は6 nibble（mGBA。06は変えない）。時刻はエミュレーションの時間だけで数え（1分は251,658,240 T）、命令・保存・状態の時点でまとめて進める。Resetは電池で動くMCU（メモリー・時計・番地・受け口）を保つ。環境中立の`IHuc3ClockCartridge`が、MCUのメモリー（1 byteに1 nibble）と現在の分に数えた秒の書き出し・読み込み、秒数を足す`AdvanceClock`、プログラムがMCUを変えた回数（`ClockChanges`。時計の進みと時刻の写しは数えない）を与える。

MBC3：0000–1FFFで下位4bit=AhならRAMと時計が有効（Pan Docs「MBC1とほぼ同じ」、SameBoy）、2000–3FFFでROMバンク7bit（MBC30は8bit。0は1に補正し、その後容量マスク）、4000–5FFFで選択（下位4bit。上位は無視、SameBoy）、6000–7FFFで時計のラッチを設定する。選択の00〜07はRAMバンクで、RAMの容量でマスクする（MBC3のRAMは最大32 KiBなので4〜7は0〜3と同じ。MBC30の64 KiBで8バンク）。08〜0Cは時計のレジスタ（秒・分・時・日の下位8bit・DH）、0D〜0Fと時計のない型（11〜13）の08〜0FはFFで書き込み無視。ROM 4 MiBかRAM 64 KiBの宣言でMBC30とする（SameBoy）。無効/なしのRAMと時計はFF、書き込み無視。

MBC3の時計（タイプ0F・10）：秒6bit・分6bit・時5bit・日9bit・停止（DHのbit 6）・桁あふれ（bit 7）。1秒はエミュレーションの4,194,304 Tで、実時計を使わない。書き込みは各レジスタのbitだけを保持し、秒の書き込みは1秒未満の経過を0に戻す（分・時・日・DHの書き込みは戻さない）。停止中は1秒未満の経過も止まる。1秒ごとに秒を1増やし、60で0にして分へ、分は60で時へ、時は24で日へ桁上がりする。範囲外の値は桁上がりせずにレジスタの幅で0へ戻る（秒・分は63→0、時は31→0。60〜62分・24〜30時は+1）。日が511→0で桁あふれを立て、0を書くまで保つ。ゲームが読むのはラッチした値で、6000–7FFFへ00の直後に01を書くとその時点の値を写す（Pan Docs。01だけ・00以外の後の01では写さない。電源投入時は写さない状態）。以上はrtc3test v004が実機で確かめる規則で、そのROMで検証する（[互換性と検証](Compatibility.md#rtc3test)）。

ResetControllerは、MBC1・MBC2では生バンクレジスタ=0、MBC3ではROMバンク=0（1を選ぶ）・選択=0、MBC5ではROMバンク=1・9bit目=0・RAMバンク=0にし、いずれもRAM無効に戻してRAMは保持する（MMM01・HuC1・カメラ・HuC3は上の各規則のとおり）。MBC3の時計は電池側の回路として動き続け、ラッチの値とラッチの途中状態も保つ。新規RAMは0初期化、新規の時計は0日0:00:00・動作中。電池保存の種別（03・06・0D・0F・10・13・1B・1E・FC・FE・FF）の`IBatteryBackedCartridge.ExportRam`は全バンクの所有コピー（0Fと、RAMのないFEは0 bytes）、`ImportRam`は全長検証後にコピーし、失敗時は不変。MBC2は1 byteに1つの4bitで、Importは下位4bitだけを使う（上位を1で保存する道具もある）。いずれもバンク制御を変えない。タイプ0F・10の`IRealTimeClockCartridge`は、`ExportClock`で現在値とラッチ値（各5レジスタ）を返し、`ImportClock`で両方を設定し（各レジスタのbitだけ。1秒未満は0から）、`AdvanceClock(秒)`で停止中でなければ秒数分を進める（範囲外の値の間は1秒ずつ、範囲内は日と残りの秒をまとめて計算。結果は1秒ずつ進めた場合と同じ）。

`EmulationSession`はROM切り替え・RAM保存・定期保存・終了を直列化する。電池保存のROMは、まずROMごとのロック（`<SHA-256>.lock`を共有なしで開いたまま持つ。プロセスが終わればOSが閉じて消える）を取り、取れなければ使用中の`IOException`で切り替えを中止する（現在のROMは止めない）。新ROMの検証とロックの後に旧ROMを止めて保存し、新RAMの読込/Importが成功してから交換し、旧ROMのロックを放す（電池のないROMへの切り替えでも放す）。同じROMの再読込には直近RAMと時計を引き継ぐ。途中失敗では旧モデルを停止して保持し、RAMコピーも保存再試行に残す。ロックは終了時の保存の後に放す。

定期保存：ウィンドウが5秒ごとに、実行を止めずにRAMの写しを取り、最後に書いた（または読み込んだ）内容とSHA-256で比べる。違いを見つけた次の確認でも同じ内容（5秒以上変化なし）なら保存する。変化が続く場合も、最初に違いを見つけてから60秒で保存する。比べるのはRAMだけで、時計だけの変化では書かない（時計はファイルの時刻で合う）。HuC3はRAMとMCUの変更の回数（`ClockChanges`）を合わせて比べ、目覚ましの設定など、プログラムがMCUへ書いた変化も保存する（時計の進みでは書かない）。読み込みや手動の保存の最中は飛ばし、失敗は`SessionNotice`（`SaveFailed`・`Automatic`）で知らせて次の確認で再試行する（実行は止めず、RAMはカートリッジに残る。WPFのサンプルは保存の行に表示する）。電池のないROMでは何もしない。

セーブはSHA-256名の`<SHA-256>.sav`に全RAM（MBC2は512 bytes、ほかは8,192 / 32,768 / 65,536 / 131,072 bytes）を書く。時計付き（0F・10）はRAMの後に48 bytesを続ける：現在値5つとラッチ値5つ（秒・分・時・日の下位8bit・DH）を各4 bytesのリトルエンディアン、保存時刻のUNIX秒を8 bytes（VBA-M・BGB・SameBoyと同じ並び）。読み込みは時刻が4 bytesの44 bytes形式と、時計の記録のないRAMだけのファイル（時計は新規のまま）も受け付け、ほかの長さは拒否する。HuC3はRAMの後にmGBAと同じ136 bytesを続ける：MCUのメモリーを2 nibbleずつ（偶数番地を下位）128 bytes、今の分が始まった時刻のUNIX秒を8 bytes（256 nibbleをすべて残せる形式はこれだけ）。読み込みはこの形式、SameBoyの17 bytes（分・日・目覚ましを対応する番地へ写し、時刻はホストの分に切り捨てる）、RAMだけのファイルを長さで見分け、閉じていた時間をMBC3と同じく足す。書くのはいつもmGBAの形式（SameBoyはこの形式を読めない）。時計の記録があれば、保存時刻から今までの秒数（ホストの時計。負なら0）を`AdvanceClock`で加え、`SessionNotice`の`ClockSeconds`で知らせる（WPFのサンプルは保存の行に「clock +N s」と出す）。保存は同じディレクトリーの一時ファイルへ書き`Flush(true)`後、既存は`File.Replace`で直前の世代を`<SHA-256>.sav.bak`へ移し、初回は`File.Move`で確定する。確定の後、書いた内容のSHA-256を`<SHA-256>.sav.sha256`へ同じ方法で書く。読み込みで記録と食い違えば（外部での置き換えか破損）、`BatteryLoadNotes.ChangedOutside`で知らせて読み込みは続ける（WPFのサンプルは保存の行に「changed outside, backup kept」）（記録のないファイルは判定しない）。失敗では元ファイルを残し、一時ファイルの削除を試みる。Windowsのホストの保存先（`WindowsStorage`）は`%LOCALAPPDATA%/GonFox.GameBoy/Saves`で、環境変数`GONFOX_GAMEBOY_SAVES`で別のディレクトリーにできる（自動の確認が利用者のセーブを変えないため）。通常終了は保存成功までClosingをキャンセルし、失敗時にモデルを破棄しない。強制終了では最後の保存より後の変化を失う。

## 観測・実行状態

`GetDebugSnapshot`はCPU・ZNHC/IME/EI/HALT/STOP、Timer/IF/IE、PPU位置/スクロール、DMA、実効MBCバンク（MBC3で時計のレジスタを選んでいる間は、RAMバンクの欄に選択の08〜0F。WPFのデバッグ欄は10進で8〜15）を値で返す。WPFは約100 ms間隔で表示し、値が変わった行だけを整形・設定する（一時停止・STOP中は文字列を作らない。デバッグ欄を閉じている間はその行を整形せず、開いたときに今の値で全行を整形する）。メモリー取得は同じ命令境界のコピーとT/PCを返す。実行中の自動追従はなく、取得・ステップ・Reset・状態操作・ROM読み込みで更新する。

`CaptureState`は命令/実行呼び出し境界でモデルを変えずに取得する。CPU命令の途中は対象外だが、周辺回路の処理途中も保持する。

| 保存対象 | 必要な状態 |
| --- | --- |
| メタデータ | 形式12（ほかの形式は拒否）、DMG、起動の方式（`BootBypass-v2`か`BootRom-v1`）、ROM SHA-256・種別、累積T |
| CPU・Bus・MBC | 全レジスタ・IME/EI/HALT/HALT直後/STOP/HALT bug・CPUの停止（未定義命令のアドレスとコード）、WRAM/HRAM・対応付け中のBoot ROM、全外部RAMと生バンク制御（MBC1：下位5bit・上位2bit・モード、MBC3：ROMバンク・選択、MBC5：ROMバンクの下位8bit・9bit目・RAMバンク、共通：RAM有効）。MBC3の時計：現在値・ラッチ値（各5レジスタ）・1秒未満の経過T・直前のラッチの書き込みが00か。MMM01の4つのレジスタ（書いた値）、HuC1のバンク・赤外線の選択・LED、カメラのバンク・選択・RAMの書き込み有効・36個のレジスタ・止めた撮影・残りのT・現像した画像（3,584 bytes）、HuC3のバンク・選択・LED・MCU（メモリー256 nibble・分の中の経過・番地・命令・引数・結果）。共通の記録のうち、ほかの種別の部分（時計・カメラ・MCU）は空でなければ拒否する |
| IRQ・Timer | IF/IE、DIV内部、TIMA/TMA/TAC、再ロード待ち/衝突窓 |
| PPU・DMA | VRAM/OAM・LCDレジスタ・走査ライン/Dot/Mode・LY=LYC bit/LYC源/STAT信号（LCD無効中の凍結値を含む）・選択済みSprite（X・Y・タイル・属性）とOAM走査の位置・WindowのY条件・LCD有効化のラインか、画素の流れ（次の段とそのdot、取得器、BG/OBJのFIFO、位置とLCDの位置、Windowの行・列と途中の状態、OBJの取得の途中、最後の画素の位置でOBJの取得を始めたdot、WX=166の保持と後で数えるWindowの行）と描画中の1ライン。Mode 3の終わりとHBlank源の位置は保存せず、復元時に計算し直す。命令の境目では、書いたLYCと細かいスクロールのSCXは比較に効いた後なので保存しない。DMA元/位置/開始・再開待ち/位相 |
| Joypad・Serial・APU | 押下/行選択/STOPに入った後の線の立下り、転送の値/クロック/ビット・完了キュー/破棄数、APUのレジスタ/Wave RAM・電源・次の段・最初の立下りの無視・保留中のスイープのクロック、各Pulseの動作/出力bit（開始直後の0出力を含む）・duty位置・次の進行までのT・長さ・音量・包絡線・スイープの影/タイマー/減算済み、Waveの動作/DAC/出力レベル/周期/次の読み込みまでのT/位置/読み込み済みbyte/最後に読んだT・長さ、NoiseのNR43/LFSR/次のクロックまでのT・長さ・包絡線、混合のサンプル位相/途中の和/左右のコンデンサー |
| VideoOutput・AudioOutput | 描画中・完成済みの両画像、LCD有効状態、有効化後の最初のフレームか、固定パレット。未取得のPCM（最大2,048フレーム）と破棄数 |

`GameBoyState`は外部へ配列・可変セッターを公開しない所有コピー。ROM自体は複製せず同一内容を要求する。内蔵のカートリッジ（ROM Only・MBC1・MBC2・MMM01・MBC3・MBC5・カメラ・HuC1・HuC3）だけが内部の`IStatefulCartridge`を実装し、未接続・独自カートリッジの保存/復元は明示的に拒否する。

`Serialize`はバイト列を返し、`Deserialize`は読み戻す。並びは識別子`DMGSTATE`、形式番号（Int32）、レイアウトの指紋（8 bytes）、機種・起動の方式・ROMのSHA-256（文字列）・種別・累積T、各部品の状態（CPU・Bus・カートリッジ・IRQ・Timer・Serial・Joypad・APU・AudioOutput・DMA・PPU・VideoOutputの順）。部品の状態は位置指定の記録型で、コンストラクターの引数の順に値を書く（反射で列挙するため、項目を足すと書く側・読む側とも自動で変わる）。整数はリトルエンディアン、配列と文字列はInt32の長さ（配列のなしは−1）の後に要素、参照の記録型と省略可能な値は有無の1 byteを前に置く。レイアウトの指紋は、各記録型の項目の名前と型を深さ優先に並べた文字列のSHA-256の先頭8 bytes。`Deserialize`は識別子（違えば`InvalidDataException`）、形式番号と指紋（違えば`NotSupportedException`）、長さ（要素は100万個・文字列は256 bytes・全体は8 MiBまでで、残りのバイト数を超えないことを確保の前に確かめる）、フラグが0か1か、余りのないことを確かめる。値の範囲と部品の間の整合は、`RestoreState`が変更前に検証する。**互換の方針：** 状態のバイト列は、同じ形式番号と指紋のビルドだけが読む。状態の項目を変えた版は、それより前の状態ファイルを読み込まず、変換もしない（理由を表示し、ファイルは消さない）。長く残す進行は電池セーブ（.sav）が担う。

Restoreは識別情報、全配列長・値範囲・PPU/STAT/DMA/LCD整合性（走査位置とMode、比較値とLY=LYC bit、各源とSTAT信号の一致。LCD無効中はLYC源とSTAT信号の一致、LCD有効化のラインは選択済みSpriteなし、最初のフレームの印はLCD有効中だけ）・HALT直後の印はHALT中だけ・APUの整合性（動作中のチャネルはDAC ON、電源OFFなら全チャネル停止、最初の立下りを無視する待ちはDIV bit 12が1の間だけ、保留中のスイープのクロックは電源ONで4 T以内、混合のサンプル位相が累積Tと一致）・PCMキュー（偶数長・2,048フレーム以下）を**変更前に**検証する。不適合ではモデルと画像番号を保持する。適合後は各ブロックへ直接コピーし、バス書き込み・Tick・Resetを経由しない。同一データを何度でも復元でき、同じROMの別システムにも使える。スレッド・イベント・Skiaリソースは保存しない。

未取得PCMと破棄数は保存対象で、復元すると保存時のキューに戻る。SequenceとCompletedFrameCountは保存対象外。復元はSequenceを1増やして画像を公開し、完成フレーム数は変えない。WPFはROMごとに9枠の状態ファイルと巻き戻しの記録を持ち（[状態の枠と巻き戻し](#状態の枠と巻き戻し)）、枠からの復元と巻き戻しの後、および枠への保存の後は一時停止する。状態復元は電池ファイルへ即時保存せず、次の保存（手動・定期・終了）から復元後RAMを保存する。

ホストは復元（枠と巻き戻しを含む）で時刻予算/FPS・旧画像・旧入力の世代をリセットする。観測/ステップ中のJoypadは保存時のまま保ち、その間のホスト入力は保持だけにする。再開時に現在のキーを`SynchronizeButtons`で新しい押下として渡し、古いSTOP復帰要求を消す。コア単体のRestoreはこの同期を行わず、同じ入力系列の再現性を保つ。CPUが止まった状態も保存・復元でき、止まる前の状態へ戻せば命令の実行を再開する。

## 状態の枠と巻き戻し

ホストの保存は`IRecordStore`（名前付きのバイト列の読み込み・丸ごとの置き換え・一覧・削除・ロック）を通す。置き換えでは前の版を別の名前に残せ、読む側は前か後の丸ごとのバイト列だけを見る。名前はASCIIの英数字と`.` `-` `_`だけ（先頭は`.`以外、160文字まで）で、保存先の外を指せない。ファイル版`FileRecordStore`は呼び出し側が渡すディレクトリーに、一時ファイル→`Flush(true)`→`File.Replace`（前の版を残すときはその名前へ）、初回は`File.Move`で書き、一覧から一時ファイルを除く。ロックは共有なしで開いたままのファイルで、プロセスが終わればOSが閉じて消す。メモリー版`MemoryRecordStore`は同じ契約をメモリーで行い（出し入れはコピー）、テストに使う。WPFでは`MainWindow`（組み立ての起点）が電池セーブと状態の枠のディレクトリーを決めてファイル版を渡す。電池セーブ（`BatterySaveStore`）もこの上にあり、ファイルの名前と挙動は[カートリッジと電池RAM](#カートリッジと電池ram)のとおり。

状態の枠（`StateSlotStore`）はROMごとに9枠で、名前は`<ROMのSHA-256>.slot<N>.state`。Windowsのホストの保存先は`%LOCALAPPDATA%/GonFox.GameBoy/States`で、環境変数`GONFOX_GAMEBOY_STATES`で別のディレクトリーにできる（自動の確認が利用者の状態ファイルを変えないため）。1枠は、識別子`SESTATE1`、保存時刻（UNIXミリ秒）、Coreのバイト列のSHA-256、実行を止めたエラー文（UTF-8、Int32の長さ、1,024 bytesまで。CPUの停止は状態に含まれ、ここには書かない）、Coreのバイト列（Int32の長さ）。読み込みは識別子・長さ・ハッシュ（違えば`InvalidDataException`）、Coreの形式と指紋（違えば`NotSupportedException`）、ROMのSHA-256（違えば`InvalidDataException`）を確かめ、`RestoreState`の検証を経てから実行を置き換える。どれかが合わなければ実行中の状態は変えない。「Save state」は一覧で選んだ枠へ実行を止めて書き、「Load state」はその枠から戻して一時停止する（空きの枠では押せない）。枠の一覧（保存日時）はROMの読み込みと枠への保存のたびに読み直す。電池のないROMはロックしないため、同じROMを2つのウィンドウで開いて同じ枠へ保存すると後の方が残る（ファイルは丸ごと置き換わる）。

巻き戻しは、実行中に1エミュレーション秒（4,194,304 T）ごとに、その時点の実行区間の終わりで状態を記録し、最大30個（約30秒分）を持つ（実行スレッドのメモリーだけで、ファイルには書かない）。「巻き戻し」は、今より0.5秒以上前の記録のうち最も新しいものへ戻して一時停止し、それより新しい記録を捨てる。続けて押すとさらに約1秒ずつ戻り、再開するとそこから記録を続ける。ROMの切り替え・Reset・枠からの復元で記録を消す（別の時間の流れになるため）。STOP・一時停止の間は記録しない。未定義命令でCPUが止まっても時間は進むため記録を続ける（止まってから約30秒以内なら止まる前へ戻れる）。デバッグ欄の時間の行は、最も古い記録からの秒数を「巻き戻し N秒」と出す。巻き戻しも電池ファイルを直ちには書き換えず、次の保存（手動・定期・終了）から戻した後のRAMを保存する。

## ホスト実行と終了

`EmulationRunner`の1スレッドが全モデル操作を所有する（Windowsのスレッド名「Game Boy emulation」。デバッガー・トレース・停滞の再現で識別する）。キューで受けた操作を命令境界で適用し、UIを同期呼び出ししない。Stopwatchの実時間×4,194,304から予算を求め、最大4,096 Tずつ実行する。小数と命令超過を持ち越し、蓄積100 ms超過は捨てて遅延に表示する。機械のサイクルは飛ばさない。

「Open ROM」のファイルダイアログは専用のSTAスレッドで開く。そのスレッドの見えないウィンドウ（メインウィンドウが所有し、同じ位置に置く）がダイアログを所有し、開いている間はメインウィンドウを無効にする。ダイアログのスレッドは、ダイアログが隠れる直前にメインウィンドウを有効に戻して前面に戻し（このプロセスが前面を持つときだけ）、所有の関係を外す。選択はFileOk、キャンセルはFileOkのないまま隠れることで知る。その後のダイアログの後始末（シェル拡張の解放。このPCでは数秒）はそのスレッドだけが待ち、UIスレッド・ROMの開始・終了を止めない。起動引数の1つ目がROMのパスなら、「Open ROM」と同じく読み込んで実行する（`--`で始まる引数は除く）。「Choose boot ROM」も同じダイアログで256 bytesのファイルを選び、次のROMの読み込みかResetから電源投入の状態でBoot ROMを実行する（`EmulationRunner.UseBootRomAsync`。読めない・大きさの違うファイルは何も変えない）。「No boot ROM」で既定（起動後の状態）に戻す。起動の方式は側欄の行に、Boot ROMの対応付け中は状態の行に「Boot ROM」と出す。

音量・ミュート・ウィンドウの通常の位置と大きさ・最大化・描画方式・Boot ROMのファイルのパス（260文字まで）は、Windowsのホスト（GonFox.GameBoy.Platform.Windowsの`HostSettingsStore`）が通常終了（電池RAMの保存の成功後）に`%LOCALAPPDATA%/GonFox.GameBoy/settings.json`へ書き（一時ファイルから`File.Replace`）、起動時に読む。ファイルがない・読めない・壊れている場合は既定値（音量70%、ミュートなし、既定の位置と大きさ、SkiaSharp、Boot ROMなし）にし、範囲外の値は項目ごとに既定値へ戻す。ウィンドウの上端の帯（タイトルバー）が仮想画面に160×16ピクセル以上入らない場合は、保存した位置と大きさを使わず、既定の位置と大きさで開く（モニターの間の隙間は考えない）。使う場合も大きさは最小値と仮想画面の大きさの間に収める。Boot ROMのファイルは起動時（起動引数のROMより先）に読み直し、読めなければ理由を表示してBoot ROMなしで始め、次の保存ではパスを書かない。保存の失敗は終了を止めない。環境変数`GONFOX_GAMEBOY_SETTINGS`は別のファイルを指定する（自動の確認が利用者の設定を変えないため）。一時停止/STOPではイベント待ちし、操作・入力で起床する。再開・Reset・状態操作は時刻基準を更新する。CompositionTarget.Renderingは画像表示だけを担い、描画頻度で実行速度を変えない。入力はキー別名・マウス・タッチID・UI Automationの「Invoke」（画面のボタン。100 msの押下）を合成し、キャプチャ/フォーカス喪失・Reset・ROM交換で解除する。組み込みデモ（Background demo・Mega demo・Sound check・RPG demo）はホストのリソースで、選んだものを「Open ROM」と同じ経路で読み込む。

通常終了は停止・電池RAM保存に成功してから、音声デバイスの停止・解放、ワーカー終了/Join、描画イベント解除、Skia破棄を行う。ワーカーはUIやTask継続の完了を待たず、未処理コマンドは完了または破棄エラーになる。

## MAUI Androidのホスト

Example.GameBoy.MauiHostは、CoreとGonFox.GameBoy.Platform.Sharedを変えずに使うAndroid（8.0以上）のサンプルで、縦向きの1画面に、名前と状態・画面・コマンドの行・メッセージ・画面のボタン（または設定）を並べる。組み立ての起点は`MainPage`（WPFの`MainWindow`にあたる）。UIの処理はすべてUIスレッドで行い、コマンド・起動・起動の値の処理は1つずつ順に行う（処理中のコマンドのタップは捨て、起動の値は順番を待つ）。

| 項目 | 内容 |
| --- | --- |
| 画面 | SkiaSharp.Views.Mauiの`SKCanvasView`（CPUで描く）。8 msごとのタイマーで`FrameExchange`の新しい画像だけを取り、`SKBitmap`（Bgra8888、Opaque）へ写して再描画を頼む（描くのは次の表示の周期）。一時停止中は新しい画像がないため描かない。配置はWPFと同じ`FrameLayout`（物理ピクセルで最大の整数倍、最近傍）。Pixel 9a（1080×2424）では6倍の960×864 |
| 画面のボタン | 十字キー・B・A・SELECT・STARTを1つの`SKCanvasView`に描き、複数の指を同時に扱う。十字キーは中心からの角度を45度ずつの8方向に分け、斜めでは2方向を押す（中心のごく近くと、腕の長さの1.35倍より外は押さない）。A・Bは円の半径の1.5倍、SELECT・STARTは周りを少し広げた範囲で受ける。指ごとに別の入力元（`touch:<指のID>:x/y/b`）として`ButtonInputState`で合成し、指が動けば押すボタンが変わり、離すかシステムが取り消すと離す。背景へ移る・設定を開く・ROMや状態を替えるときは全部離す。押したときに触覚（`HapticFeedbackConstants.VIRTUAL_KEY`。端末の「タップの振動」の設定に従い、権限は要らない）を返す（設定で切れる） |
| 音 | `AudioTrackDevice`：AudioTrack（用途Game・種類Music、48 kHz・float・ステレオ、ストリーム、低遅延の指定）。バッファは最小値と480フレームの大きい方（Pixel 9aでは60 ms）。専用のスレッドが`AudioBuffer`を240フレーム（5 ms）ずつ読んでブロッキングで書き、トラックのバッファが読み出しの速さを決める（WASAPIと同じ）。停止はpauseとflush。開けない・止まった場合は理由を出して無音で続ける（WPFと同じ`AudioPlayback`）。音量（0〜100%の2乗の倍率、既定70%）とミュートはWPFと同じで、端末の音量ボタンはメディアの音量を変える |
| 振動カートリッジ | 振動付きMBC5のモーター（`EmulationStatus.Rumble`）を状態の更新（0.1秒ごと）で読み、動いている間は読むたびに150 msの振動を出し直し、止まれば取り消す（`RumbleMotor`。読むのが途絶えても0.15秒で止まる）。ゲームの振動として出し（Android 13以上は用途Mediaで端末の「メディアの振動」の設定に従い、それより前は用途Game）、タップの振動とは分ける。VIBRATEの権限を使う。設定で切れる |
| 置き場所 | アプリの内部の領域（`Context.FilesDir`。ほかのアプリから読めず、アンインストールで消える）に、電池セーブ（`Saves`）と状態の枠（`States`）をWindowsと同じ形式で、中断の状態（`Resume`。ROMごとに1つ）、最後に開いたROMと選んだBoot ROMのコピー（`Library`の`last-rom.gb`・`boot-rom.bin`）を置く。ファイルの選択で選んだ文書はアプリへ一時的に渡されるだけで、再起動の後には読めないため、コピーを持つ。Androidの自動バックアップは使わない（ROMとBoot ROMのコピーを外へ送らない） |
| 設定 | `Preferences`：音量・ミュート・ボタンの触覚・振動カートリッジ・使うBoot ROMの名前・最後に開いたもの（`demo:<番号>`か`rom:<名前>`）・背景へ移ったときに実行中だったか。範囲外の値は既定値にする |
| 起動 | 起動の値（下記）があればそれを、なければ最後に開いたもの（ROMは`Library`のコピーを電源投入の状態から、電池RAM付きで）を、それもなければ背景デモを開く。その前に、Boot ROMの設定があれば`Library`のコピーを使う（読めなければ理由を出して設定を消す）。Androidが背景でプロセスを終えた後にアクティビティを戻す（保存した状態を持って作られる）ときは、最後に開いたものを開いてから中断の状態を戻し、実行中だったなら再開する。起動の値やROMが開けなくても、最後に開いたものか背景デモを開いてから理由を出す |
| 背景と復帰 | 背景へ移る（ホーム・画面の消灯・ファイルの選択の画面）と、入力を離し、音と振動を止め、一時停止して電池RAMを保存し、中断の状態を書き、表示のタイマーを止める。どれかが失敗しても残りは行う。戻ると、背景への保存が終わるのを待ってから（保存は実行を止めるため）、実行中だったときだけ再開する。保存の途中で戻ってまた離れたときは、前の保存の後に次の保存を行い、実行中だったかは前の保存の答えを引き継ぐ。電池RAMはWPFと同じ定期保存（5秒ごとの確認）もする。ウィンドウが閉じられたら（BACKなど。背景へ移る処理のすぐ後に来る）、背景への保存が終わるのを待ってから実行スレッド・音声・振動を解放し、次のアクティビティには新しい画面を作る |
| コマンド | ROM（`FilePicker`。8 MiBまで）、Demo（4つから選ぶ）、Pause/Resume、Reset、State（枠1へのSave・Load と Rewind）、Settings（ボタンの代わりに出す：Mute・Volume・Button haptics・Rumble・Choose boot ROM / Remove boot ROM。Boot ROMは256 bytesのファイルを`Library`へコピーし、次のROMの読み込みかリセットから使う） |
| 記録 | logcatのタグ`GonFox.GameBoy`に、作成・起動の値・読み込み・メッセージ・セッションの保存・音量・振動の切り替え・Boot ROMの対応付けの変化・背景と復帰、5秒ごとの状態（FPS・T・Boot ROMの対応付け・音声の状態とキューの量・不足と破棄の数）、ボタンの押下と離し（Debug）を書く |

**起動の値（intentのextra）：** `adb shell am start -n <パッケージ>/<アクティビティ>`に、`--ei demo 0〜3`（組み込みデモ。一覧の順で、3がRPGデモ）、`--es rom <パス>`（アプリが読めるファイル。端末の`Android/data/com.gonfox.gameboy/files`にadbで置ける）、`--es bootrom <パス>`か`none`、`--ez muted true|false`、`--ei volume 0〜100`を付ける。起動中のアプリへは`OnNewIntent`（singleTop）で届く。保存した状態から戻されたアクティビティと、最近のアプリから開き直したアクティビティは、最初の起動の値を繰り返さない。インストール直後の最初の起動は、Pixelではシステムの更新の画面を経由してextraが落ちるため、確認では一度起動してから値を送る。

## 参照

実機との差異は[互換性と検証](Compatibility.md)に残す。[Pan Docs](https://gbdev.io/pandocs/)（音声は[Audio](https://gbdev.io/pandocs/Audio.html)）、[SM83命令表](https://gbdev.io/gb-opcodes/optables/)、[MBC1](https://gbdev.io/pandocs/MBC1.html)、[MBC3](https://gbdev.io/pandocs/MBC3.html)、[MBC5](https://gbdev.io/pandocs/MBC5.html)、[OAM DMA](https://gbdev.io/pandocs/OAM_DMA_Transfer.html)、[Timer](https://gbdev.io/pandocs/Timer_Obscure_Behaviour.html)を基準にし、テストで確認したモデルの仕様と区別する。
