# SM83 命令対応表

現行版は通常側の定義済みコード245種（CBプレフィックスを含む）とCB側256種を実装する。割り込み受理、HALTの待機・復帰、STOPの4通りの分岐と入力による復帰も扱う。機種とブロック間の契約は[現行仕様](Specification.md)を参照。

命令の振り分け・転送・分岐・クロックは [Sm83Cpu.cs](../GonFox.GameBoy.Core/Cpu/Sm83Cpu.cs)、演算・フラグ・CB 処理は [Sm83Cpu.Alu.cs](../GonFox.GameBoy.Core/Cpu/Sm83Cpu.Alu.cs)。同じ CPU クラスを処理の種類で 2 ファイルに分け、デコーダー生成や共通デバイス基底クラスは使わない。

## 表の読み方

コードは 16 進数、時間は T-cycles。`r` は B/C/D/E/H/L/A、`rr` は BC/DE/HL/SP、`qq` は BC/DE/HL/AF。`d8/d16` は即値、`a8/a16` はアドレス、`e8` は符号付き変位、`cc` は NZ/Z/NC/C。条件分岐の時間は「成立 / 不成立」の順。

命令内の読み出し・書き込み・内部待機は各4 T-cycles。読み書きの後、そのM-cycleの4 T-cyclesを進める。各T-cycleで累積時間、Timer（Serialのクロックを含む）、DMA、PPUの順に更新する。表の時間はフェッチ込みであり、命令終了時に別途加算しない。

## 通常命令

以下の表は全 256 コードを分類したもの。範囲内で対象に `(HL)` が含まれる場合だけ、メモリー読み書きの時間が増える。

| コード | 命令 | bytes | T-cycles |
| --- | --- | --- | --- |
| `00` | NOP | 1 | 4 |
| `01/11/21/31` | LD rr,d16 | 3 | 12 |
| `02/12` | LD (BC/DE),A | 1 | 8 |
| `0A/1A` | LD A,(BC/DE) | 1 | 8 |
| `22/32` | LD (HL+/-),A | 1 | 8 |
| `2A/3A` | LD A,(HL+/-) | 1 | 8 |
| `03/13/23/33` | INC rr | 1 | 8 |
| `0B/1B/2B/3B` | DEC rr | 1 | 8 |
| `04/0C/14/1C/24/2C/3C`、`34` | INC r / INC (HL) | 1 | 4 / 12 |
| `05/0D/15/1D/25/2D/3D`、`35` | DEC r / DEC (HL) | 1 | 4 / 12 |
| `06/0E/16/1E/26/2E/3E`、`36` | LD r,d8 / LD (HL),d8 | 2 | 8 / 12 |
| `07/0F/17/1F` | RLCA / RRCA / RLA / RRA | 1 | 4 |
| `08` | LD (a16),SP | 3 | 20 |
| `09/19/29/39` | ADD HL,rr | 1 | 8 |
| `10` | STOP 00 | 2 / 1 | 4（ボタンと割り込みの要求で4通り。下記） |
| `18` | JR e8 | 2 | 12 |
| `20/28/30/38` | JR cc,e8 | 2 | 12 / 8 |
| `27/2F/37/3F` | DAA / CPL / SCF / CCF | 1 | 4 |
| `40–7F`（`76` を除く） | LD r,r / LD r,(HL) / LD (HL),r | 1 | レジスタ間 4、それ以外 8 |
| `76` | HALT | 1 | 4（以後は待機 4 ずつ） |
| `80–87` / `88–8F` | ADD A,r / ADC A,r（`86/8E` は (HL)） | 1 | レジスタ 4、(HL) 8 |
| `90–97` / `98–9F` | SUB A,r / SBC A,r（`96/9E` は (HL)） | 1 | レジスタ 4、(HL) 8 |
| `A0–A7` / `A8–AF` | AND A,r / XOR A,r（`A6/AE` は (HL)） | 1 | レジスタ 4、(HL) 8 |
| `B0–B7` / `B8–BF` | OR A,r / CP A,r（`B6/BE` は (HL)） | 1 | レジスタ 4、(HL) 8 |
| `C6/CE/D6/DE/E6/EE/F6/FE` | ADD/ADC/SUB/SBC/AND/XOR/OR/CP A,d8 | 2 | 8 |
| `C0/C8/D0/D8` | RET cc | 1 | 20 / 8 |
| `C1/D1/E1/F1` | POP qq | 1 | 12 |
| `C2/CA/D2/DA` | JP cc,a16 | 3 | 16 / 12 |
| `C3` | JP a16 | 3 | 16 |
| `C4/CC/D4/DC` | CALL cc,a16 | 3 | 24 / 12 |
| `C5/D5/E5/F5` | PUSH qq | 1 | 16 |
| `C7/CF/D7/DF/E7/EF/F7/FF` | RST 00/08/10/18/20/28/30/38 | 1 | 16 |
| `C9` / `D9` | RET / RETI | 1 | 16 |
| `CB` | 次のバイトを CB 命令として実行 | 合計 2 | 下表に含む |
| `CD` | CALL a16 | 3 | 24 |
| `E0` / `F0` | LDH (a8),A / LDH A,(a8) | 2 | 12 |
| `E2` / `F2` | LDH (C),A / LDH A,(C) | 1 | 8 |
| `E8` | ADD SP,e8 | 2 | 16 |
| `E9` | JP HL | 1 | 4 |
| `EA` / `FA` | LD (a16),A / LD A,(a16) | 3 | 16 |
| `F3` / `FB` | DI / EI | 1 | 4 |
| `F8` | LD HL,SP+e8 | 2 | 12 |
| `F9` | LD SP,HL | 1 | 8 |
| `D3/DB/DD/E3/E4/EB/EC/ED/F4/FC/FD` | 実機の未定義命令 | 1 | フェッチ 4 の後に CPU が停止（以後は 4 ずつ時間だけ進む） |

`LDH` は `FF00 + a8/C` へアクセスする。`LD (a16),SP` は下位→上位、PUSH/CALL/RST は SP を減らしながら上位→下位、POP/RET は下位→上位の順で転送する。PC/SP/HL のアドレス演算は 16 bit で折り返す。POP AF は F の下位 4 bit を常に 0 にする。

INC/DEC rr の内部待機、PUSH・CALL・RST の書き込み前の内部待機、割り込み受理の 2 つ目の内部待機（SP）、LD SP,HL の内部待機（HL）では、16 bit の増減の回路（IDU）が値をアドレスバスへ出す。その値が FE00–FEFF で PPU が Mode 2 なら、DMG では OAM を壊す（Pan Docs、SameBoy、blargg `oam_bug`。[OAMの破損](Specification.md)）。ADD HL,rr・LD HL,SP+e8・ADD SP,e8 の内部待機は IDU を使わない（blargg `oam_bug` の 3-non_causes）。POP・RET・LD A,(HL±)・LD (HL±),A は FE00–FEFF への読み書きとして壊す。

## CB 命令

CB の後のバイトを表す。各 8 コードの下位 3 bit は順に B/C/D/E/H/L/(HL)/A。BIT/RES/SET の範囲は、対象 bit 0〜7 の順に 8 コードずつ並ぶ。時間・長さは CB プレフィックス込み。

| コード | 命令 | レジスタの時間 | (HL) の時間 |
| --- | --- | --- | --- |
| `00–07` | RLC | 8 | 16 |
| `08–0F` | RRC | 8 | 16 |
| `10–17` | RL | 8 | 16 |
| `18–1F` | RR | 8 | 16 |
| `20–27` | SLA | 8 | 16 |
| `28–2F` | SRA | 8 | 16 |
| `30–37` | SWAP | 8 | 16 |
| `38–3F` | SRL | 8 | 16 |
| `40–7F` | BIT b,r/(HL) | 8 | 12（読み出しのみ） |
| `80–BF` | RES b,r/(HL) | 8 | 16 |
| `C0–FF` | SET b,r/(HL) | 8 | 16 |

## フラグの要点

F の上位から Z/N/H/C。8 bit の加減算では bit 3、ADD HL では bit 11 の桁上がり・借りを H に反映する。

- ADD/ADC は N=0、SUB/SBC/CP は N=1。CP は A を変更しない。
- INC/DEC は C を保持する。16 bit の INC/DEC は全フラグを保持し、ADD HL は Z を保持する。
- ADD SP,e8 と LD HL,SP+e8 は Z=N=0。結果には符号付き変位を加えるが、H/C は変位を符号なしの下位バイトとして計算する。
- AND は H=1、XOR/OR は H=0、いずれも N=C=0。Z は結果から定める。
- DAA は前の N/H/C と A から BCD 補正する。N を保持、H を解除し、Z/C を更新する。減算後は C を保持する。
- CPL は Z/C を保持して N=H=1。SCF/CCF は Z を保持し、N/H を解除する。
- 通常側の A 回転命令は必ず Z=0。CB 側の回転・シフト・SWAP は結果から Z を定め、N/H を解除する。SWAP は C=0。
- BIT は C を保持、N=0、H=1、対象 bit が 0 なら Z=1。RES/SET は全フラグを保持する。

## 実行状態と割り込み

| 操作 | 現行の動作 |
| --- | --- |
| DI | IME と EI の保留を即時解除 |
| EI | 次の命令の完了後に IME を有効化。EI;EI でも最初の EI の期限を延ばさない |
| RETI | スタックから復帰して IME を即時有効化 |
| 割り込み受理 | 命令境界で IME と IF & IE を確認し、5 M-cycle（20 T-cycles）で受理する。IME を解除し、内部待機 3 回の後に PC の上位バイト、続いて下位バイトを push する。上位 push の後の IE と、下位 push の書き込み直前（開始から 16 T）の IF から VBlank / STAT / Timer / Serial / Joypad の順で 1 件選び、その 2 T 後に IF の該当 bit を解除してベクターへ移る。上位 push が IE を書き換えた場合はその値で選び直し、該当なしなら IF を変えず PC=0000（Mooneye `ie_push`）。下位 push による IF への書き込みは選択に効かない |
| IF 書き込み | CPU の書き込みは M-cycle の 1 T 目の後に効き、同じ 1 T 目に周辺が立てた要求も書いた値で上書きする。読み出しは M-cycle の始まりの値 |
| HALT | 命令フェッチを止め、ステップごとに 4 T-cycles 進める。各待機 M-cycle の 2 T 目に IF & IE を標本化し、要求があればその M-cycle の終わりで復帰する。後半 2 T の要求は次の M-cycle で気付く。HALT 直後の最初の待機 M-cycle だけは始まりで標本化し、その M-cycle の要求は次の M-cycle で気付く。IME が有効なら次のステップで受理する。OAM DMA は最初の待機 M-cycle まで転送し、2 つ目から復帰する M-cycle まで止まる（Gambatte `oamdma_late_halt_stat`・`oamdmasrc80_halt_*_read8000`）。実行 API（`RunForTCycles`）では、どの周辺も動かず要求も待っていない待機 M-cycle をまとめて進める（結果は 1 M-cycle ずつと同じ。[現行仕様](Specification.md)） |
| HALT bug | HALT 実行時に IF & IE に要求があれば停止しない。IME=0 かつ EI 保留なしなら次の命令フェッチの PC 加算を 1 回抑止する。IME=1 または EI 直後なら受理後の戻り先を HALT 自身にする（Pan Docs） |
| STOP | フェッチの後に、P1 で選んだ行のボタンの押下（P10〜P13 のどれかが低い）と IE & IF の要求で 4 通りに分かれる（下記）。STOP に入ると DIV をリセットして停止し、選んだ行の線が下がるまで追加呼び出しは消費 0、`IsStopped=true`。予算が余っていても戻る |
| Reset / カートリッジ挿入 | IME・EI 保留・HALT・STOP・CPU の停止を解除 |

IME、EI 保留、HALT、STOP は `DebugSnapshot` の値として観測できる。HALT/STOP は `GameBoySystem.IsHalted/IsStopped` からも確認できる。

STOP は Pan Docs（Reducing Power Consumption）の分岐図と SameBoy（`c458e7c5` の `stop`）に従い、フェッチ（4 T）の後に次の 4 通りに分かれる。

| 選んだ行のボタン | IE & IF の要求 | 動作 |
| --- | --- | --- |
| 押されている | あり | 1 バイトの NOP（4 T）。DIV はそのまま進む |
| 押されている | なし | 次のバイトを読む M-cycle（時間は進む。合計 8 T）の後に HALT する（HALT 直後として扱う）。DIV はそのまま進む |
| 押されていない | あり | STOP に入る（DIV をリセット）。次のバイトは読み飛ばさず、復帰の後に命令として実行する（IME が有効なら割り込みの後） |
| 押されていない | なし | STOP に入る（DIV をリセット）。次のバイトを読み飛ばす（2 バイト） |

STOP に入った後に選んだ行の線が下がると（押下か P1 の行選択の変更。Joypad の割り込みを要求するのと同じ立下り）、次の正の予算の実行またはステップで復帰する。立下りは記録するため、次の実行までにボタンを離しても復帰する。STOP に入る前の押下、選んでいない行の押下では復帰しない。状態を復元した直後は、選んだ行の線が低ければ復帰する。行を選んでいない（P1=$30）ときは Reset だけが戻す。入りと復帰は時間を消費せず、IME と IF & IE が揃っていれば続けて受理する。SameBoy の復帰後の 8 T と IME=0 のときの DIV の保持（SameBoy でも未確定の印）、DMG の P1 の選択解除の遅れ、ボタンのチャタリング、アナログ条件、STOP 中の LCD の表示は扱わない（DMG で自動判定できる実機のテスト ROM がない）。

HALT の標本化位相は SameBoy の DMG モデルに合わせた。PPU の Mode/LY=LYC/VBlank の変化は M-cycle の 2 T 目までに起きるため HALT でも NOP 待ちと同じ境界で受理し、Mode 2 の要求と Timer の再ロードは後半に起きるため HALT 中は 1 M-cycle 後に受理する（Mooneye `halt_ime1_timing2-GS`・`intr_2_*`、gbmicrotest `oam_int_halt_*`/`oam_int_nops_*`）。HBlank の要求は SCX mod 8・Window・OBJ で M-cycle の中の位置が変わり（最後の画素の位置の OBJ（X=167）の取得は待たない：Gambatte `10spritesPrLine_10xposA7_m0irq_2`）、同じ規則で 2 T 目までなら同じ境界、後半なら HALT 中は 1 M-cycle 後に受理する（gbmicrotest の注記のない `int_hblank_halt_scx0`〜`7` と `int_hblank_nops_scx0`〜`7` が診断の実行で一致）。LYC の書き込みは 1 T 後から LY=LYC の比較に効く（Gambatte `lycdisable_ff45_scx3_3`）。HALT 直後の最初の待機 M-cycle は始まりで標本化するため、その前半に立つ要求（SCX=3 の HBlank など）も 1 M-cycle 後に受理する（SameBoy の `just_halted`、Gambatte `late_m0irq_halt_m0stat_scx3` の DMG-08 の結果）。受理での IF の標本化と解除の位置、IF 書き込みの位置は、Gambatte の `late_m0irq_vs_tima`・`lycint143_m1irq_late_retrigger`・`m2int_m0irq_scx3_ifw`（DMG-08）と gbmicrotest `hblank_scx3_if` に合わせた。

未定義11命令は、実機と同じく CPU を止める（ロックアップ）。フェッチの時間と PC 進行を残し、アドレスとコードを`CpuFault`として`GameBoySystem.Fault`に記録して`IsFaulted`を立てる。以後のステップと実行は 1 M-cycle（4 T）ずつ時間だけを進め、PPU・Timer・APU・DMA・Serial は動き続け、割り込みでは再開しない。Reset・ROM交換・止まる前の状態への復元で戻る。定義済み命令の実装漏れは、全コードの実行・時間テストと命令群の結果テストで検出する。

## 検証と参照

命令の単体テストは外部ROM・ネットワーク・WPFを必要としない。全体の確認結果と公開ROMは[互換性と検証](Compatibility.md)を参照。

- 通常側は定義済み 245 コードをすべて実行して時間を照合し、未定義 11 コードは CPU の停止（時間は進み、割り込みで再開しない）・診断・Reset と復元を検証する。
- CB 側は全 256 コードの値・フラグ・対象以外のレジスタ保持・メモリー・時間を検証する。
- 各条件分岐は NZ/Z/NC/C の成立・不成立を確認する。CALL/RET の戻り先、スタックのバイト順とアクセス時刻も確認する。
- DAA は ADC/SBC 後の 00〜99 の全組み合わせと入力キャリー 0/1、計 40,000 組を 1 件のテスト内で検証する。期待値は独立した 10 進演算から求める。
- 符号付き加算、桁境界、アドレス折り返し、EI の遅延・取消、HALT の待機、STOP 中の消費 0 応答を確認する。STOP の 4 通り（時間・PC・DIV・HALT 直後の印、パディングが未定義命令のときに 1 バイトの形だけ CPU が止まること、IME が有効なら割り込みがパディングより先）、選んだ行の立下りだけでの復帰、行を選ばないときは Reset だけで戻ること、復元後の復帰を `StopTests`（15 件）で確認する。
- 受理途中の IE 書き換え（取消・選び直し・遅すぎる下位 push）、受理での IF の標本化（下位 push の時点）、IF 書き込みの位置、EI 直後の HALT の戻り先、HALT の標本化位相（HALT 直後の最初の M-cycle を含む）、STOP 復帰後の受理を確認する。
- IDU で OAM を壊す命令（INC/DEC rr の 8 種・PUSH・CALL・RST・LD SP,HL・割り込みの受理）と壊さない命令（ADD HL,rr・LD HL,SP+e8・ADD SP,e8）、HALT 中の OAM DMA の停止と復帰後の再開（HALT の途中の状態の復元を含む）を確認する。

長さ・時間は[gbdevの命令データ](https://github.com/gbdev/gb-opcodes/blob/master/Opcodes.json)、[命令表](https://gbdev.io/gb-opcodes/optables/)、[Pan Docs](https://gbdev.io/pandocs/CPU_Instruction_Set.html)を参照する。テストの期待値は定数と数値例に固定し、実装から生成しない。固定版MooneyeでもDAA・Fレジスタ・割り込み・HALT/EI・Timer、命令ごとのメモリーアクセス時刻（`call_timing`等。DMA中に別バスから命令を読む）を検証する。命令の結果（`cpu-instrs`）・命令の長さ（`instr-timing`）・命令の中のメモリーアクセスの時刻（`mem-timing`）・HALTの不具合（`halt-bug`）・OAMの破損（`oam-bug`）は、[テストROM集](../Roms/TestSuite/README.md)でSameBoyの結果と比べる。
