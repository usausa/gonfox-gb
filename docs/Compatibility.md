# 互換性と検証

GonFox.GameBoyはDMGだけを対象とし、Coreの各ブロックを単体テスト（Unit）と、公開テストROM・自作ROMを実行するテスト（Rom）で検証する。プラットフォーム層とWPFのサンプルは単体テストで、Androidのサンプルは実機の確認の手順で確かめる。2026-10-08の回帰確認では、GonFox.GameBoy.Core.Tests 2,199件（Unit 1,870・Rom 329）、GonFox.GameBoy.Platform.Tests 133件、Example.GameBoy.WpfHost.Tests 42件の合計**2,374件がすべて成功し、スキップは0**で、Releaseビルドは警告0である。選定したROMの成功は、一般のゲームやテスト集全体の互換性の保証ではない。既知の失敗・近似・未対応は[既知の失敗](#既知の失敗)と[既知の近似と未対応](#既知の近似と未対応)にまとめる。挙動の定義は[現行仕様](Specification.md)と[CPU命令表](CpuInstructions.md)、サンプルの使い方は[サンプルの使い方](Examples.md)、性能の測り方は[ベンチマーク](Benchmark.md)にある。

## 再実行

```powershell
dotnet build GonFox.GameBoy.slnx -c Release
dotnet run --project GonFox.GameBoy.Core.Tests -c Release --no-build
dotnet run --project GonFox.GameBoy.Platform.Tests -c Release --no-build
dotnet run --project Example.GameBoy.WpfHost.Tests -c Release --no-build
```

テストのプロジェクトはMicrosoft.Testing.Platformの実行ファイル（xunit.v3）である。Coreの単体テストだけなら`dotnet run --project GonFox.GameBoy.Core.Tests -c Release --no-build -- --filter-trait "Category=Unit"`、ROMのテストだけなら`Category=Rom`を指定する。通常のビルドと起動の手順は[README](../README.md)にある。

テストが使うROMはすべてリポジトリーにある。公開テストROMは`Roms/External/<テスト集>/`に、配布物から取り出したファイル（ROM・期待画像・ソース・シンボル）を置き、許諾表示と、取得元・固定版・ファイルごとのSHA-256・成功条件を記したマニフェストを添える。テストは読むファイルのSHA-256をマニフェストと照合し、不足・不一致はエラーにする（無条件にスキップしない）。ビルドは`Roms/External`と自作ROMをテストの出力の`TestData`へ写す。テストはネットワークへ接続せず、外部の道具も起動しない。公開ROMをソースから組み立て直したときのバイナリーの再現性は確かめていない。

## 固定版と許諾

| 配布物 | 固定版 | マニフェスト・許諾 |
| --- | --- | --- |
| Mooneye Test Suite | `mts-20260714-0944-31510e1`、ソース`31510e12eea6286d36eea060a6adde755e1067aa` | [マニフェスト](../Roms/External/mooneye-manifest.json)（取得URL・ハッシュ・上限）、[MIT](../Roms/External/Mooneye-LICENSE.txt) |
| dmg-acid2 | v1.0、ソース`dc2295408f881637ff69f784d8d93f3d2db30181` | [マニフェスト](../Roms/External/dmg-acid2-manifest.json)（ROM・参照PNG・ソース）、[MIT](../Roms/External/DmgAcid2-LICENSE.txt) |
| gbmicrotest | コミット`463eb6bc0fe31d61781ef63060ad6d74090c0255`（MIT許諾の追加後）。アーカイブに同梱の構築済みROMとソース | [マニフェスト](../Roms/External/gbmicrotest-manifest.json)（選定128件）、[MIT](../Roms/External/GbMicrotest-LICENSE.txt) |
| mealybug-tearoom-tests | コミット`70e88fb90b59d19dfbb9c3ac36c64105202bb1f4`。ROMの圧縮ファイル（2019-10-24の構築）から取り出したROM、DMGの期待画像、ソース | [マニフェスト](../Roms/External/mealybug-manifest.json)（成功条件・上限）、[MIT](../Roms/External/Mealybug-LICENSE.txt) |
| rtc3test | v004、ソース`80ae792bf1b3c3387929b912e6df001af3511e24`。リリースのROMとシンボル、ソース | [マニフェスト](../Roms/External/rtc3test-manifest.json)（成功条件・上限・項目の一覧）、[Unlicense](../Roms/External/Rtc3Test-LICENSE.txt) |
| game-boy-test-roms（AGE・little-things-gb・SameSuite） | c-sp/game-boy-test-roms v7.0、コミット`8e1f6d7f3a1d8683f11fdf23008d1b1b26e51b52`。構築済みROMと期待画像の圧縮ファイル（SHA-256 `b9a9d7a1…3832c`）から、使う20ファイルを元のパスのまま取り出したもの | [マニフェスト](../Roms/External/game-boy-test-roms-manifest.json)（ファイル・成功条件・上限）、[MIT（まとめ）](../Roms/External/GameBoyTestRoms-LICENSE.txt)、[MIT（AGE `cd3f654d13`）](../Roms/External/AgeTestRoms-LICENSE.txt)、[zlib（firstwhite `9d168486e1`）](../Roms/External/Firstwhite-LICENSE.txt)、[MIT（SameSuite `f71b4b3c`）](../Roms/External/SameSuite-LICENSE.txt)。同じまとめのGambatteのhwtests（GPL-2.0）とblarggのテスト（許諾の記載なし）は含めない |
| SameBoyのDMGのBoot ROM | v1.0.3の`sameboy_winsdl_v1.0.3.zip`（SHA-256 `66fb05ac…`）の`dmg_boot.bin`（SHA-256 `6f64da4c…`） | [マニフェスト](../Roms/External/sameboy-boot-manifest.json)（成功条件・上限）、[MIT](../Roms/External/SameBoy-LICENSE.txt) |
| 自作ROM | 各READMEのROM（とシンボル）のSHA-256 | [背景デモとMBC1版](../Roms/BackgroundDemo/README.md)・[セーブカウンター](../Roms/SaveCounter/README.md)・[音声確認ROM](../Roms/SoundCheck/README.md)・[メガデモ](../Roms/MegaDemo/README.md)・[RPGデモ（USAGI QUEST）](../Roms/RpgDemo/README.md)のREADMEに生成ソース・操作・観測ラベル。許諾は各フォルダーの`LICENSE`（MIT）。メガデモとRPGデモの参照画像の入力列・選択規則は各フォルダーの`reference.json` |
| 自作テストROM | 各ROMのSHA-256（`reference/<ROM>.json`） | [テストROM集](../Roms/TestSuite/README.md)のREADMEにROM・ケース・既知の違い・除いたもの。許諾は`LICENSE`（MIT）。期待値（SameBoy）と参考のbinjgbの結果は`reference/<ROM>.json` |
| RGBDS v1.0.4（メガデモ・RPGデモ・テストROM集の再生成だけ） | `rgbds-win64.zip`のSHA-256 `bd1d7e38…f97e6`と`rgbasm`・`rgblink`・`rgbfix`（RPGデモは`rgbgfx`も）の各ハッシュ | MIT。各フォルダーの`Build.ps1`が`.cache/rgbds`へ取得・照合する |
| binjgb v0.1.11（メガデモ・RPGデモの参照画像、メガデモの音の比較、テストROM集の参考の結果だけ） | `binjgb-windows.tar.gz`のSHA-256 `86ebb0f6…f3e66`と`binjgb-tester.exe`・`binjgb.exe`のハッシュ | MIT。各フォルダーの`Reference.ps1`とメガデモの`Compare-Audio.ps1`が`.cache/binjgb`へ取得・照合する |
| SameBoy（テストROM集の期待値だけ） | ソース`c458e7c5`（v1.0.3）のアーカイブ（SHA-256 `6aba4ce1…2e8c3`）から組み立てた`sameboy_tester` | MIT。`Roms/TestSuite/Reference.ps1`が`.cache/sameboy-src`へ取得・照合し、Visual StudioのClangで組み立てる |

RGBDS・binjgb・SameBoyは道具で、生成物（ROM・シンボル・参照PNG・期待値のJSON）だけを管理する。どれも通常のビルド・テストには不要で、テストは道具を起動しない。

## テストの構成

Romカテゴリ（329件）の内訳：

| 区分 | 件数 | 内容 |
| --- | ---: | --- |
| Mooneye Test Suite | 92 | 選定したacceptanceとMBCのケース |
| gbmicrotest | 128 | 注記にDMGを含むケース |
| mealybug-tearoom-tests | 27 | 24ケースと、描画中の状態の保存・復元の3件 |
| dmg-acid2 | 3 | 命令ステップ・4,096 Tの予算の2件と、走査の途中からの復元 |
| rtc3test | 1 | 3メニュー23項目 |
| game-boy-test-roms | 17 | AGE 13・`firstwhite` 1・SameSuite 2・ファイルと許諾の照合1 |
| SameBoyのBoot ROM | 3 | ファイルと許諾の照合、引き渡しの状態、その後のMooneye `boot_regs-dmgABC` |
| 自作ROM | 48 | 背景デモ系8・メガデモ18・音声確認ROM 1・RPGデモ21 |
| 自作テストROM | 10 | ROMごとに1件（[自作テストROM](#自作テストrom)） |

Unitカテゴリ（1,870件）が保持する範囲：

| 領域 | 確認内容 |
| --- | --- |
| CPU | 全通常/CB命令の条件成立/不成立・フラグ・スタック・時間。DAAは独立した10進演算の40,000組。IDUでOAMを壊す命令と壊さない命令 |
| Timer・割り込み・入力・Serial | DIV/TACの書き込み、再ロードと衝突の窓、入力のエッジ、転送と観測の副作用、シリアルのクロックとDIVのカウンターの位相。受理途中のIEの書き換え、EI直後のHALTの戻り先、受理でのIFの標本化と解除、IF書き込みの位置、HALT直後の標本化、未定義命令でのCPUの停止（時間は進み、割り込みで再開せず、Reset・復元で戻る）。STOPの4通りの分岐と選んだ行の線の立下りでの復帰、HALT中の早送りが1 M-cycleずつと同じ状態になること（4種のプログラム×4通りの実行予算）、起動直後の位相とP1 |
| PPU・DMA | LY・STATの境界（LY更新とMode 2の1 M-cycle差、ライン144・153、STAT書き込み、LCD無効中のLY=LYCの凍結、LCD有効化のラインと最初のフレームの非表示）、OAM/VRAMの読み書きの窓、Windowの行、Spriteの選択・透明・優先。画素の流れ（SCX mod 8・Window・OBJでのMode 0とHBlankの位置、描画中の書き込みが効く位置、Windowの特例とWX=166の保持、項目ごとのOAM走査）。DMAの開始・再開・制限・転送元、DMA中の同じバスの読み書き・HALT中の停止・OAM走査、FEA0–FEFFの読み出し、OAMの破損の行と式。CPUの制限を迂回するデバッグ参照 |
| 描画の差分 | 旧来の画素ごとの合成（タイル取得・BGP・全OBJの探索）を参照実装として、乱数のVRAM・OAM・LCDC・スクロール・WX/WY・パレットのフレームを全画素で比べる（Windowの特例に当たる入力は除く）。途中で書かれない行の一括描画と取得器の段を、画素とMode 0の位置で比べる |
| 画像の受け渡し | 色成分・四隅・Stride・alpha、未完成の画像の分離、コピーの所有権、LCD・Reset・復元の通知 |
| APU・PCM | Pulseのduty・長さ・包絡線・スイープ、Waveの読み込み順・遅れ・出力レベル・再生中のWave RAM・再トリガーの書き換え、NoiseのLFSRの系列と間隔、左右の混合・DAC・高域通過・切り詰め、1秒48,000フレーム、キューの境界、T-cycleごとの参照実装との全フレームの比較、分割実行・同期点によらない同一のPCM |
| MBC・Boot ROM | 全対応容量・モード・バンク、0の補正とマスクの順、全RAM、Resetでの保持、Importのサイズと所有権。MBC1M、MBC2、MBC3/MBC30と時計、MBC5（9 bitのROMバンク・16 RAMバンク・振動）、MMM01・HuC1・ポケットカメラ・HuC3、ヘッダーのRAMの食い違い。Boot ROMの重ね合わせ・FF50・電源投入の状態 |
| 状態 | 全ブロック・全RAM・両画像と、DMA・Timer・Serial・PPU（描画中のライン）・APU（未取得のPCMを含む）の途中、HALT bug・STOP・EI・CPUの停止の保存と復元。範囲外の値・別のROM・旧形式の拒否で原状を保つこと。8つの場面でバイト列の往復とその後の実行が一致し、壊れた・別の形式・別の指紋のバイト列と、乱数で1〜3 bitを壊した300通りは決めた例外か正常な復元になる。状態の変換が反射で使う記録型の一覧が状態の構成と一致すること |

プラットフォームとサンプルのテスト：

| プロジェクト | 確認内容 |
| --- | --- |
| GonFox.GameBoy.Platform.Tests（133件） | 実行スレッド（時刻予算・FPS・操作のキュー・STOPとCPUの停止の間の操作・Boot ROM・カメラの画像・終了）、入力の合成、音声のキュー（プライミング・不足・上限・時計のずれへの追従・確保なし・目標の自動調整）と再生の制御・故障時の無音での継続・実行スレッドが渡すPCMとCoreの一致、電池ファイル（時計・HuC3を含む）と定期保存・前の世代・ハッシュの記録・ロック、保存の抽象（ファイル版・メモリー版）・状態の枠・巻き戻し、フレームの3枠、メモリーの表示、設定ファイル、Windowsの置き場所と環境変数 |
| Example.GameBoy.WpfHost.Tests（42件） | 状態の行とデバッグの行（変わった行だけを作り直すこと）、保存の結果・CPUの停止・音声の出力の文言、SkiaSharpの描画（RowBytes 640/672、最近傍の整数倍・余白・縮小、更新、独立したコピー、破棄）、WriteableBitmapの表示（画素と整数倍の矩形、大きさの変更、最初の画像の前、UI Automationの画像）、描画方式の設定とウィンドウの位置の判定、組み込みデモがコミット済みのROMであること |

## Mooneye Test Suite

通常のローダーと`GameBoySystem`を使い、B/C/D/E/H/L=`3/5/8/13/21/34`の状態で実際に`LD B,B`を実行したらPASS、すべて42hならFAILとする（先の命令を見つけるだけでは成功にしない）。STOP・CPUの停止（未定義命令）・T上限・実時間上限は別の診断を返し、PC・レジスタ・T・Serial・診断用のHRAMを残す。上限は1ケース50,000,000 T・実時間60秒。PPUを含む通常の描画の経路を通り、ROM名による例外やLY・SCの書き換えはない。固定版の[serial_send_byte.s](https://github.com/Gekkio/mooneye-test-suite/blob/31510e12eea6286d36eea060a6adde755e1067aa/common/lib/serial_send_byte.s)は4,096 Tの転送完了の前にタイムアウトし得るため、全件をレジスタ方式で判定する。Serial送信の完全性はMooneyeからは主張せず、単体テストとSCの完了を待つ自作ROMで確かめる。選定92件はすべてPASSする。

| 区分 | ケース |
| --- | --- |
| CPU | `bits/reg_f`、`instr/daa` |
| 起動後の状態 | `boot_div-dmgABCmgb`、`boot_hwio-dmgABCmgb`、`boot_regs-dmgABC` |
| 割り込み・HALT・EI | `ei_sequence`、`ei_timing`、`if_ie_registers`、`intr_timing`、`interrupts/ie_push`、`halt_ime0_ei`、`halt_ime0_nointr_timing`、`halt_ime1_timing`、`halt_ime1_timing2-GS`、`di_timing-GS`、`rapid_di_ei`、`reti_intr_timing` |
| Timer | `div_timing`、`timer/`の`div_write`・`rapid_toggle`・`tim00`・`tim00_div_trigger`・`tim01`・`tim01_div_trigger`・`tim10`・`tim10_div_trigger`・`tim11`・`tim11_div_trigger`・`tima_reload`・`tima_write_reloading`・`tma_write_reloading` |
| シリアル | `serial/boot_sclk_align-dmgABCmgb` |
| OAM DMA | `oam_dma/basic`・`oam_dma/reg_read`・`oam_dma/sources-GS`、`oam_dma_start`、`oam_dma_restart`、`oam_dma_timing` |
| PPU | `ppu/`の`stat_lyc_onoff`・`vblank_stat_intr-GS`・`lcdon_timing-GS`・`lcdon_write_timing-GS`・`stat_irq_blocking`・`intr_1_2_timing-GS`・`intr_2_0_timing`・`intr_2_mode0_timing`・`intr_2_mode3_timing`・`intr_2_oam_ok_timing`・`hblank_ly_scx_timing-GS`・`intr_2_mode0_timing_sprites` |
| 命令のアクセス時刻 | `add_sp_e_timing`、`call_cc_timing`、`call_cc_timing2`、`call_timing`、`call_timing2`、`jp_cc_timing`、`jp_timing`、`ld_hl_sp_e_timing`、`pop_timing`、`push_timing`、`ret_cc_timing`、`ret_timing`、`reti_timing`、`rst_timing` |
| MBC（`emulator-only/`） | `mbc1/`の`bits_bank1`・`bits_bank2`・`bits_mode`・`bits_ramg`・`ram_64kb`・`ram_256kb`・`rom_512kb`〜`rom_16Mb`の6件・`multicart_rom_8Mb`（MBC1M）、`mbc2/`の`bits_ramg`・`bits_romb`・`bits_unused`・`ram`・`rom_512kb`・`rom_1Mb`・`rom_2Mb`、`mbc5/`の`rom_512kb`〜`rom_64Mb`の8件 |

MBC以外は`acceptance/`以下。容量名はbit単位（16 Mb=2 MiB、256 kb=32 KiB）。命令のアクセス時刻のうち`add_sp_e_timing`・`call_cc_timing`・`call_timing`・`jp_cc_timing`・`jp_timing`・`ld_hl_sp_e_timing`・`ret_cc_timing`・`ret_timing`・`reti_timing`の9件は、VRAMからのDMA中にWRAM（エコー領域）の命令を読むため、DMA中の2バスの分離を要する。`oam_dma/sources-GS`はOAM DMAの転送元（ROM・VRAM・MBC5のRAM・WRAM・E000〜FFFFのWRAMの写し）からの転送を比べる。固定版のMBC5の試験（`common/harness/mbc5_rom.s`）は、ROMB1へ書く値のbit 0を0にしたままバンク0〜255を切り替えるため、9 bit目（ROMB1=1）とバンク256〜511は単体テスト（`Mbc5Tests`。全容量でのバンク0〜511の折り返しを含む）で確かめる。

選んでいないもの：`boot_*`の他機種版（DMG以外の起動後の状態）、成功するが選定の範囲外の`bits/mem_oam`・`bits/unused_hwio-GS`、機種固有か目視用の`madness/`・`manual-only/`。

## gbmicrotest

各ROMはFF80に実測値、FF81に期待値を書き、最後にFF82へ01（成功）かFF（失敗）を書く。命令境界ごとにFF82を見て、01をPASS、FFをFAILとする。上限は1ケース4,000,000 T・実時間60秒で、最長の`is_if_set_during_ime0`でも約157万Tで終わる。選定したケースはLCDを無効→有効にして位相をそろえるか割り込みで同期し、BootBypassの起動の位相に依存しない。

READMEは全ケースを実機（DMG-CPU-08とみられる）で確かめたとするが、ソース先頭の注記は`pass - dmg`・`pass - ags`・空欄に分かれる。LCD・STAT・割り込み・HALT・OAM/VRAMのアクセスの境界に関わるケースのうち、注記にDMGを含むものだけを選んだ。同梱の構築済みROMは、ソースのNOP数・遅延ループの定数・inc列・割り込みベクターの位置・比較する期待値・SCXへ書く値をバイト列で照合してから採用した。選定128件はすべてPASSする。

| 群（マニフェストの`group`） | 件数 | 主なケース |
| --- | ---: | --- |
| line153 | 21 | `line_153_lyc0_stat_timing_a`〜`n`、`line_153_lyc153_stat_timing_a`〜`f`、`line_153_lyc0_int_inc_sled` |
| stat-write | 14 | `stat_write_glitch_l0_*`・`l1_*`・`l143_*`・`l154_a`〜`c` |
| lcd-on | 15 | `lcdon_to_ly*`、`lcdon_to_lyc*_int`、`lcdon_to_oam_int_l1/l2`、`lcdon_*_to_vblank_int_*` |
| stat-irq | 18 | `oam_int_*`（HALT・NOP・IF端・inc列）、`lyc1_int_if_edge_*`、`hblank_int_l1/l2/scx0`、`hblank_int_di_timing_*` |
| vblank-irq | 14 | `vblank_int_*`・`vblank2_int_*`（IF端・NOP・inc列） |
| access | 30 | `oam_read_l1_*`、`oam_write_l0/l1_*`、`vram_read_*`、`vram_write_*` |
| halt | 2 | `halt_op_dupe`、`is_if_set_during_ime0` |
| mode3-scx | 14 | `hblank_int_scx1`〜`7`、`hblank_scx2_if_a`、`hblank_scx3_if_a`〜`d`、`hblank_scx3_int_a`・`b` |

除外したケースと理由：

| ケース | 理由 |
| --- | --- |
| `line_153_lyc_a`〜`c` | 構築済みROMの遅延がソースと一致しない（aとcは同一のバイナリー、aとbは期待値以外が同一） |
| `stat_write_glitch_l154_d` | ほかのケースにある`clear_if`がなく、確認済みの`lcdon_*_to_vblank_int_*`が示すVBlankのIFと矛盾する期待値E0 |
| `halt_op_dupe_delay` | 期待値DIV=$55が、テスト自身の約60 M-cycleの遅延と整合しない |
| `line_153_ly_*`、`oam_int_if_level_*`、`lcdon_to_oam_unlock_*` | 確認の注記がAGSだけ |
| 注記が空欄のケース（`poweron_*`を含む）、Timer・DMA・MBC・APU系 | 実機で確かめた記録がないか、選定の範囲外 |

参考に全513件を実行すると478件がPASSし、FAILは上の除外のうち`halt_op_dupe_delay`・`line_153_lyc_b`・`stat_write_glitch_l154_d`の3件だけで、残る32件は結果を書かない表示用のテストやMBC5を要求するものである。注記のない`poweron_*`、LCDを入れ直さずに起動直後の位相から測る`hblank_int_scx*_if_*`・`hblank_int_scx*_nops_b`、`line_65_ly`、LCDを入れた直後のラインの`int_hblank_*`なども成功するが、注記がないため選定に加えない。

## mealybug-tearoom-tests

Mode 3中にPPUのレジスタを書き換え、それが効く位置を画像で見るテスト集。期待画像は作者（Matt Currie氏）のエミュレーターのもので、作者は21件をDMG-blob、3件をDMG-CPU Bの実機の写真と照らしている（写真のない`m2_win_en_toggle`・`m3_scx_high_5_bits`・`m3_scy_change`も選んだ）。`LD B,B`を実行した時点の最新の完成画像を、期待画像と全画素で比べる。22件はDMG-blobの画像を、DMG-blobとDMG-CPU Bで画像が違う`m3_lcdc_bg_en_change`・`m3_lcdc_win_en_change_multiple_wx`はCPU Bの画像（blobとは境目が1画素ずれ、228画素・3画素が違う）を期待する。上限は1ケース4,000,000 T・実時間60秒で、最長の`m3_wx_4_change_sprites`でも約106万Tで終わる。24件すべてが一致する。

期待画像にはDMGのBoot ROMがVRAMに残すもの（ヘッダーのロゴのタイル1〜24、®のタイル25、マップの9904〜990F・9910・9924〜992F）が写るため、テストは最初の命令の前に同じ内容を状態へ書く（BootBypassの起動状態は変えない。実Boot ROMの実行でも同じ内容が残ることはSameBoyのBoot ROMで確かめた）。圧縮ファイルのROMは2019-10-24の構築で、ソースはその後に置き場所だけ変わったため、ROMとソースはハッシュで個別に固定した。ほかの3件（`m3_lcdc_obj_en_change`・`m3_scy_change`・`m3_wx_4_change_sprites`）は、書き込みのあるラインの描画中に各命令境界（CPU自身の書き込みの時刻）で状態を取り、復元した後の2フレームが全ブロックで一致することを確かめる。

## dmg-acid2

起動後の完成12〜14枚目を、命令ステップと4,096 Tの予算の両方で、公式のDMGの参照PNG（`reference-dmg.png`）と全23,040画素・BGRAの全成分で比べ、Resetの後も同じ比較をして差分0である。上限は1起動5,000,000 T・1ケース実時間60秒。参照PNGは固定のグレースケール形式を.NETだけで展開し、Core.TestsへWPF・Skia・ネイティブのデコーダーを持ち込まない。3件目は、走査の途中からの状態の復元と入力の再実行で同じ画像へ進むことを確かめる。

## game-boy-test-roms

c-sp/game-boy-test-roms v7.0から、DMGの実機で確かめた結果を持つAGE・`firstwhite`・SameSuiteのテストを使う。マニフェストの全ファイルと許諾のSHA-256を照合する（照合の1件）。上限は1ケース実時間60秒。

### AGE

DMG-CPU C向けの名前で、作者がDMG-CPU-08でも確かめた14件。`LD B,B`（40h）の実行まで進め、レジスタのテストはB/C/D/E/H/L=3/5/8/13/21/34、画像のテストは`-dmgC.png`と全画素を比べる。上限は30エミュレーション秒。13件（`halt/`の`ei-halt`・`halt-m0-interrupt`・`halt-prefetch`、`ly`、`m3-bg-bgp`・`m3-bg-lcdc`・`m3-bg-scx`、`oam-read`、`stat-interrupt`、`stat-mode`・`stat-mode-sprites`・`stat-mode-window`、`vram-read`）を選び、すべて成功する。`oam-write-dmgC`は既知の失敗。

### little-things-gb `firstwhite`

DMGとMGBで白と記録されたテスト。最初の0.5エミュレーション秒（2,097,152 T）に完成した画像がすべて`firstwhite-dmg-cgb.png`（白）と一致すれば成功で、LCD有効化後の最初のフレームを表示しないことを確かめる。成功する。

### SameSuite

LIJI32/SameSuite `f71b4b3c`（2022-04-10）の`apu/div_write_trigger`・`apu/div_write_trigger_10`。同梱の説明でCGBより前の機種が成功するとされる2件（DMG-B・blobで確認）で、ほかのAPUのテストはCGBのPCMレジスタで結果を読むため選ばない。`LD B,B`まで実行し、B/C/D/E/H/L=3/5/8/13/21/34なら成功。上限は30エミュレーション秒。2件とも成功する。

まとめに含まれるMooneyeのwilbertpol版（2016年。未定義命令0xEDで終わる）は選定しない。参考にCPUの停止を終わりとして実行すると121件中95件が成功する。

## rtc3test

MBC3の時計を、rtc3test v004が実機で確かめる内容（[tests.md](https://github.com/aaaaaa123456789/rtc3test/blob/80ae792bf1b3c3387929b912e6df001af3511e24/tests.md)）で検証する。ROMはメニューをボタンで選んで実行し、結果を9C00のタイルマップに書く。結果の文字のタイル番号には、ROM自身の判定が合格$40・不合格$80としてORされる（`src/main.asm`の`RunTests`。DMGでは合格が黒、不合格が薄い灰色）。テストはプレイヤーと同じくボタンを押し（Aで実行、下で次のメニュー、Aで戻る。6フレーム押して6フレーム離す）、マップを読んで、3メニュー23項目すべてが合格の色で、N/Aがなく、項目名がマニフェストの一覧と一致することを確かめる。時間の許容範囲はROM自身のもので、テストは時間を判定しない。上限は251,658,240 T（60エミュレーション秒）とホストの120秒で、3メニューの実行は約44エミュレーション秒かかる。

| メニュー | 項目（すべて合格） |
| --- | --- |
| Basic tests | RTC on・Tick（1000.0 ms。許容999.0〜1001.0 ms）・RTC off・Register writes・Seconds increment・Rollovers・Overflow・Overflow stickiness |
| Range tests | All bits clear・All bits set・Valid bits・Invalid value tick・Invalid rollovers・High minutes・High hours |
| Sub-second writes | RTCS/500・RTCS/900・RTCM/50・RTCM/600・RTCH/200・RTCDL/800・RTCDH/300・RTC off/400（許容はそれぞれ±1.5 ms） |

時計の単体テスト（`Mbc3RtcTests`）は内部の時計のTを直接進めて1 T単位で確かめ、まとめた進めと1秒ずつ進める参照を範囲外の値を含む300通りで比べる。電池ファイルの時計の欄（48 bytes。古い44 bytesの欄と時計のないファイルも読む）と、閉じていた時間の加算はGonFox.GameBoy.Platform.Testsが確かめる。

## SameBoyのBoot ROM

`GameBoySystem.UseBootRom`で渡したBoot ROMを電源投入から実行する経路を、SameBoy v1.0.3のDMGのBoot ROMで確かめる。`Roms/External/sameboy`には、Windows向けの配布物から取り出した`dmg_boot.bin`（`BootROMs/dmg_boot.asm`をRGBDS v1.0.4で組み立てても同じbytes）とMITの許諾を置く。成功条件はソースから決めた：電源投入から7,022,400 T（100フレーム）以内にFF50を書いて0100に至り、AF 01B0・BC 0013・DE 00D8・HL 014D・SP FFFE、LCDC 91・BGP FC・SCY 00・NR50 77・NR51 F3・NR52 F1、FF50はFFを読み0000–00FFはカートリッジ、LY 144のMode 1（最後のフレーム待ちのVBlankの直後）、タイル1〜24にヘッダーのロゴ（0104〜0133の各bitを2倍にしたもの）、タイル25に®、マップの9904〜990Fが01〜0C・9910が19・9924〜992Fが0D〜18で、ほかの9800〜9BFFは0。どのnibbleの値も通るよう、ロゴの位置には決まった模様を置く。続けてMooneye `boot_regs-dmgABC`がSameBoyのBoot ROMの後で成功する。`boot_div`はBoot ROMの処理の長さが任天堂のものと違うため対象にしない。エミュレーターはBoot ROMを同梱しない。

## 自作ROM

期待値は各READMEに定義した規則・式・ROMの表と、別エミュレーター（SameBoy・binjgb v0.1.11）の結果から決める。このエミュレーター自身の出力を後から正解として固定したものはない。

### 背景デモ（ROM Only版・MBC1版）

全画素の仕様値、入力によるスクロール・非表示、命令ステップと異なる予算での同一の境界、全メモリーと電池RAM、保存と復元を確かめる（8件）。14の入力イベントを最大1,800,000 Tまで与え、両版が同じ最終画像になる。入力は要求した時刻ではなく、実際に到達した命令境界へそろえる。MBC1版は入力で変えた位置のExport/Importと再起動後の画像も確かめる。

### メガデモ

[メガデモ](../Roms/MegaDemo/README.md)の18件（映像11件・音楽7件）：

| 観点 | 確認内容 |
| --- | --- |
| 入力 | 最小0・最大7、押した読み取りでの1段と20回後・以後6回ごとのリピート、左右・上下の打ち消し、A/B/Startの押下エッジ、停止中も入力を受けること、演出ごとの保存値、Selectと同じ読み取りの入力が新しい演出へ効くこと、Windowの状態表示の文字 |
| 画素 | 波（振幅5/7・配色1/3・反転）、プラズマ（強さ6/5、模様0と、4回の64 bytesの転送を終えた模様2）、広がり0の軌道を、表示中の角度から式で計算した全23,040画素と比べる。状態表示の16ラインもフォントと値から計算する |
| Sprite | 4模様でOAM 40個の座標・図柄・属性が式どおり。広がり0では40個が同じ8ラインに重なり、OAM順の先頭10個だけが描かれる |
| 時間 | 各演出の最大量とタイル転送を含む60フレームで、全144ラインのSCX/BGPの書き込みが、そのラインの描画の後から次のラインの描画（Dot 84）の前に入る。VBlank処理はライン0のHBlankより前に終わり、ラグ（`wLagFrames`）は0 |
| 再現性 | 参照の入力列の330フレーム分（演出の切替2回を含む）を命令ステップ・4,096 T・997 Tの予算で進め、全入力時刻で全状態（APUの各チャネル・混合・未取得のPCMを含む）・スナップショット・全64 KiB・画像が一致する。タイル転送中のSTAT処理の途中で保存し、演出の切替を含む続きを2回再実行して全状態が一致する |
| 独立参照 | binjgbで描いた19枚（3演出・8ボタン・最小/最大・転送途中・停止・10個制限・演出の往復・同時押し）と、電源投入からのT-cycleで同じ入力を与えて全画素が一致する |
| 固定 | ヘッダー・チェックサム・ROM Onlyと、観測ラベル（映像のC000〜と音楽のC050〜C05B）のアドレス。ベンチマークの3演出の素材が各演出・量7・実行中で始まり、4秒で238〜240枚・ラグ0で進むこと |
| 音程とテンポ | 音程表（MIDI 36〜96）と効果音の8音が、平均律の周波数から再計算した周期と一致する。速度を3→7→0と変える580フレームで、毎フレームのステップ・端数・各チャネルの最後の音符を、ROMの曲表を読む小さなモデル（端数へ24 + 6×速度、音符0は保持・1は消音）と比べて一致し、鳴っている音符のCH1〜CH3の周期・メロディーのデューティと減衰・ドラムのNR42/NR43・NR51 = $7Dも一致する |
| Start・効果音・音色 | Startで止めたフレームでNR51 = 0、30フレームの間ステップと端数を保ち、PCMの最後の1,000フレームが0。再開でNR51が戻り、保存した音符を初期音量から鳴らし直す。上・下の各段でCH2が量の音程・NR22 = $F1・デューティ50%になり、8フレームの間は和音に上書きされない。AとSelectでメロディーのデューティ・音色の組・波形RAM・ハイハットのNR43・Waveの出力レベルが変わる |
| ステレオ・Wave RAM | 電源投入から4秒のPCMで左右とも十分な音量があり、4分の1以上のフレームで左右が異なる（和音は左、ドラムは右だけ）。無入力の14秒の間、毎フレームWave RAMが最初の波形表のまま |

デモはベースの音符ごとにNR30へ0を書いてCH3を止めてから鳴らす。DMGでは、鳴っているCH3を鳴らし直す瞬間がWave RAMの読み出しと重なるとWave RAMの先頭が書き換わり（Pan Docs NR34の注意。このモデルの重なりの窓はbinjgb・SameBoyと同じで、テストROM集の`apu`の再トリガーのケースもSameBoyと一致する）、止めずに鳴らし直すと割り込み処理の数Tの違いで音色が変わる。Wave RAMのテストはこれを見張る。

**binjgbとの音の比較：** `./Roms/MegaDemo/Compare-Audio.ps1`（比較は`CompareAudio.cs`）が、binjgbの`binjgb.exe`の音をSDLのディスク出力で取り出し、電源投入から無入力の約13秒をこのエミュレーターのPCMと比べる（テストからは実行しない）。93 msごとのスペクトル（60 Hz〜5 kHz）のコサイン類似度の平均0.98以上・下位10% 0.95以上、各窓で最も強い周波数の一致95%以上、音量の包絡の相関0.9以上で成功とし、聴き比べ用のWAVを`.cache/megademo-audio`へ書く。記録した値は左0.991（下位10% 0.988）・右0.993（同0.985）、最も強い周波数の一致270/278窓、包絡0.965。binjgbはSO1（右）を先に書くため左右が逆に並び、出力の高域通過がない（0〜1の単極の値）ので、比較では左右を入れ替え、両方に同じ10 msの移動平均を引く処理をかける。アナログ部・機種差・リサンプリングは合わせていないため、波形のbit一致は求めない。聴感の評価はしていない。

参照の作成で分かったbinjgbとの違いはホスト側の方針だけである。binjgbはヘッダーのロゴを検査してROMを拒否するため、一時コピーにだけロゴとチェックサムを書く。また既定で反対方向の同時押しの片方を落とすため、参照の入力列は反対方向を同時に押さない（DMGの配線では両方の線が押下になり、打ち消し規則は入力のテストで確かめる）。LCDを有効にした直後の4フレームをbinjgbは出力しないため、選ぶフレームは演出の切り替えから8フレーム以上離す。

### RPGデモ（USAGI QUEST）

[RPGデモ](../Roms/RpgDemo/README.md)の21件：

| 観点 | 確認内容 |
| --- | --- |
| 独立参照 | binjgbで描いた22枚（夜空の背景・落ちる文字・`PUSH START`と歩く勇者・メニュー2枚・選択3枚・戦闘10枚・KEY TEST 4枚）と、電源投入からのT-cycleで同じ入力を与えて全画素が一致する |
| VBlankの時間 | 参照の全行程と、選択画面で2フレームごとに入力を変え続けた90フレームで、VBlank処理のVRAMの書き込みがVBlankの4,560 T以内に終わる（最長は約4,170 T）。連打の後、最後の選択の顔と敵のタイル（16・36タイル）がROMのタイルと一致する |
| 画面と操作 | タイトルの飛ばし（SCY 112）とメニュー（上下・Select・B・A）。選択画面の勇者の一周・難易度の両端・Bでの戻りと、表示の文字（`>NIGHTMARE`・`SHADOWCAT`・`PINA`・`HEART BEAM`・`HERO 2/8`）。KEY TESTの8ボタンの数・押しているボタンの名前とパッドのバイト・`COUNT: 00008`の表示・同時押し、ボタンごとの音のCH2の周期（平均律）とNR22、Start+Selectでのタイトルへの戻り |
| 戦闘 | 文章にA、コマンドに十字キーとA、交代にSelectを押す自動の操作で4回戦い、ROMが乱数を引いた瞬間の状態からREADMEの規則で計算した数値と比べる（審判）。EASY（GUARDとRUNを含む）・NORMAL（MAGICとMP切れ・GUARD・交代・逃走）・NIGHTMARE（RUNが効かず、倒れた勇者の次が出る）の勝ちと、NIGHTMAREでRUNだけを続けたGAME OVER。毎ターン、敵と勇者8人のHP・MP・攻撃の回数・GUARD・結果、最後に曲とEXP、戦闘後の敵と勇者のタイルがROMの絵と一致する。GUARDの「最低1」は、敵の最小の攻撃が3（半分で1）のため起きない |
| 一時停止 | StartでNR51 = 0、60フレームの間曲の行と端数を保ち`- PAUSE -`を表示し、Aは効かない。もう一度Startでコマンドと曲が戻る |
| 音楽 | 音程表（MIDI 36〜96）が平均律と一致する。5曲のテンポ（119・94・150・140・77 BPM）・繰り返し・パターン数、全音符が調の音階（イ短調はG♯を含む）に入り、各パターンで3音とも鳴る。タイトル・選択・戦闘の曲を1周以上、毎フレームの行・端数・パターン・CH1〜CH3の周期と音色・ベースの音量と波形RAM・ドラムのNR42/NR43・NR50/NR51を、READMEの規則で書いた曲のモデルと比べて一致する。勝利と全滅の曲は2パターンで止まり、3音とも消音のまま |

### 音声確認ROMとセーブカウンター

[音声確認ROM](../Roms/SoundCheck/README.md)は、`SoundCheckTests`（1件）が各区間の左右（片側は完全に0、両側は左右一致）をPCMから確かめ、実WASAPIでの手動の確認にも使う。[セーブカウンター](../Roms/SaveCounter/README.md)はRomカテゴリに含めない手動の確認用のROMで、WPFとAndroidの電池セーブの確認に使う。

### 自作テストROM

[テストROM集](../Roms/TestSuite/README.md)の10本（`selftest`・`cpu-instrs`・`instr-timing`・`mem-timing`・`halt-bug`・`oam-bug`・`irq-timing`・`oam-dma`・`ppu-timing`・`apu`）は、CPU命令の結果と時間、HALTの不具合、OAMの破損、割り込み、OAM DMA、PPUの時間、APUを測り、測った値を結果のバイト列として出す（合計5,535 bytes・2,017ケース）。期待値は同じROMをSameBoy（`c458e7c5`。DMG-Bとして、SameBoyのBoot ROMから起動）で実行した結果で、binjgbの結果も参考に記録する。`TestSuiteTests`（ROMごとに1件）は、ROMのSHA-256と、ケースが結果の全バイトを隙間なく覆うことを確かめてから、ケースごとに比べる。SameBoyがDMGの実機の結果（GambatteのhwtestsのDMG-08の結果）と違う点はROMで測らない。GonFoxが再現していないSameBoyの挙動の21ケース（`apu`のスイープの判定の遅れ・CH4の開始の遅れ・DMGのNR10の規則の9件、`oam-bug`のライン1のDot 0〜3・80〜83の12件）は`known`として比べず、テストの出力に記録する。ほかの1,996ケースはすべて一致し、SameBoyのBoot ROMを通しても起動後の状態から始めても同じ結果になる。

## 資料と実機の結果で決めた規則

挙動は固定版の資料から作り、資料が食い違う点と資料にない点は、DMGで確かめたテストROMの結果で決めた。ROMの名前による特別扱いはしない。期待値は資料かDMGで確かめたテストに基づく。根拠に挙げるGambatteの名前はhwtests（`d819bad196`）のDMG-08の結果、blarggの名前はretrio/gb-test-roms `c240dd7d`のテストで、どちらも許諾の都合でリポジトリーに含めず、テストでは実行しない。それらの領域は単体テストと自作のテストROM（[自作テストROM](#自作テストrom)）で確かめる。

| 資料 | 固定版 | 主な用途 |
| --- | --- | --- |
| [Pan Docs](https://gbdev.io/pandocs/) | gbdev/pandocs `0191af06ac49661587dcde3d57a241a626b8df75` | 全体の基準（Rendering・Pixel FIFO・OAM DMA・OAM Corruption Bug・Audio・Power Up Sequence・Reducing Power Consumption・各MBC） |
| SameBoy | LIJI32/SameBoy `c458e7c5d2d350fb37a1931c40da9f758d28d240` | DMGの経路（PPUの競合表、`apu.c`、`memory.c`、`sm83_cpu.c`）、カートリッジの細部 |
| Gambatte | `d819bad196`（`libgambatte/src/sound`、起動後の状態`initstate.cpp`、`testrunner.cpp`） | APUの位相と起動後の状態、hwtestsの結果の読み方 |
| binjgb | v0.1.11（`emulator.c`） | APUの段ごとの処理、MBC5 |
| mGBA | 0.10.5 | HuC3 |
| Blarggの「Gameboy sound hardware」（gbdev wiki）と`dmg_sound`の解説・ソース | 2026-10-04に取得した版 | 長さの余分なクロックと63、包絡線の+1、減算後の停止、電源中の長さ |
| SonoSooS氏のまとめ | rev `8efa6645`の"NOP and STOP" | STOP |
| nitro2k01氏の調査（[SameBoyのissue 278](https://github.com/LIJI32/SameBoy/issues/278)） | — | WindowのY条件、無効なWindowの画素の挿入 |

### 割り込み・HALT・LCD・STAT

| 規則 | 根拠 |
| --- | --- |
| LCD有効化後の最初のフレームを表示しない（完成には数える。電源投入・Resetは表示する） | Pan Docs、SameBoy、`firstwhite` |
| LCD有効化のラインはOBJを選ばず、画素の流れがほかのラインより2 dot遅く始まる | SameBoy、`enable_display_ly0_sprites_m0stat`、gbmicrotest `int_hblank_*`（注記なし） |
| ライン144はMode 1の前に、HBlank源とOAM源をつないだままLY=LYCを比べる。VBlankの始まりのOAM源はMode 1の始まりまで続く | `lycint143_m1irq`・`m1irq_m2enable_lyc`・`m2m1irq_ifw` |
| ライン153の153と0の比較の位置（[現行仕様](Specification.md)の表） | `lycint152_lyc153irq_late_retrigger`・`lycint152_lyc0irq_ifw`・`lyc153_late_ff45_enable_*` |
| LYCの書き込みは1 dot後から比較に効く | `m0enable/lycdisable_ff45_scx3_3` |
| STAT書き込みは最初の1 Tだけ全源、値は1 T後。行頭ではHBlank源が信号を保つ | Pan Docs・SameBoy、`ff41_disable`・`late_enable_m0disable`・`lycstatwirq_trigger_m0_late` |
| LCD無効中のSTAT書き込みは、凍結したLY=LYCの結果でLYC源をつなぐ | `lcdoff_lycirqen` |
| HBlank源は最後の画素の位置に着いた1 dot後に立ち、そこでのOBJ（X=167）の取得を待たない。STATのモードは取得の後に0になる | `10spritesPrLine_10xposA7_m0irq_2`・`m2int_wxA6_spxA7_m0irq_2`（同じ機体の`m3stat`の各件はモードが取得を待つことを示す） |
| CPUのIF書き込みはM-cycleの1 T目の後に効く | SameBoy、`m2int_m0irq_scx3_ifw`、gbmicrotest `hblank_scx3_if` |
| 受理は待機3 M-cycleの後にpushし、IFは下位バイトのpushの時点で標本化して2 T後に消す | SameBoy、`late_m0irq_vs_tima`・`lycint143_m1irq_late_retrigger` |
| HALT直後の最初の待機M-cycleは始まりで標本化する | SameBoyの`just_halted`、`late_m0irq_halt_m0stat_scx3` |
| 未定義命令はCPUだけが止まり、時間は進み、割り込みで再開しない | Pan Docs、SameBoy、`undef_ops` |

### 画素の流れと描画中の書き込み

Mode 3はDMGの画素の流れ（背景/Windowの取得器と、BG・OBJの2つのFIFO）で描く。Pan Docs（Rendering・Pixel FIFO・Window・OAMの各ページ）とSameBoyのDMGの経路から作った。

**書き込みの位置（mealybug）：** CPUの書き込みは4の倍数のdotにあり、ふつうは次のdotから効く。SameBoyの競合表（DMG）では、SCYとLCDCのタイル・マップ・OBJサイズのbitは書き込みのdotから、SCXはその前のdotから効くが、mealybugの画像（`m3_scy_change`・`m3_lcdc_tile_sel_change`・`m3_lcdc_bg_map_change`・`m3_lcdc_win_map_change`・`m3_lcdc_tile_sel_win_change`・`m3_lcdc_obj_size_change`の2件）はこれらもSCXと同じ前のdotから効くことを示し、その位置にした。Window表示を消す書き込み（`m3_lcdc_win_en_change_multiple_wx`）と、最初の画素でBG表示を消す書き込み（`m3_lcdc_bg_en_change`。SameBoyはOBJ表示にだけこの規則を持つ）も前のdotから効く。パレットのORの1 dot（`m3_bgp_change`・`m3_obp0_change`）、SCX（`m3_scx_high_5_bits`・`m3_scx_low_3_bits`）、WX（`m3_wx_4/5/6_change`）、OBJ表示とその取得の中断（`m3_lcdc_obj_en_change`の2件）はSameBoyの位置のまま一致する。

| 規則 | 根拠 |
| --- | --- |
| 描画中のSCXの書き込みは、取得器のタイル列には1 dot前から、細かいスクロールの比較には書き込みのdotから効く（比較を逃すと一周して8 dot延びる） | `scx_during_m3`の2件・`scx_m3_extend_1`、`enable_display/ly0_late_scx7_m3stat_scx3_2` |
| OAM走査は項目iをDot 2+2iで、そのときのOBJサイズで判定する。Mode 2中のLCDCとOAMの書き込みは走査済みの項目に効かない | SameBoy、`late_sizechange*`の8件 |
| WindowのY条件はMode 2の始まりで比べ、描画中のWYの書き込みは3 dot後に比べる | `window/arg/late_wy_*`の5件・`window/late_wy_1` |
| 描画中にWindow表示を消すと、開始の判定だけは1 dot前まで古い値を見る。取得器はそのdotで背景を読むので、捕まった開始はWindowの画素も色0の挿入も出さず、取得をやり直す6 dotだけMode 3を延ばす | `late_disable_early_scx03_wx11_2`・`late_disable_late_scx03_wx11_2`・`late_disable_scx5_1`、mealybug `m3_lcdc_win_en_change_multiple_wx` |
| WX=166：ラインの終わりにY条件があれば、Window表示によらず次の画素の流れの開始までWindowの開始を保ち、その間に有効にすると次のラインで開始し、無効にすると止める。行カウンターは開始ごとに進み、保たれたWindowは次のラインの始めにもう1つ進む。Mode 3に入ってからの開始は、そのラインを描いた後で数える | `window/on_screen/wxA6_*`の6件 |
| 描画中のパレットの書き込みは、1 dot前が行の最初の画素ならORを経ずに新しい値になる（LCDC bit 0の規則と同じ） | AGE `m3-bg-bgp`（SCX=1でDot 95に描くx=0） |

資料の食い違いの決め方：

- WindowのY条件：Pan Docsは「ラインの始めにWY=LYなら成立」とだけ書き、SameBoyはWindow表示が有効なときだけ成立させる。nitro2k01氏が実機（SGB、DMG A/B/C）の信号をロジックアナライザーで記録した調査では、無効なWindowの画素の挿入が「一度WY/WXで起動した後」に起き、SameBoyに合う。mealybugにも、書き込み時の判定がないと一致しないケースがある。Window表示を一度も有効にしないROMでは、起動時のWX=0・WY=0のままでも画素は入らない。
- 無効なWindowの画素の挿入：Pan Docsの記述、nitro2k01氏の条件（WX mod 8 = 7 − SCX mod 8、WXは0〜166）、SameBoyの実装が一致する。
- WX=0：Pan Docsは「SCX mod 8だけ左へずれる」とし、SameBoyではSCX mod 8が1〜6のとき1画素多く落ちる（7はWXの位置にもう一度来た挿入でPan Docsと同じ列になる）。時間は`m3_window_timing_wx_0`がSameBoyと一致し、列は実機で確かめていないためSameBoyに従った。
- WX=166：Pan Docs（そのラインに出ず、次のラインから全幅）とSameBoyが一致する。
- LCDC bit 0：Pan DocsのLCDCの節は「白」、Pixel FIFOの節とSameBoyはBGPの色0とする。区別できるテストがないため白にした。
- X=0のOBJ：SameBoyは取得待ちの間は画素を止めるが、DMGではそのOBJが同じdotで先に取得されるため到達せず、入れていない。

### OAM DMAとOAMの破損

Pan Docs（OAM DMA・Memory Map・OAM Corruption Bug）とSameBoyのDMGの経路から作り、食い違う点は実機の結果で決めた。OAMの破損の単体テストの期待の行は、SameBoyの`GB_trigger_oam_bug`・`_read`を逐語的に移した別の実装で求めた。

| 規則 | 根拠 |
| --- | --- |
| 転送中にCPUがDMAの転送元と同じバスを読むと、そのM-cycleにDMAが運ぶbyteが見える（命令の取得も同じ） | `busyread*`・`busypop*`・`busydelay_1`（HALTのない取得が、転送中のROMのbyteを命令として実行する） |
| 同じバスへの書き込みは書いた先へ届かず、そのbyteの代わりにOAMへ入る。転送元がWRAMとその写し（C0–FFのページ）なら読んだbyteとのAND、カートリッジ（ROM・RAM）とVRAMなら書いた値 | `busypush*`・`busywrite8000`・`srcA000_busywrite4000`（MBCのレジスタへの書き込みも届かない）。SameBoyはDMGで転送元A000以降をAND、ROMとVRAMは書いた先へ届けるとし、`srcA000_busypush*`・`src0000_busypush*`と合わない |
| 再開の開始待ちの間も古い転送を続けるが、読むページと使うバスは書き込みの時点で新しいものに替わる | `oamdma_src8000_srcchange0000_busyinc`・`_busyread0000_1`/`_2` |
| CPUがHALTで待つ間、2つ目の待機M-cycleから復帰するM-cycleまで転送も止まる | `oamdmasrc80_halt_lycirq_read8000`・`_m2irq_read8000`・`oamdma_late_halt_stat_1`/`_2`。SameBoyの、止めた後に復帰で1 M-cycle分を足す形は`_2`と合わない |
| 転送中に走査したOAMの項目は画面外とする | Pan Docs、`late_sp00x`〜`late_sp39y`の16件（始まりと終わりの位置を1 dotずらすと失敗する） |
| FEA0–FEFFは、OAMが止められていない間は00を読む | Pan Docs、全13の転送元の`busypushFEA1`・`busypushFF01` |
| OAMの破損：Mode 2のDot 4〜79のFE00–FEFFへの読み書きと、IDUがFE00–FEFFの値を出す内部待機（INC/DEC rr・PUSH・CALL・RST・割り込みの受理・LD SP,HL）で、そのM-cycleの行（Dot 4k〜4k+3は行k）を壊す。式はPan DocsとSameBoyのDMG。行の位置はSameBoyの行の並びを`4-scanline_timing`の最初の位置に合わせた | blargg `oam_bug`の`2-causes`・`3-non_causes`・`4-scanline_timing`・`5-timing_bug`・`7-timing_effect`（116の位置の結果のCRC）・`8-instr_effect`（命令ごとの結果のCRC） |

### APU

挙動はPan Docs（Audio・Audio Registers・Audio Details）を基準に、Blarggの記述、SameBoyの`apu.c`、binjgbの段ごとの処理、Gambatteの`libgambatte/src/sound`と起動後の状態から決めた。DMGで判定できる公開ROMは少ない（Mooneyeに音のテストはなく、gbmicrotestの`audio_testbench`・`wave_write_to_0xC003`は成否を出さず、SameSuiteのAPUのテストの多くはCGBのPCM12/PCM34で結果を読む）ため、単体テストはテスト内でバイト列から作る小さなプログラムと参照実装で確かめる。PCMは、仕様をT-cycleごとに書き直した参照実装と、包絡線・長さ・スイープ・DAC・電源の切り替えを含む16万Tの全フレームで比べる（書き込みを奇数Tに置き、チャネルの変化がDIV-APUの立下りと重ならないようにする）。毎TにAPUを同期した場合と同期点だけの場合も全フレームが一致する。LFSRは別の表し方（全1から始めてXORを入れ、bit 0が0のとき音量）を参照にして、15 bitで40,000クロック・7 bitで2,000クロックの出力と、周期32,767/127を比べる。

| Wave・Noise・DACの挙動 | 根拠 |
| --- | --- |
| Waveの読み込み順（各byteの上位が先）、トリガー後の最初の読み込みは位置1、それまでは最後に読んだサンプル（電源OFFで0）を出す、周期の変更は次の読み込みから、出力レベルのシフト | Pan Docs |
| トリガーから最初の読み込みまで1周期+6 T | SameBoy（2 MHz単位で周期+3） |
| 再生中のWave RAMはCH3が読むTだけ届き、そのとき読んだbyteが対象（DMG） | Pan Docs |
| 読み込み直前の再トリガーによるWave RAMの書き換え（DMG） | 書き換え方はPan Docs（Blarggの記述）、時期はSameBoy（読み込みの2 MHz 1つ前） |
| LFSRは0から開始、bit 0とbit 1のXNOR、bit 0が1で音量、シフト量14/15で停止 | Pan Docs |
| DACの向き（デジタル0で正）、全DAC OFFで出力0とコンデンサーの切り離し、高域通過の係数0.999958/T | Pan Docs |

Gambatteのhwtestsの`sound`（音の有無89件、16進の表示50件）のDMG-08の結果で決めた規則：

| 規則 | 根拠 |
| --- | --- |
| BootBypassのフレームシーケンサーは段0の後（次は段1）。CH1は起動音2音目（NR11=80、NR12=F3、NR13=C1、NR14=87）で動作中のまま終わる状態を包絡線が0まで下がった後として扱い、duty位置3で、次の位置までPC=0100から276 T（周期値7C1で252 Tごと） | `ch2_init_env_counter_timing_2`〜`4`、`ch1_init_pos_1`〜`8`（位置4→5の境は524〜528 T、0→1は1,532〜1,536 Tの間）、Gambatteの起動後の状態 |
| Pulseの出力bitは位置を進めたときに決まり、NRx1でdutyを変えても次の位置まで変わらない | `ch1_duty0_to_duty3_pos3`・`_2`・`_0`、Gambatte（`DutyUnit`）、SameBoy（NR11の書き込みで出力を作り直さない） |
| トリガーから最初の位置の進行まで、停止中のチャネルは周期+8 T、動作中のチャネルは周期+4 T | `ch1_duty0_pos6_to_pos7_timing_1`・`_2`、`ch1_duty0_to_duty3_pos3_0`。動作中の+4 TはDMG-08の結果では決まらず（0〜12 Tのどれでも成功する）、GambatteとSameBoyに合わせた |
| 電源ONで最初の立下りを無視するかは、書き込んだM-cycleの終わり（4 T後）のDIV bit 12で決める | `ch2_init_reset_env_counter_timing_5`・`ch2_late_reset_nr52_2b`、Gambatte |
| トリガーが包絡線を1周期遅らせる（次の段が7）かは、書き込んだM-cycleの4 Tの間に立下りがあれば、その段の後で決める。長さの余分なクロックは書き込みの時点で決まる | `ch2_init_reset_env_counter_timing_11`・`ch2_init_reset_length_counter_timing_2`、Gambatte（`EnvelopeUnit::nr4Init`） |
| スイープのクロックは段2/6の立下りの4 T後に来る。トリガーは、書き込んだM-cycleの間の段2/6と、まだ来ていないそのクロックを、読み込み直したタイマーに数えない | `ch1_init_reset_sweep_counter_timing_nr52_1`・`_2`・`ch1_init_reset_sweep_counter_timing_4`、Gambatte |

### 起動直後の状態とBoot ROM

BootBypassでは、PC=0100がライン153のDot 400（Mode 1。LYは0を読み、LY=LYCの一致が立つ。ライン0の始まりの56 dot前）にあたり、P1は両方の行を選んだ$CFである。

| 根拠 | 内容 |
| --- | --- |
| Gambatteの起動後の状態（`initstate.cpp`） | video cycleは153×456+396（LYの事象まで60 T。GambatteのLYの読み出しは4 T早く変わるため56）。DIVは0x1ABCC（このモデルのABCCと同じ） |
| Gambatte `display_startstate` | PC=0100から52 Tと56 TのSTATの読み出しが$85と$84（Mode 1の後、ライン0の始まりの1回だけのMode 0。LY=LYCの一致が立つ） |
| gbmicrotest `poweron_*`（DMG-CPU-08。注記がないため参考） | STATは5→85、6→84、7→86。Mode 2→3は26/27、3→0は69/70、LY 0→1は119/120（読み出しは4×(DELAY+8) T）。DIVはAB→ACが4/5 |
| Pan Docs（Power Up Sequence） | PC=0100でSTAT $85、LY $00、P1 $CF |

Mooneyeの`boot_div-dmgABCmgb`・`boot_hwio-dmgABCmgb`（P1の$CFを要する）・`boot_regs-dmgABC`も成功する。実Boot ROMの対応付け・FF50・電源投入の状態はSameBoy（`memory.c`）とPan Docsに従い、[SameBoyのBoot ROM](#sameboyのboot-rom)で確かめる。

### STOP

STOPの4通りの分岐（ボタンと割り込みの要求によるNOP・HALT・1バイトと2バイトのSTOP）と、選んだ行の線の立下りでの復帰（[CPU命令表](CpuInstructions.md)）は、Pan Docs（Reducing Power Consumption）の分岐図、SameBoyの`sm83_cpu.c`（`stop`）、SonoSooS氏のまとめから作った。DMGで自動判定できる実機のテストROMはない（gbc-hw-testsは目視用）ため、単体テスト（`StopTests`）だけで確かめる。

### カートリッジ

- MBC1・MBC1M：Mooneyeの`mbc1/`の13件。MBC1Mは、1 MiBのMBC1でbank 10h（0x40104）にもNintendoロゴがあるものとして判定し、読み込みの警告に出す。
- MBC2：Pan DocsとSameBoyに食い違いはなく、MBC2Aの実機で確かめたMooneyeのテストで確かめる。RAM有効は値の下位4 bitが$A、内蔵RAMの上位4 bitは読むと1（SameBoy、`mbc2/ram`の6回目）。
- MBC3と時計：Pan DocsとSameBoyから決め、時計の規則はrtc3testに合わせた。ラッチはPan Docsの00→01に従う（SameBoyは6000–7FFFへのどの書き込みでも写す。rtc3testはどちらでも合格する）。範囲外のbitはrtc3testの記述（有効なbitだけが書けて読める）に合わせて書き込みの時点で落とす（SameBoyは8 bitを持ち、読み出しで有効なbitだけを見せる）。RAMバンク4〜7は型によらずRAMの容量で折り返す（SameBoyは時計付きの型でFF、時計のない型で0〜3と同じ）。時計のない型（11〜13）で08〜0Cを選ぶとFFを読む（SameBoyはRAMを見せる）。MBC30はROM 4 MiBかRAM 64 KiBの宣言で判別する（SameBoy）。
- MBC5：Pan Docs、SameBoy、binjgbで決めた。食い違いはRAM有効の書き込みだけで、Pan Docsとbinjgbは下位4 bitが$Aで有効、SameBoyは$0Aだけを有効とする。DMGのMBC5で確かめたテストROMがないため、Pan Docsの記述（実際のMBCは下位4 bitを見る）に従った。電源投入とResetではROMバンク1を選ぶ（SameBoy・binjgb）。振動付き（1C〜1E）はRAMバンクのレジスタのbit 3をモーター、bit 0〜2をRAMバンクとし、RAMは最大64 KiB。判定できるテストROMがないため単体テストで確かめる。
- ヘッダーのRAMの食い違い：「+RAM」の種別（02・03・10・13・1A・1B・1D・1E）でRAMサイズのコードが0のものはRAMなしとして読み込んで警告し、RAMのない種別（01・0F・11・19・1C）でRAMサイズがあるものは拒否する。blarggの`halt_bug`はMBC1+RAMでサイズ0のまま実機で動く。
- MMM01・HuC1・ポケットカメラ・HuC3：実機で確かめたテストROMがないため、Pan Docsの規則の単体テストで確かめる。期待値はPan Docsの式から手で計算したもので、実装の出力を写したものはない。Pan Docsが決めていない点は次のように選んだ。

| 種別 | 選んだ点 | 単体テスト |
| --- | --- | --- |
| MMM01（0B〜0D） | RAMバンクの上位は4000のbit 2〜3（Pan Docsの図とSameBoy。見出しの「1〜2」は誤り）。対応付けの前もRAMが使え（SameBoy・MAME）、4000–7FFFは最後のバンク、対応付けの書き込みでRAMのマスクも設定する（SameBoy）。SameBoyと食い違う3点（RAMのマスクの極性、モード0のRAMの固定、多重化の0000–3FFF）はPan Docsに従った | `Mmm01Tests`（Pan Docsの例「下位$10、マスク$30→バンク$11」を含む） |
| HuC1（FF） | 0Ehで赤外線（値の全体で判定。SameBoyは下位4 bit）、電源投入とResetでROMバンク1（SameBoy）。1 MiBを超えるROM・32 KiBを超えるRAMは、レジスタの幅が資料にないため拒否 | `HuC1Tests` |
| ポケットカメラ（FC） | ROMバンク6 bit（Gambatte）、RAMの書き込み有効は下位4 bit（SameBoy・Gambatte）、撮影の時間はPan Docsの式で始めたアクセスから数え、HALT中も進む（Gambatte）。画像はPan Docsのエミュレーター向けのサンプルの手順を整数で同じ切り捨てになるよう計算する（利得とA005は使わない） | `CameraTests`、GonFox.GameBoy.Platform.Testsの画像の受け渡し |
| HuC3（FE） | MCUは忙しくならない（SameBoy）、写す幅は6 nibble（mGBA）、1440以上の分は日を進めない、Resetは電池で動くMCUを保つ。電池ファイルはmGBAの136 bytes（SameBoyの17 bytesも読む） | `Huc3Tests`、GonFox.GameBoy.Platform.Testsの`Huc3BatteryTests` |

### シリアル

内部クロックはSameBoyのモデル（DIVのカウンターのbit 7の立下りで反転し、SCへの書き込みで低にそろえる）で、`boot_sclk_align-dmgABCmgb`（DMG ABC・MGBで確かめた期待値）が成功する。単体テスト（`SerialTests`）は、SCへの書き込み時のカウンターの位相8通りで完了が256 − 位相 mod 256 + 3,840 T後になること、DIVのリセットが立下りになること、転送中のSCの書き込みを、SameBoyのモデルから計算した期待値で確かめる。

## サンプルの確認

### WPF

Example.GameBoy.WpfHostをUI Automationで操作して確かめる（確認のスクリプトはリポジトリーに含めない）。画面はエミュレーターのウィンドウだけを描画させて取得し、音はプロセス単位のループバックでこのアプリの音だけを取得する（音量5%以下）。ほかのウィンドウ・アプリの内容は取得しない。電池セーブ・状態・設定は環境変数`GONFOX_GAMEBOY_SAVES`・`GONFOX_GAMEBOY_STATES`・`GONFOX_GAMEBOY_SETTINGS`で確認用の場所を指し、利用者のファイル（`%LOCALAPPDATA%\GonFox.GameBoy`）を使わない。

| 確認 | 手順と見ること |
| --- | --- |
| 標準の手順 | 背景デモで、実行約60 FPS、「Pause」（監視の行が変わらずボタンが切り替わる）、「Step」（+4 T）、「Save state」と「Load state」（保存時と同じ画像・レジスター・PPU・周辺回路・T）、最小化中に止めた後に最新の画像が出ること、最大化の往復、閉じた「Debug」の区画の行が自動化の木にないこと、通常の終了（終了コード0） |
| 描画方式 | dmg-acid2を起動引数で開き、SkiaSharpとWriteableBitmapを一時停止中・実行中に切り替え、最大化・最小化を往復する。ウィンドウの画像の中の整数倍の画像を期待画像と物理ピクセル単位で比べ（通常は3倍の480×432）、設定に選択が残ること |
| 画素の流れと最初のフレーム | 曲付きメガデモ・dmg-acid2・mealybugの`m3_bgp_change`・`m3_scx_high_5_bits`・`m3_wx_4_change`を開き、約4秒後の画像を期待画像と比べる。`firstwhite`は0.25秒ごとに3秒間取得した画像がすべて白。メガデモは画面の「Select」で演出を切り替える（ROMがLCDを入れ直す） |
| CPUの停止 | 未定義命令D3を実行するROM（Gambatteの`undef_op_d3`。リポジトリーに含めない）で、状態の行の「CPU locked」とメッセージ欄の止まった位置・命令、時間が進むこと、一時停止中の「Step」（+4 T、PCは変わらない）とReset |
| Boot ROM | 設定にSameBoyのBoot ROMのパスを書いてdmg-acid2を開き、Boot ROMの実行中の状態の行、引き渡しの後の画像（全画素一致）、Reset（PC 0000）、「No boot ROM」→Reset（PC 0100）。512 bytesのファイルを選ぶと何も変えないこと、ない場所を指す設定では理由を出して起動後の状態で始め、終了時にパスを書かないこと |
| ファイルダイアログ | 「Open ROM」で選ぶ・キャンセル・キャンセル直後に開き直す・後始末中にアプリを閉じる。メインウィンドウと同じ位置に開き、開いている間はメインウィンドウが無効で、選択・キャンセルの直後に有効へ戻ること |
| 設定 | 音量・ミュート・最大化・通常の位置と大きさが次の起動に残ること。画面外の位置のファイルでは既定の位置と大きさ、壊れたファイルでは既定値で起動して書き直すこと |
| 時計と電池セーブ | rtc3testのBasic testsを実時間で実行し、終了から起動までの時間だけ時計が進むこと。セーブカウンターで、実行中のままの定期保存、2つ目のアプリが「In use」で読み込みを断ること、Reset後の前の世代とハッシュの記録、終了でのロックの解放 |
| 状態の枠と巻き戻し | 枠へ保存→再開→巻き戻し（約1秒ずつ戻って一時停止）、空きの枠では復元できないこと、アプリを再起動して枠から復元すると保存時と同じ状態になり、巻き戻しの記録が消えること |
| 音声 | 音声確認ROM（実WASAPI）で、100 msごとの区間の左右、音量の曲線（10%と5%の差が約12 dB）、ミュート・一時停止で音が0、再開から音までの時間、状態の操作・Resetの後も音が続くこと、プロセスを0.3秒止めた後の回復、10分の連続再生で欠落がないこと。メガデモで曲とROMのStartによる消音・再開 |
| 長時間の操作 | 曲付きメガデモで30分、画面のボタン・一時停止/再開・状態の保存/復元を繰り返し、FPS・音の不足と破棄、プロセスのメモリー・スレッド・ハンドルが増え続けないこと |

UI Automationでコンボボックスの一覧を開いて選ぶと、ウィンドウが背景にある間は、その後のUI Automationの最大化と閉じる（Windowパターン）が効かなくなる。`WM_CLOSE`では閉じるため、手順ではウィンドウの状態の変更を一覧の操作より前に行い、`WM_CLOSE`で閉じる。ファイルダイアログのファイル名の欄と「開く」はダイアログ自身のWin32のコントロールで、PowerShell 7のUI Automationでは操作の型が出ないため、そのウィンドウへ`WM_SETTEXT`と`BM_CLICK`を送る（キー・マウスの入力は使わない）。

### Android（Pixel 9a）

Androidのサンプル（Example.GameBoy.MauiHostとGonFox.GameBoy.Platform.Android）は単体テストを持たず、Pixel 9a（Tensor G4、Android 17（API 37）、1080×2424・420 dpi・表示60 Hz）をUSBでつなぎ、adbの確認スクリプトで次を確かめる。ROMは起動の値（intentのextra。[現行仕様](Specification.md)）で開き、画面は`screencap`、状態はlogcatと`uiautomator dump`で読む。Release版の最終の構成で通す。確認のスクリプトと確認用のROMはリポジトリーに含めない。パッケージを`com.gonfox.gameboy`にした後のアプリでは、この一覧をまだ実機で通していない。

| 確認 | 方法と確かめること |
| --- | --- |
| ビルド | `dotnet build Example.GameBoy.MauiHost -c Release`（arm64・x64）が警告0。トリミングの解析の警告を出すビルド（`-p:SuppressTrimAnalysisWarnings=false -p:TrimmerSingleWarn=false`）で、このリポジトリーのアセンブリから警告が出ない（MAUI本体とSkiaSharp.Viewsの警告は対象外） |
| 画面 | dmg-acid2を開き、スクリーンショットの中の画像を参照画像（`reference-dmg.png`）と物理ピクセル単位で比べ、6倍（960×864）の全829,440画素が一致すること（コールドスタートの後、起動中のアプリへ送った後、SameBoyのBoot ROMを通した後） |
| ボタン | A・B・十字キーの4方向と斜め・SELECT・STARTの9か所のタップと押し続けで、押下と離しがログに出て（斜めは2方向）、触覚を返すこと。背景デモでBを押している間は画像の全画素が白（背景の非表示）になること |
| 設定 | 設定の各要素を`uiautomator dump`で確かめ、「Button haptics」の切り替え（切った間は触覚なし）、「Mute」の切り替え、音量のスライダーが効くこと |
| Boot ROM | `--es bootrom`でSameBoyのBoot ROMを設定してdmg-acid2を開き、T=0から対応付けられてFF50で外れ、その後の画像が全画素一致すること。強制終了して起動し直すとアプリ内のコピーから再びT=0で始まり、設定の「Remove boot ROM」で「Boot ROM: none」になること |
| 電池セーブ | セーブカウンターで、定期保存を待って強制終了→起動（最後のROMを開き直す）→リセット、また開いて1秒でBACK（定期保存の前）→起動として、画面の濃淡が1ずつ進み、閉じる前の値がファイルに届くこと |
| 状態の枠と巻き戻し | 背景デモを枠1へ保存→再開→枠1から復元→再開→巻き戻し、強制終了して起動し直して枠1から復元。復元した画像が保存した画像と全画素一致し、巻き戻しが約1秒前へ戻って一時停止すること |
| 背景と復帰 | ホームのキーの3秒後と0.3・0.7・1.2秒後に起動：背景で一時停止して保存し、戻ると実行中だった場合だけ再開して約60 FPSに戻ること。ホーム→`am kill`→起動：保存した状態からアクティビティが作られ、最後に開いたものと中断の状態を戻して続きから進むこと。BACK→起動：保存を待ってから実行スレッドを解放し、新しい画面が最後に開いたものを開くこと |
| ファイルの選択 | 「ROM」でファイルの選択（DocumentsUI）が前面に出る間は一時停止して保存し、BACKで閉じると再開すること |
| 振動カートリッジ | モーターを約0.9秒ごとに入切する確認用の最小のROM（MBC5+RUMBLE）で、モーターの入切がログに出て、端末の振動の記録（`dumpsys vibrator_manager`）にこのアプリの150 msの振動と取り消しが、ゲームの振動（用途MEDIA）として残ること |
| 音 | 曲付きメガデモをミュートで30秒鳴らし、AudioTrack（バッファ60 ms・低遅延）が再生中で破棄0であること。不足の回数とキューの目標の変化をログで見る |
| RPGデモ | 画面のボタンでタイトル→メニュー→選択→戦闘とKEY TESTの8ボタンを押し、タイトルの`PUSH START`の枠の下3行を参照の336と、選択画面とKEY TEST（8回押した後）を530・2000と、6倍の全画素で比べる |

タップは自分のアプリが前面にあるときだけ送る。端末を別のadbのクライアントと共有する場合は、その操作が止んでから始め、アプリが前面を失えば最初からやり直す。`input tap`は押してから離すまで約2 msで、1フレームより短い押下は、実行スレッドが押下と離しを同じ実行の合間に反映するとROMに届かないため、人のタップに近い120 msの押下（同じ点への`input swipe`）を使う。音は端末のスピーカーから鳴るため確認の間は起動の値で消音にし、最後に戻す（起動の値の消音はアプリの設定に残る）。インストール直後の最初の起動はPixelのシステムの更新の画面を経由して起動の値が落ちるため、一度起動してから値を送る。この端末ではadbから生のタッチを書けず（`sendevent`はSELinuxで拒否）、`input`は1本の指だけなので、複数の指による入力元の合成は単体テストで確かめる。

## 既知の失敗

DMGの実機の結果と合わないことが分かっているもの。どのROMもリポジトリーに含めず、テストでは実行しない。

| テスト | 件数 | 理由 |
| --- | ---: | --- |
| Gambatte `dmgpalette_during_m3` | 11 | 起動ROMの後にLCDを入れ直さずに描画中のBGPを書くと、DMG-CPU-08では書き込みの1 dot前の画素が新しい値になる（例：SCX=0、Dot 96の書き込みでDot 95のx=1）。同じくDMG-CPU-08で確かめたAGE `m3-bg-bgp`はLCDを入れ直した後で、同じdot・同じ画素が古い値のまま（ORと同じ値）になり、mealybug（DMG-CPU B）もそこでORを示す。違いはLCDを有効にした経路にあり、このモデルのdotでは表せないため、mealybugとAGEに合わせた。起動直後の位相を直しても実Boot ROMを通しても変わらない（任天堂・SameBoy・BootixのどのBoot ROMも、AGEの入れ直しと同じ`ldh [rLCDC],a`の経路でLCDを入れる）。M-cycleより細かいCPUとPPUの位相か、ほかの単位の違いと考えられ、確かめる資料がない |
| Gambatte `oamdma`の`srcFE00_readFE00`・`srcFE00_busyread0000`・`busyreadA000`・`busyreadC000` | 4 | FE00からの転送でDMGが読むWRAMのDE00〜DE9F（テストは書かない）の値を見る。期待値は作者のDMG-08の電源投入時の値（FF）で、このモデルはWRAMを00で始める。FE00の転送元がWRAMの写しであることは、DE98〜DE9Fを書いてから読む`srcFE00_busypush*`（ANDの結果）のDMG-08の結果と、テストROM集の`oam-dma`（転送元FE00・FF00の内容）が示す。FF00からの転送で読むDFのページは作者の機体でも00で、`srcFF00_*`の結果とも合う |
| AGE `oam-write-dmgC` | 1 | 40行（SCX 0〜7×遅延0〜4、各12回の書き込み）のうち一致しない8行は、どれも遅延2の行で、テストの期待値が「DMG-CPU Cでは、LCDを止めた時期で変わり、テストを2フレームより広げると行全体が変わる」と注記した行（`(*)`）である。ほかの32行は一致し、境目の位置は`oam-read-dmgC-cgbBC`とも合う |

gbmicrotestの除外（[gbmicrotest](#gbmicrotest)）は、実機の記録やROMの不整合による選定外で、既知の失敗には数えない。

## 既知の近似と未対応

| 対象 | 近似・未対応 |
| --- | --- |
| PPU | Mode 3が終わったM-cycleに届く書き込み（Mode 0の後に前のdotへ効くSCX・LCDC・パレット）は、そのラインの最後の1〜2画素に反映しない。描画中のOBJのタイルと属性はOAM走査の時点の値。LCDC bit 0が無効なときの背景は白（SameBoyはBGPの色0）、WX=0の左端の列はSameBoyに従い、どちらも実機では確かめていない。起動直後のまま描画中にBGPを書く場合は1 dot前の画素をORのままにする（[既知の失敗](#既知の失敗)） |
| OAM・DMA | 描画中（Mode 3）のDMAでOBJの取得がDMAの書くwordを読む挙動（Pan Docs）、VRAMからのDMAとPPUのVRAMの読み出しの競合、DMA中のCPUのOAMアクセスとIDUによる破損（このモデルは起こさない。SameBoyは起こす）、Mode 2の直前と終わり（Dot 0〜3・80〜83）の読み書きによる破損（SameBoyではCPUのアドレスに依る。テストROM集の`oam-bug`の`known`）は、DMGで確かめたテストROMがないため再現しない。OAMの破損の読み出しの式のうち行4・8・12・16はSameBoyが機体で違うと記すもので、テストROM集の`oam-bug`がSameBoyとの一致を確かめるだけである |
| CPU・割り込み | テストROMにない組み合わせのI/O書き込みの時刻の競合は確かめていない |
| STOP | 単体テストだけで確かめた。復帰の後のSameBoyの8 T、IME=0のときのDIVの保持（SameBoyでも未確定の印）、DMGのP1の選択解除の遅れ（SameBoy `memory.c`）、ボタンのチャタリング、IME=1での復帰中のJoypad割り込みの乱れ、STOP中のLCDの表示は再現しない |
| APU | "zombie"書き込み（動作中のNRx2書き込み）は、旧周期0で包絡線が動作中なら音量+1の場合だけを再現する（DMGでは値の組み合わせで結果が非決定的。Gambatteの`sound`のDMG-08の結果はこの規則に合う）。再トリガーによるWave RAMの書き換えはDMGの多数派の規則だけで、機体差は再現しない。DAC OFFは即座に0（実機は機種差のある緩やかな変化）。シフト量に応じたスイープ計算の遅れとその途中のNR10書き込み、1 MHz/2 MHzクロックの位相（トリガー時に周期タイマーの下位2 bitを保つ動き、Wave・Noiseの開始・再読み込みを含む）、再読み込みの瞬間のNRx3/NRx4書き込み、読み込みと同じTのNR33/NR34書き込みは再現しない。DMGのNR10減算の解除は、トリガー後に減算で計算した場合だけ止める（スイープの判定の遅れ、CH4の開始の位相、NR10の規則はテストROM集の`apu`の`known`の9件）。BootBypassのAPUの位相はGambatteの起動後の状態の値で、実Boot ROMを実行した結果ではない（Boot ROMごとに起動音の後のCH1の位相が違う）。アナログ回路の特性（DACの非直線性・雑音）とCGBの追加機能（PCM12/PCM34等）は扱わない |
| PCM | 実機のアナログ出力ではなく、T-cycleごとの混合レベルを48 kHzの区間ごとに平均した値（箱型の平均で帯域制限なし。24 kHzを超える成分は折り返す）。高域通過は1フレーム単位の係数で、Pan Docsの毎Tの式とはフレーム内の変化の扱いがわずかに違う。全DACがOFFかはフレームの最後のT-cycleで判断する。1段=64の単位・上限±30,720・四捨五入・切り詰めはこのエミュレーターの定義で、実機の出力電圧には対応させていない。BootBypassの高域通過は落ち着いた状態から始まり、電源投入時の過渡は再現しない |
| 起動・機種 | DMGだけ。既定はBoot ROMの実行後の状態から始めるBootBypassで、実機の電源投入状態の完全な再現ではない。利用者が渡すBoot ROMはSameBoyのもので確かめ、任天堂のBoot ROMでは確かめていない。電源投入時のRAMの不定の値（WRAMは00で始める）、DIVの初期値、Boot ROMごとに違うCH1の位相は再現しない。mealybugの期待画像に写るBoot ROMのロゴは、テストが状態へ書いて補う |
| カートリッジ | MBC1Mは1 MiBでbank 10hにロゴがあるものだけを判定する。MMM01・HuC1・ポケットカメラ・HuC3は単体テストだけで確かめた。カメラは画像をホストから受け（WPFのサンプルは渡さず一様な灰色）、撮影の途中の画像・読み出しの期間の書き込みは再現しない。HuC1・HuC3の赤外線は相手の機器につながず、HuC3の音・目覚まし・忙しい時間と、Pan Docsが引用する掲示板の追加の観察（命令0が番地を進めない等）は再現しない。MBC5のRAM有効（下位4 bit）はDMGで確かめていない。MBC3の時計はエミュレーションの時間で進み、一時停止・STOP中は止まる。閉じていた間は次の読み込みで保存時刻からの秒数を足す（1秒未満は保存しない）。ラッチの電源投入時の状態、RAMバンク4〜7、時計のない型の08〜0C、MBC30の判別は実機で確かめていない |
| 保存 | 電池RAMは定期保存（変化が5秒止んだら。変わり続けても60秒で）・前の世代・ハッシュの記録・ROMごとのロック。強制終了では最後の保存より後の変化を失い、定期保存がゲームの書きかけの瞬間を写すことは5秒の待ちで減らすがなくせない。状態ファイルは同じ形式番号とレイアウトの指紋のビルドだけが読み、古い状態ファイルは変換しない。巻き戻しは実行中の約30秒分をメモリーに持つだけで、終了・ROMの切り替え・Reset・枠からの復元で消える。電池のないROMはロックしないため、2つのウィンドウから同じ枠へ保存すると後の方が残る |
| Windowsのホスト | ファイルダイアログの後始末（シェル拡張の解放と考えられ、開発に使ったPCで5〜9秒）は別スレッドで続き、その間に開き直したダイアログは表示が遅い。設定の位置は仮想画面だけで判断し、モニターの間の隙間は考えない。物理のマルチタッチ、多様なモニターと音声デバイス、30分を超える連続動作は確かめていない |
| Androidのホスト | Pixel 9aの縦向きだけで確かめる（横向きは扱わない）。曲の鳴り始めに出力が不足して短く途切れることがある（キューの目標が自動で上がった後は起きにくい。Monoのメモリーの回収などの一時的な停止と考えられ、原因は調べていない） |

### 利用者の操作が要る未確認の項目

表示設定・機器の変更、聴き比べ、管理者権限、端末を手で触ることが要るため、利用者の都合がつくときに確かめる。

1. **Windowsの実機の条件**：高DPI（全モニターが96 DPIの環境でだけ確かめた）、音声デバイスの切断と既定のデバイスの切り替え、出力デバイスのあるPCでのGonFox.GameBoy.Platform.WindowsのWASAPIの再生（出力デバイスのないPCでは、「Audio unavailable」と理由を出して無音で続けることを確かめた）、GB風の画面のマウスでの操作。高DPIは表示スケールを125%・150%にして起動し、背景デモで表示面の寸法・画像の整数倍・最大化の往復・最小化から戻した後の最新の画像を見る。音声は、まず音声確認ROMかメガデモで鳴ることを確かめ、再生中に既定のデバイスを外すか無効にして「Audio unavailable」の理由と無音での継続を見て、戻してから「Retry audio」で鳴ること、既定のデバイスを切り替えたときに音が移ることを見る。マウスでは、描画方式・状態の枠・デモの一覧を選んだ後に、最大化と閉じるが効くことを見る。
2. **Androidの手で触る確認**：2本の指の同時押し、ファイルの選択の画面でROM・Boot ROMを選ぶ操作、振動の感触、Pixel 9a以外の端末（画面の大きさ・タブレット・遅い端末）。背景デモで十字キーを押しながらA（反転）かB（背景非表示）を押す、「ROM」「Choose boot ROM」でファイルを選ぶ、ボタンと振動付きのROMで振動を感じる、ほかの端末でレイアウトと完成FPSを見る。
3. **音の聴感評価**：binjgbとの比較のスクリプトが書く2つのWAV（`.cache/megademo-audio`）を人が聴き比べる。
4. **実行スレッドの停滞の原因**：別の作業と重なったときに実行スレッドが20〜120 ms止まることがある。キューの目標を自動で上げて不足・破棄を減らしているが、原因（OSのスレッドの割り当て・ほかのプロセス）は特定していない。スレッドの割り当てのETW（コンテキストスイッチと待機の解除）には管理者権限が要り、短いプロセスを繰り返し起動する負荷では再現しなかった。原因に応じて実行スレッドの優先度・待機の方法を比べる。

### 対象外

CGB（ゲームボーイカラー）・SGB（スーパーゲームボーイ）は、DMGと別のハードウェアの仕様（CGBは倍速のCPU・カラーのパレット・VRAMとWRAMのバンク・HDMA・赤外線、SGBはSNESとのコマンドのパケット・枠の絵など）のため、所有者の判断で対象外とする（Mooneye `boot_*`の他機種版も選ばない）。CGB専用のソフトが使うMBC6・MBC7も扱わない。外部との通信（ケーブル・外部クロック入力・外部機器）も対象外で、未接続のSerialの動作と送信値の観測だけを持つ。

追加機能は該当する境界のテストと既存の選定ROMを通して対応範囲を更新する。テストROMを起動できたことと、その成功条件を満たしたことを区別する。
