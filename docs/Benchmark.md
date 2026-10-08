# ベンチマーク

`Benchmark`は、GonFox.GameBoy.Coreの速度・管理確保・生成コードをBenchmarkDotNetで測る開発用のコンソール（net10.0、BenchmarkDotNet 0.15.8）である。参照するのはCoreだけで、内部の`Ppu`・`MemoryBus`などはCoreの`InternalsVisibleTo`で使う。ホスト（GonFox.GameBoy.Platform.*・Example.GameBoy.*）は測らない。性能は推測で決めず、この計測と等価性の検証で判断する。エミュレーターの仕様は[現行仕様](Specification.md)、テストROMとの一致は[互換性と検証](Compatibility.md)、ビルドとテストの実行は[README](../README.md)にある。

## 実行方法

Release・デバッガーなしで、リポジトリーのルートから実行する。ROMは実行ファイルの位置から`GonFox.GameBoy.slnx`のあるディレクトリーまでさかのぼって探すため、チェックアウトの中で動かす。使うROMは次の4つで、SHA-256が`Workloads.cs`の値と違えば例外で止まる。

| ROM | ケース |
| --- | --- |
| `Roms/BackgroundDemo/background-demo.gb` | Background |
| `Roms/BackgroundDemo/mbc1-demo.gb` | Mbc1Background |
| `Roms/External/dmg-acid2/dmg-acid2.gb` | Acid2 |
| `Roms/MegaDemo/megademo.gb` | MegaWave・MegaPlasma・MegaOrbit |

```powershell
dotnet run --project Benchmark -c Release -- --verify
dotnet run --project Benchmark -c Release -- --list flat
dotnet run --project Benchmark -c Release -- --filter *
dotnet run --project Benchmark -c Release -- --filter '*ExecutionBenchmarks*' --artifacts .cache/benchmark/execution
```

- `--verify`だけを渡すと、BenchmarkDotNetを動かさずに後述の検証を行い、結果をJSONで標準出力へ書く。
- `--list`（`flat`または`tree`）と`--help`は検証を省き、BenchmarkDotNetの一覧またはヘルプを出して0で終わる。
- それ以外は、先に全ケースの検証を行ってから引数をBenchmarkDotNetへ渡す。そのため`--filter`で一部だけを測るときも4つのROMが要る。`--filter`を省くと、測るものを対話で選ぶ。
- ROMの不足・不一致と検証の不一致は例外で止まり、終了コードは0以外になる。BenchmarkDotNetの結果が1つもない、致命的な検証エラーがある、ビルド・実行に失敗した、統計のない（NA）ケースがある場合は1を返す。NAを成功と扱わない。
- 結果は既定で`BenchmarkDotNet.Artifacts/results`（Gitの対象外）に、`--artifacts <ディレクトリー>`を付ければその`results`に出る。BenchmarkDotNetの既定の要約（CSV・Markdown・HTML）に加えて、全データのJSON（`*-report-full.json`）、計測ごとのCSV（`*-measurements.csv`）、生成コード（`*-asm.md`）を書く。

## ジョブの設定

`Program.cs`は`DefaultConfig`に次の設定を足す。

| 項目 | 設定 |
| --- | --- |
| ジョブ | `Job.Default`にID `Default`を付け、.NET 10（`CoreRuntime.Core10_0`）のJITで動かす |
| 回数 | 独立した2プロセス（LaunchCount 2）。各プロセスでウォームアップ5回・計測10回 |
| 呼び出し | UnrollFactor 1。ExecutionとPPUは`[InvocationCount(1)]`で1 Iterationに1回だけ呼び、その前に`[IterationSetup]`で準備した状態へ戻す（計測外）。BusとStateの呼び出し回数はBenchmarkDotNetが決める |
| 診断 | `MemoryDiagnoser`（1回あたりの管理確保）、`DisassemblyDiagnoser`（探索の深さ2、ソース付き） |
| 出力 | 既定の出力に加えて`JsonExporter.Full`と`CsvMeasurementsExporter.Default` |

生成コードのサイズは深さ2までで取得できた範囲であり、Core全体の大きさではない。逆アセンブルを取得できないケースのNAを0 Bと扱わない。同じビルドでも回ごとにインライン化の判断（動的PGO）が変わり、サイズが揺れることがある。

## ベンチマークとケース

| メソッド | ケース | 1回の処理 |
| --- | --- | --- |
| `ExecutionBenchmarks.RunFourSeconds` | `ExecutionCase`の10ケース | 準備した状態から、システム全体を4エミュレーション秒（16,777,216 T。命令の境界までわずかに超えることがある）実行する |
| `PpuBenchmarks.Render240Frames` | `PpuCase`のBackground・Window・Sprites | PPUだけを240フレーム（16,853,760 T）進める |
| `BusBenchmarks.ReadWrite4096` | `Mbc1`がfalse（ROM Only）・true（MBC1） | バンクの切り替え・WRAMへの書き込み・切り替え側のROMとEcho RAMの読み出しを4,096回行う |
| `StateBenchmarks` | Background・Mbc1Background | 準備した状態で、`Capture`は状態の取得、`Serialize`はバイト列への変換、`Deserialize`はバイト列からの状態の作成、`Restore`はシステムへの復元、`CopyFrame`は画像（92,160 bytes）のコピーを1回行う |

| ケース | 負荷と準備 |
| --- | --- |
| Alu・MemoryBranch・TimerDma・Sound | コードで作る小さなROM（LCD OFF、HALTなし）。Aluは算術・論理・CB・条件分岐、MemoryBranchはWRAMの読み書き・転送・条件分岐を繰り返す。TimerDmaはHRAMでOAM DMA（転送元C000）の起動と648 Tを超える待ちを繰り返し、Timerも進める。Soundは4チャネルを両側へ出し続け、約4,100 TごとにCH1の周期をDIVの値で書き換える |
| Background・Mbc1Background | 背景デモとそのMBC1版。起動から1エミュレーション秒進めた状態から、Rightを押し続ける |
| Acid2 | dmg-acid2。起動から1エミュレーション秒進めた状態から、入力なしで進める |
| MegaWave・MegaPlasma・MegaOrbit | 曲付きのメガデモ。起動1秒後にSelectで演出を選び、上を約1秒押して量を7にした状態から、入力なしで動かす。毎フレームHBlank割り込み144回とOAM DMAがある |
| PPUの3ケース | PPUだけに、単色のタイル（BG・Window・OBJで別の色）と40個のOBJ（1ラインに最大10個）を置く。WindowケースはWindow（x=80、y=32から）を、SpritesケースはOBJを有効にする |
| Busの2ケース | ROM Only（32 KiB）またはMBC1（ROM 64 KiB、RAM 32 KiB）。各バンクをそのバンク番号で埋める |

背景デモ2本とacid2は4秒の97.6〜99.5%をHALTで待ち、メガデモはCPUが約6割の時間で命令を実行する。HALTが支配的なROMだけでCPUの速さを評価しないよう、HALTのない合成ROMを用意している。PPUの単色のパターンは正解を式で計算しやすい合成負荷で、ゲームの画像の密度を代表しないため、既存のROM・メガデモと合わせて判断する。計測区間には、ROMの読み込み・準備・状態の比較・ファイル処理を含めない。

`Workloads.cs`はGonFox.GameBoy.Core.Testsにもソースとして取り込まれ、BenchmarkDotNetとホストの型を含まない。`BenchmarkWorkloadTests`は、合成ROMがHALT・STOPせずに狙った部品を動かすこと、PPUの画像が式どおりであること、Busのチェックサムを確かめる。`MegaDemoTests`は、メガデモの3ケースが各演出を量7で選び、4秒のあいだ動き続けることを確かめる。

## 検証（`--verify`）

速さを比べる前に、同じ処理をしていることを`BenchmarkVerification`で確かめる。次のどれかが違えば例外で止まる。

- Execution：4秒を1回の予算で実行する。準備した状態へ戻すと初期状態の指紋に一致し、4,096 Tずつに分けて実行しても実消費Tと最終状態の指紋が同じになり、`StepInstruction`で1命令ずつ同じ目標まで進めても最終状態の指紋が同じになること。1命令ずつの実行では、HALT中のTとHALT以外のステップ数も数える（計測外）。
- PPU：240フレームが完成し、全画素が独立した式（BG・Window・OBJの色、A=255）と一致し、戻して描き直しても同じ画像になること。
- Bus：2回続けて、チェックサムが526,336（ROM Only）・530,431（MBC1）になること。

JSONには、Executionの各ケースのROMのSHA-256、入力（終了時のジョイパッド）、初期の累積Tと状態の指紋、実消費T、完成フレーム数、HALT以外のステップ数、HALT中のT、最終状態と最新の完成画像のSHA-256が入る。PPUは画像のSHA-256、Busはチェックサムを出す。状態の指紋は、状態の全ブロック（両方の画像、隠れた位相、全RAM、未読のシリアルのbyte、未取得のPCMを含む）をJSONにしたもののSHA-256である。これは再現性と等価性の検証で、実機との一致を主張するものではない。状態形式の変更やハードウェアの精度の修正で正当に変わるため、変わったときは理由を残す。

## 参考値

最後に計測した値。記録を取った機械（Ryzen 9 5900X、Windows 11、高パフォーマンス電源）で、上のジョブ（.NET 10のJIT）を使った。外乱は時間を増やす向きにしか働かないため、同じコードを複数回測った中で最も速い回の値を載せる。別の時間帯の値には環境の揺れが含まれるので、変更の判断にはこの表を使わず、同じ時間帯に交互に測った値で比べる。

| ベンチマーク | ケース | 時間 |
| --- | --- | ---: |
| Execution（4エミュレーション秒） | Alu / MemoryBranch / TimerDma / Sound | 33.92 / 28.10 / 58.02 / 35.10 ms |
| Execution（4エミュレーション秒） | Background / Mbc1Background / Acid2 | 15.82 / 15.66 / 24.04 ms |
| Execution（4エミュレーション秒） | MegaWave / MegaPlasma / MegaOrbit | 46.89 / 46.12 / 51.38 ms |
| PPU（240フレーム） | Background / Window / Sprites | 17.21 / 18.58 / 20.17 ms |
| Bus（4,096ループ） | ROM Only / MBC1 | 24.58 / 26.16 µs |

Executionの実時間に対する倍率は、4,000 msをこの値で割ったものになる（Backgroundで約250倍、MegaOrbitで約78倍）。Busは同じコードでも回ごとに23〜36 µsの幅がある。StateBenchmarksは同じコードでも回ごとに20〜50%揺れるため、参考値を置かない。

## 性能の方針

### 毎Tと毎M-cycleの経路

- 毎Tに動くのは`Clock.AdvanceTCycles`が呼ぶ`Timer`・`OamDma`・`Ppu`の`Tick`だけで、これらはインライン化される。`Tick`には毎Tの判定（PPUは次に処理のあるdotか、OAM DMAはM-cycleの区切りと転送の有無）だけを置き、処理は`[MethodImpl(MethodImplOptions.NoInlining)]`の別のメソッド（`Ppu.RunEventDot`・`OamDma.EndMachineCycle`）へ出す。処理を`Tick`に入れると`Tick`がインライン化されなくなり、PPUだけの描画に約2倍かかった。
- M-cycleごとの処理も毎回の呼び出しにせず、インラインの空き判定の後ろに置く（毎M-cycleの呼び出しはCPUのケースを遅くした）。HALTの待機中のOAM DMAの停止のように、続く状態で要る設定はM-cycleごとにやり直さず、入るときと出るときだけ変え、状態の復元ではCPUの状態から求め直す。
- APU・シリアル・MBC3の時計・特殊カートリッジは毎Tの処理を持たない。APUはアクセス・DIV-APUの立下り・実行の終わりで追いつき、出力が一定の区間をまとめて混合へ積分する。シリアルはDIVのカウンターのbit 7の立下りで、時計はアクセスの時点の累積Tの差で進む。Timerは選択ビットをTACの書き込みのときにマスクにし、立下りを`前の値 & ~新しい値 & マスク`で判定する。毎Tの呼び出しを足すと実行系全体が遅くなるため、部品は必要な時点でまとめて追いつく形を基本にする。
- HALT中は、どの部品も動かないM-cycle（聞き手のあるDIVのbitが落ちない、TIMAの再ロードがない、PPUの次の処理のdotより前、標本化を待つ割り込みの要求がない。OAM DMAはHALTの2つ目のM-cycleから止まる）を`Sm83Cpu.Run`がまとめて進める。実行予算の終わりは、1 M-cycleずつ進めたときと同じM-cycleになる。
- 命令のM-cycle（`Clock.AdvanceMachineCycle`）は、Timer・PPU・DMAが4 Tのあいだ動かなければ一度に進め、そうでなければ4回`Tick`する。HALTとロックの待機（`WaitHalted`・`WaitLocked`）は`AdvanceTCycles(4)`のままにする（ここで一括の判定を使うと、HALTの多いケースが遅くなった）。
- こうした近道は、毎Tに進めた結果と完全に同じでなければならない。入れる前のツリーと`--verify`の全ケースが一致することと、`HaltSkipTests`などのテストで確かめる。
- `StepInstruction`はJITのインライン化の予算に達している。1行のヘルパーへの`AggressiveInlining`は生成コードを増やすだけで速くならなかった。毎Tや毎M-cycleの経路を変えたら、`DOTNET_JitDisasm`でTier1のコードの大きさと残る呼び出しを確かめる。

### PPU

- 表示ラインは描画の始め（Dot 86）に行の終わりまで計算し、Mode 3中にPPUのレジスタが書かれたときだけ、書き込みの位置まで確定して残りを計算し直す。
- 書き込みのない行は一度に描く。タイル行の2バイトを256項目の展開表で8画素にし、OBJはX順に並べて各タイル行を1回だけ展開し、`VideoOutput.WriteLine`で1行を書く。WindowとOBJが同じ行など、一括の描画が扱わない行だけを取得器（ピクセルFIFO）で1 dotずつ計算する。両者の一致は`PpuWholeLineTests`の差分テストで確かめる。
- 書き込みのない行のOAM走査は、Dot 84で残りの項目を一度に読む。
- 行ごとのループは位置や個数をローカル変数に置き、終わりの判定をループの前に1回だけ求める（フィールドを毎回読み書きすると、PPUだけの描画が5〜8%遅くなった）。
- 作業領域（色・濃淡・OBJ・展開したタイル行・並び順）はPPUが持つ固定長の配列で、行ごとに作り直し、状態には含めない。

### 確保

- 命令の実行・PPU・Bus・状態の復元・画像のコピーは、定常の管理確保を0 Bに保つ（MemoryDiagnoserの列で確かめる）。PCMの読み出し（`AudioOutput.ReadFrames`）も呼び出し側のバッファーへ書き、確保しない。
- 状態の取得は所有する写しを、`Serialize`・`Deserialize`はバイト列と配列を作るため、意図して確保する（状態の大きさはBackgroundで約210 KB、Mbc1Backgroundで約243 KB）。初期化や状態の保存の意図したコピーを、0 Bの目標のために削らない。

### 生成コードとJIT

- `Clock`などの具象型を保つ。.NET 10のPGOはinterfaceの呼び出しを最適化でき、関数ポインターがかえって遅い例もあるため、`ICartridge`の削除や関数ポインター化を先に決めない。
- 小さなCPU・PPUのヘルパーは生成コードを見て個別に判断し、属性を一括で足さない。
- 走査線やPCMのループはSpanを局所に取り、既存の配列を再利用する。ループの構文を大量に置き換えるより、配列の参照・長さ・計算を局所に置く効果を測る。
- `unsafe`・手動の`Unsafe.Add`・SIMD・プール・NativeAOTは、採用の条件を測って示せたときの候補で、標準の方針ではない。タイルのキャッシュとBGPの対応付けのSIMDは、描画に占める割合が小さく見送った。
- 速さはJIT（.NET 10）で測る。Example.GameBoy.MauiHostのAndroidのRelease版は、LLVMで全メソッドを事前コンパイルする（既定のprofiled AOTとJITより実行スレッドの負荷が約半分。APKが大きくなり、ビルドが長くなる）。

### ホスト

- 画像の受け渡し（GonFox.GameBoy.Platform.Sharedの`FrameExchange`）は、実行スレッド（書き手）と表示するスレッド（読み手）の間を3枠でlockなしに行う。書き手は自分の枠へ写して共有の枠と交換し、読み手は新しい画像があるときだけ交換して、受け取った枠から表示用の画像へ直接写す。読まれなかった画像は次の画像で置き換わり、最新だけを表示する。全画像を順に渡すキューは遅延を増やすため使わない。
- 停止中・STOP中・最小化中・非表示のあいだは、毎フレームの描画の呼び出しを受けない（`DisplayPolicy.WantsEveryFrame`）。状態の表示と、1命令・Reset・状態の復元で変わった画像は遅いタイマーで出す。
- Example.GameBoy.WpfHostは描画方式を実行中に選べる。SkiaSharp（既定）は表示面をCPUで拡大し、WriteableBitmapは160×144だけを書いてWPFの合成で拡大するため、大きな窓ではCPUの使用が少ない。

### 残る候補

いまの速度で困ってはいないため、必要になったときに扱う。

- WindowとOBJが同じ行の一括の描画（dmg-acid2では約12%の行がこれにあたる）。OBJの取得待ちとWindowの開始の重なりを式にし、差分テストで確かめる。
- 速いクロックのNoise（ハイハットは16 Tごと）の区間をまとめて混合へ積分する方法。先に内訳を測る。
- MemoryBus・MBCの分岐の並べ替え（見込みは約1%で保留）。
- Androidで画像の拡大をGPUに任せる表示（Androidのビュー・`SKGLView`）。

## 変更の測り方と判断

1. 条件をそろえる。Release・デバッガーなし・同じ機械・SDK・ランタイム・電源設定で測る。計測中はビルド・テスト・ほかの計測のほか、ファイルの編集やスクリプトの実行もしない。BenchmarkDotNetは実行のたびにベンチマークのプロジェクトをソースからビルドするため、計測中にソースを変えると測る対象が変わり、並行した作業は外乱にもなる。
2. 先に等価性を確かめる。比べる2つのツリーで`--verify`の出力が一致することを確かめる。状態形式や意図した動作が変わる変更では、スクラッチのコピーで指紋から形式番号と変わったブロックを除いて比べ、変わった理由を残す。
3. 交互に測る。基準（前のコミットを`git archive`などで別のディレクトリーへ取り出し、Gitの対象外のdmg-acid2もそろえる）と候補を用意し、基準→候補→候補→基準の順に、`--artifacts`で出力先を分けて同じ時間帯に測る。参考値や別の時刻の値とは比べない。同じDLLを1つのプロセスへ2つ読み込んで交互に測る方法は、コードの配置で片方が一貫して遅く出たため使わない。
4. 揺れを見込んで読む。外乱は時間を増やす向きにしか働かないため、PCが静かでなければ各ツリーの最も速い回どうしで比べる。記録を取った機械では順序とコードの配置だけで約±5%動いたので、1組だけの5%前後の差は結論にしない。逆の順（候補→基準→基準→候補）で測り直し、ホットパスに触れないプラセボのビルド（使われないクラスと到達しない分岐だけを足したもの）とも比べる。BenchmarkDotNetの警告（Iterationが100 ms未満、山が複数ある分布、プロセス間の差）を残し、少ない標本で小さな改善を断定しない。
5. BusとStateは同じコードでも回ごとの差が大きい。BenchmarkDotNetの1回の結果で判断せず、必要なら同じ処理を多数回（例：2,000回×7組の最速）直接比べる。
6. 内訳を調べる。`--profiler EP`（BenchmarkDotNetに同梱のEventPipeのCPUサンプリング）でプロファイルを取る。インライン化された処理はサンプリングで分けられないため、別のツリーで対象を`NoInlining`にして割合の目安を取るか（呼び出しの費用が加わる）、`DOTNET_JitDisasm`でTier1のコードを読む。
7. 採否を決める。同じ結果と再現する改善があるときだけ採用する。差が揺れの範囲なら、実装の単純さとコードの小ささを優先して保留できる。速度と確保量の一方だけが良いときはトレードオフを明記する。ハードウェアの精度や機能の追加による費用と、同じ機能の退行を区別する。速度の絶対の閾値を通常のテストへ入れない。
8. 記録する。対象のコミットと環境、ワークロード・入力・ハッシュ、前後の平均とばらつき、確保量、コードサイズ、等価性の検証、採用・保留の理由を短く残す。NA・失敗・条件の違う計測を成功の結果に混ぜない。
