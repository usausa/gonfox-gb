# テストROM集

GonFox.GameBoyのテスト（Core.Testsの`TestSuiteTests`）が実行する、自作のDMG用テストROMです。[MITライセンス](LICENSE)で利用できます。CPU命令、命令とメモリーアクセスの時間、HALTの不具合、OAMの破損、割り込み、OAM DMA、PPUの時間、APUを確かめます。

各ROMは測った値（命令の結果のCRC、M-cycleの数と位置、レジスター・フラグ・状態のbit、OAMの内容のCRCなど）を結果のバイト列として出し、合否はROMの中で決めません。期待値は同じROMを[SameBoy](https://github.com/LIJI32/SameBoy)で実行した結果で、[binjgb](https://github.com/binji/binjgb)の結果も並べて記録します。GonFox自身の出力を期待値にしたものはありません。

## ROM

| ROM | 確かめること | 結果（bytes） | ケース |
| --- | --- | ---: | ---: |
| `selftest` | 結果の受け渡し：全バイト値、ADDの結果とフラグ、ワードのバイト順 | 260 | 4 |
| `cpu-instrs` | 通常命令242種（HALT・STOP・CBの接頭辞と未定義の11種を除く）とCB命令256種の結果。境界の値・DAAに関わる値・フラグ16通りを含む32状態（CB命令は16状態）で実行し、AF・BC・DE・HL・SP・メモリーのオペランドかpushした2バイト・分岐の経路をCRC-16にまとめる。DAAはAとフラグの全入力 | 998 | 499 |
| `instr-timing` | 同じ498命令の長さ（M-cycle）。条件付きの16命令は不成立と成立 | 514 | 514 |
| `mem-timing` | 命令の中のメモリーの読み書きが何M-cycle目か（LD・ALU・BIT・POP・RET・PUSH・CALL・RST・INC/DEC (HL)・CBの読み書きの命令、LD (a16),SP） | 153 | 153 |
| `halt-bug` | HALTの不具合（1〜3バイトの命令・RST・読み直したHALT・直前のIE/IFの書き込み）、EIの直後のHALT、IEで許可していない要求、Timerで復帰する位置（IME=0と1） | 124 | 33 |
| `oam-bug` | OAMの破損：INC/DEC rr・LD A,(HL±)・PUSH・POPを、ライン1のMode 2の各M-cycleで実行した後のOAMのCRC-16 | 524 | 262 |
| `irq-timing` | 割り込みを受理する位置（命令の長さ・IFの書き込み・STAT/VBlank・Serial）、EI/DIの遅れ、受理中のIE/IFへのpush、優先順位、HALTからの復帰、Timerの溢れと再ロードの窓、DIV・TACの書き込みによるTIMAの増加、Serialの時刻 | 335 | 218 |
| `oam-dma` | 転送元ごとの転送の内容、転送中のCPUの読み出しと命令の取得（同じバスの競合）、OAMが塞がる期間、再開、転送中のOAMと別のバスへの書き込み、DMAのレジスターとFEA0〜FEFF | 150 | 106 |
| `ppu-timing` | LYの変化、LY=LYC、LCD有効化のラインと1ラインのSTAT・VRAM・OAMの読み出し、SCX・Window・OBJによるMode 3の終わり（Mode 0とその割り込みの位置）、DMGのSTAT書き込みの副作用、LYCの書き込み、STAT・VBlankの割り込みの時刻 | 1,700 | 132 |
| `apu` | レジスターの読み返しと電源、Wave RAM、長さ、DIV-APUの時刻、スイープ（トリガー・段・減算の解除）、DAC、包絡線で音量が0になっても止まらないこと、再生中のWave RAMの読み書きと再トリガー | 777 | 96 |

結果は合計5,535 bytes・2,017ケースです。どのROMも32 KiBのROM Onlyで、GonFoxでは6エミュレーション秒以内（最長は`oam-bug`の約2,163万T）に終わります。測る前にLCD・DIV・Timer・APUを同期させるため、SameBoyのBoot ROMを通しても起動後の状態から始めても同じ結果になります。包絡線の進み方はDMGのレジスターから読めないため測りません。任天堂のロゴは含みません。

## 結果の受け渡し

`src/report.inc`が全ROMに共通の枠組みで、`src/hardware.inc`がレジスターの名前を定めます。開始処理（割り込みの禁止、LCD OFF、WRAM・VRAM・OAM・HRAMの消去、APU OFF）の後に各ROMの`Main`を呼び、`Emit`・`EmitWord`で書いた結果をWRAMに貯めます。

| アドレス | 内容 |
| --- | --- |
| C000 | 状態。結果を画面に出した後で1 |
| C00E〜C00F | 結果の長さ（最大4,078 bytes） |
| C010〜 | 結果 |
| D010〜DEFF | 各ROMの作業領域 |

`Main`から戻ると、長さと結果を2bppのタイルの画素として画面に出し（タイル0〜254を読む順に13行。タイル255は色0〜3を並べた較正用で17行目）、状態を1にして`LD B,B`を実行し、IE=0のHALTで止まります。`TestSuiteTests`はWRAMから、SameBoyとbinjgbの結果は画面の画素から読みます。

## 期待値とテスト

`reference/<ROM>.json`に、期待値とするエミュレーター（`reference`。どれも`sameboy`）、ROMのSHA-256、実行の長さ（binjgbのフレーム数`frames`、GonFoxのT-cycleの上限`maxTCycles`）、ケースの名前と位置（`cases`）、SameBoyとbinjgbの結果を記録します。`TestSuiteTests`はROMのSHA-256を照合し、ケースが結果の全バイトを隙間なく覆うことを確かめてから、ケースごとにGonFoxの結果を比べます。違うケースは、名前と期待値・結果のバイト列を出して失敗します。

- SameBoyがDMGの実機の結果と違うことが分かっている点は、ROMで測りません（[除いたもの](#除いたもの)）。
- GonFoxがSameBoyの挙動を再現していない点は、ケースに`known`（理由）を付けて残します。テストはそのケースを比べず、テストの出力に期待値と結果を書きます（[既知の違い](#既知の違い)）。

## 再生成

通常のビルド・テストには道具もネットワークも要りません。ROMを変えるときだけ使います。

```powershell
./Roms/TestSuite/Build.ps1
./Roms/TestSuite/Reference.ps1
```

- `Build.ps1`は固定版のRGBDS v1.0.4（`.cache/rgbds`へ取得し、ハッシュを照合）で`src/<ROM>.asm`を組み立て、`<ROM>.gb`と`<ROM>.sym`を書きます。`-Name`で一部のROMだけを扱い、`-Verify`はコミット済みのファイルと比べるだけです。
- `Reference.ps1`は先に`Build.ps1 -Verify`を実行し、SameBoyとbinjgbでROMを動かして画面から結果を読み、`reference/<ROM>.json`の設定（`reference`・`frames`・`maxTCycles`・`cases`）を残して書き直します。`-Show`は書かずに表示だけします。
  - SameBoy：固定版のソース（`c458e7c5`、v1.0.3）の`sameboy_tester`を、Visual StudioのC++ Clangツールで`.cache/sameboy-src`に組み立てます。DMG-Bとして`Roms/External/sameboy/dmg_boot.bin`から起動し、`frames`÷60＋3秒動かします。
  - binjgb：v0.1.11の`binjgb-tester`（`.cache/binjgb`へ取得し、ハッシュを照合）。起動後の状態から`frames`フレーム動かします。
  - 2つのテスターはヘッダーのロゴを要するため、`.cache/testsuite-reference`の一時コピーにだけロゴとチェックサムを書きます。

## 既知の違い

GonFoxが再現していないSameBoyの挙動で、`known`を付けたケースです（[互換性と検証](../../docs/Compatibility.md#既知の近似と未対応)）。

| ROM | ケース | 違い |
| --- | --- | --- |
| `apu` | `timing: CH4 trigger at an odd cycle after power-on, NR52 2 cycles later` | 電源投入から奇数M-cycle目にCH4を鳴らすと、SameBoyは開始を遅らせ、GonFoxはすぐに始める |
| `apu` | `sweep trigger delay:`の5件、`sweep clock: shift 1`・`shift 7` | スイープの溢れの判定を、SameBoyはシフト量に応じて遅らせる（トリガーの後は3＋シフト量M-cycle、再トリガーは2＋シフト量、段の後はシフト量だけ）。GonFoxはトリガーの時点と段の4 T後に判定する |
| `apu` | `sweep negate: NR10=01, frequency 555 …` | 加算の計算が2047になった後のNR10の書き込みで、SameBoyはCH1を止める。GonFoxは減算で計算した後に減算をやめた場合だけ止める |
| `oam-bug` | ライン1のDot 0〜3と80〜83に当たる12件（LD A,(HL±)・PUSH・POP） | SameBoyはCPUのアドレスに依る破損を加える。GonFoxはこの位置で破損を加えない |

## 除いたもの

SameBoyがDMGの実機の結果（GambatteのhwtestsのDMG-08の結果）と違い、GonFoxが実機の結果に合う点は、ROMで測りません。

| ROM | 除いたもの | 実機の結果 |
| --- | --- | --- |
| `apu` | DIVの書き込みから1,023 M-cycle後の電源投入で、最初のDIV-APUの段を飛ばすか | `ch2_late_reset_nr52_2b` |
| `irq-timing` | 再ロードのM-cycleにTIMAを書いたときのIF（TIMAの値は測る） | `tc01_late_tima_irq_1` |
| `oam-dma` | FE00・FF00からの転送中のCPUの読み出し（DMGはWRAMのDE・DFのページを読む） | `srcFE00_busypop*` |
| `oam-dma` | 転送元と同じバスへのCPUの書き込み、転送中のHALT、命令の取得で見る再開 | [互換性と検証](../../docs/Compatibility.md#oam-dmaとoamの破損)の表 |
| `ppu-timing` | X=167のOBJがあるときのMode 0の割り込みの位置（STATのモードは測る） | `10spritesPrLine_10xposA7_m0irq_2`・`m2int_wxA6_spxA7_m0irq_2` |

`ppu-timing`はVRAMを8FFFで読みます。SameBoyがMode 3の終わりにbit 12の立ったアドレスを読み替える処理（ソースに未確認の印がある）に当てないためです。

## SameBoyとbinjgbの違い

binjgbの結果は参考に記録します。SameBoyと違うケースの数と主な理由は次のとおりです。

| ROM | 違うケース | 主な理由 |
| --- | ---: | --- |
| `selftest`・`cpu-instrs`・`instr-timing` | 0 | — |
| `mem-timing` | 2 | LD (a16),SPの2バイトを上位から書く |
| `halt-bug` | 13 | EIの直後のHALTで要求があるとHALT+1へ戻る。IME=0のTimerでの復帰が1 M-cycle早い |
| `oam-bug` | 238 | OAMの破損を再現しない |
| `irq-timing` | 38 | SerialのクロックがDIVにそろわない。Mode 2の割り込みが1 M-cycle遅い。再ロードの窓、TACの書き込み、受理中のpushの扱いが違う |
| `oam-dma` | 37 | バスの競合がない。FE00・FF00がWRAMの写しにならない。DMAのレジスターとFEA0〜FEFFがFFを読む |
| `ppu-timing` | 73 | Windowの遅れ、OBJによるMode 3の延び、Mode 0・Mode 2の割り込みの位置、DMGのSTAT書き込みの副作用が違う |
| `apu` | 14 | フレームシーケンサーがDIVでなく自分の数で進む。CH4の開始の遅れ、スイープの判定の遅れ、DMGのNR10の規則がない |
