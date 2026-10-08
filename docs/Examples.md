# サンプルの使い方

ライブラリーを使うホストのサンプルが2つあります。WindowsのWPFの`Example.GameBoy.WpfHost`と、Android（MAUI）の`Example.GameBoy.MauiHost`です。どちらもGonFox.GameBoy.CoreとGonFox.GameBoy.Platform.Sharedを使い、プラットフォームごとの部分（音声の出力・置き場所など）はGonFox.GameBoy.Platform.WindowsとGonFox.GameBoy.Platform.Androidにあります。ブロックの構成と契約は[現行仕様](Specification.md)を参照してください。

## WPF版（Windows）

Windowsと.NET 10 SDKで動きます。

```powershell
dotnet run --project Example.GameBoy.WpfHost -c Release
```

### 操作

「Open ROM」、またはデモの一覧（Background demo・Mega demo・Sound check・RPG demo）で選んで「Run demo」で開始します。対応ROMはROM Only（32 KiB）、標準MBC1（最大2 MiB）とMBC1M（1 MiBのマルチカート）、MBC2（最大256 KiB、内蔵RAM 512×4 bit）、MMM01（最大8 MiBのマルチカート、RAM最大128 KiB）、MBC3（最大2 MiB、MBC30は4 MiB。RAM最大64 KiB、時計付きを含む）、MBC5（最大8 MiB、RAM最大128 KiB。振動付きはRAM最大64 KiBで、モーターが動いている間は状態の行に「Rumble」と出します）、ポケットカメラ（最大1 MiB、RAM最大128 KiB）、HuC1（最大1 MiB、RAM最大32 KiB）、HuC3（最大2 MiB、RAM最大32 KiB、時計のMCU）で、MBC1・MBC2・MMM01・MBC3・MBC5・カメラ・HuC1・HuC3は電池RAM（MBC3とHuC3は時計も）にも対応します。カメラのセンサーには、このWPFのサンプルでは画像を渡さないため一様な灰色が写ります（ホストが画像を渡すAPIはあります）。HuC1・HuC3の赤外線は相手の機器につながりません。「+RAM」の種別でRAMサイズが0のROMは、RAMなしとして警告付きで読み込みます。容量の組み合わせは[現行仕様](Specification.md)を参照してください。

| 操作 | 動作 |
| --- | --- |
| Pause / Resume | モデルの時間を停止 / 続行 |
| Reset | 起動状態へ戻る（Boot ROMを選んでいれば電源投入の状態からBoot ROMを実行）。実行/一時停止を維持し、カートリッジRAMと状態保存枠は保持 |
| Choose boot ROM / No boot ROM | 256 bytesのDMGのBoot ROMファイルを選ぶと、次のROMの読み込みかリセットから電源投入の状態で実行します（実行中は状態の行に「Boot ROM」）。「No boot ROM」で既定の起動後の状態に戻します。選んだファイルは次の起動でも使います |
| Step（Debug） | 停止中に1命令・割り込み受理・HALT待機のいずれかを実行（CPUが止まっているときは時間だけ4 T進む）。STOPは新しい入力まで進まない |
| Save RAM | 電池RAM（MBC3は時計も）をファイルへ保存。成功時だけ元の実行状態へ戻る |
| Save state / Load state | CPU・RAM・周辺回路・画像を、一覧で選んだ枠（Slot 1〜9）へファイルとして保存 / その枠から復元。両操作の後は一時停止。保存した枠は次の起動でも使える |
| Rewind | 実行中に約1秒ごとに記録した状態（最大30秒分）から、約1秒前へ戻して一時停止。続けて押すとさらに戻る |
| Read（DebugのMemory） | Startを16進数、Lengthを10進数の1〜256 bytesで指定。取得時T-cycle/PC付きの読み取り専用コピー |
| Mute / 音量 | 既定の出力デバイスへの音を消す / 0〜100%で調整（初期70%）。エミュレーションの進み方は変わらない |
| Retry audio | 出力デバイスを開けない・切断されたときに有効。無音で続けている間に開き直す |
| Renderer（Display） | 画面の描画方式を選ぶ。SkiaSharp（既定。CPUで拡大）か、WriteableBitmap（160×144の画像をWPFが拡大し、大きな窓でもCPUが少ない）。表示される画像は同じで、選択は次の起動でも使う |
| Debug（右の欄の下） | レジスター・PPU・周辺回路・時間・音声のキューの行、1命令、メモリーの取得を開く。閉じている間（既定）は行を整形しない |

方向キー、A=Z/J、B=X/K、Select=Back/Shift、Start=Enterに対応します。画面上の8ボタンはマウス・タッチの押下中だけ有効で、UI Automationの「Invoke」（支援技術・自動操作）では0.1秒押します。入力元を合成し、フォーカス喪失で全解除します。Memoryの欄の編集中は文字編集を優先します。

音は実行中だけ鳴り、一時停止・STOP・1命令・状態操作の後は、再開した地点から約40 msためて鳴らし始めます。ほかの処理などで実行が一時的に止まって音が途切れると、ためる量を20 msずつ（最大120 ms）増やし、途切れずに30秒続くごとに10 msずつ40 msへ戻します。DebugのAudioの行に遅延の目安（通常60〜70 ms）、速度補正、不足・破棄の回数を表示します。ROMのパスをコマンドライン（またはexeへのドラッグ＆ドロップ）で渡すと、起動時にそのROMを読み込みます。

FPSはPPUが完成した画像の実時間あたりの枚数で、通常約59.7、一時停止・STOPでは0です。画面の再描画回数とは異なり、WPFの描画周期が揺れると、間の画像は表示前に新しい画像へ置き換わります。画面は枠に収まる最大の整数倍（物理ピクセル）の大きさで、最近傍で拡大します。一時停止・STOP・最小化の間は毎フレームの描画をせず、元に戻すと最新の画像を表示します。状態欄は約100 msごとに、値が変わった行だけを書き換えます。

### 保存とエラー

音量・ミュート・ウィンドウの位置と大きさ（最大化を含む）・描画方式・Boot ROMのファイルのパスは通常終了時に `%LOCALAPPDATA%/GonFox.GameBoy/settings.json` へ保存し、次の起動で戻します。ファイルが壊れている・値が範囲外の場合は既定値に戻し、どの画面にも入らない位置（外したモニターなど）では既定の位置と大きさで開きます。Boot ROMのファイルが読めない・256 bytesでない場合は、Boot ROMの行に理由を出してBoot ROMなしで始めます。

電池RAMは「Save RAM」、ROM切り替え、通常終了時に保存し、次回のROM読み込み時に復元します。実行中も5秒ごとにRAMを確かめ、変わった内容が5秒以上そのままなら（変わり続けるときも1分以内に）実行を止めずに保存します（定期保存）。保存先は `%LOCALAPPDATA%/GonFox.GameBoy/Saves/<ROM全体のSHA-256>.sav`。同じ内容ならファイル名を変えても同じセーブです。保存のたびに1つ前の世代を`.sav.bak`に、内容のハッシュを`.sav.sha256`に残し、次の読み込みで食い違えば（外部での置き換えや破損）保存の行に注記（changed outside）を出したうえで読み込みます。同じROMを別のウィンドウ（アプリ）で開こうとすると、セーブを守るため読み込みを断ります。

MBC3の時計付きのROMは、時計の値と保存時刻をRAMの後に記録し（VBA-M・BGBと同じ形式）、次に開くときに閉じていた間の時間だけ時計を進めます。HuC3は時計のMCUのメモリーと時刻をmGBAと同じ形式でRAMの後に記録し（SameBoyの形式も読めます）、同じく閉じていた時間を足します。実行中の時計はエミュレーションの時間で進み、一時停止中は止まります。

保存失敗ではRAMを保持して一時停止し、ROM切り替え・終了を中止します（定期保存の失敗は保存の行に表示して実行を続け、次の確認で再試行します）。保存先のロック・権限等を解消して再試行してください。不正サイズの既存ファイルはバックアップして確認してください。強制終了では最後の保存（定期保存を含む）より後の変化を失います。

実行状態はROMごとに9枠まで `%LOCALAPPDATA%/GonFox.GameBoy/States/<ROM全体のSHA-256>.slot<N>.state` に保存し、次の起動でも復元できます。状態ファイルは、このアプリの同じ版（保存状態の形式とその並び）だけが読み込めます。新しい版で形式が変わると古い状態ファイルは読み込めない（理由を表示し、ファイルは消しません）ため、長く残す進行は電池セーブを使ってください。壊れた・別のROMの・読めない形式の状態ファイルでは、実行中の状態を変えません。巻き戻しの記録はアプリの中だけにあり、ROMの切り替え・リセット・枠からの復元・終了で消えます。復元直後は保存時の入力を保持し、再開時に現在のキーへ同期します。**状態復元・巻き戻しだけでは `.sav` を変更しませんが、その後のRAM保存・定期保存・ROM切り替え・通常終了では戻した後のRAMを保存します。**

未対応カートリッジと、対象外のCGB専用ROMは読み込みエラーになります。未定義命令では実機と同じくCPUだけが止まり、画面・音・タイマーは進み続けます（状態の行に「CPU locked」、メッセージ欄に止まった位置と命令を表示。リセット・状態復元・巻き戻しで戻れます）。出力デバイスがない・使えない場合は「Audio unavailable」と理由を表示し、無音のまま実行を続けます。同梱の自作ROMはNintendoロゴを含めず、ロゴ警告は意図したものです。

## Android版（MAUI）

`Example.GameBoy.MauiHost`はAndroid 8.0（API 26）以上のスマートフォン向けのサンプルです（縦向き）。CoreとGonFox.GameBoy.Platform.Sharedは変えずに使い、Android向けの部分（AudioTrackの音、アプリの置き場所、触覚と振動）は`GonFox.GameBoy.Platform.Android`にあります。.NET MAUI 10.0.20（`maui-android`のワークロード）、JDK、Android SDK（API 36）が必要です。このプロジェクトもGonFox.GameBoy.slnxに入っているため、ソリューション全体のビルドにはこのワークロードが要ります。

```powershell
dotnet workload install maui-android
dotnet build Example.GameBoy.MauiHost -c Release
adb install -r Example.GameBoy.MauiHost/bin/Release/net10.0-android/com.gonfox.gameboy-Signed.apk
```

JDKとAndroid SDKが既定の場所にないときは、`-p:JavaSdkDirectory=<JDK>`と`-p:AndroidSdkDirectory=<SDK>`を付けます。Release版は全メソッドをLLVMで事前コンパイルするため、ビルドに数分かかります（実行スレッドの負荷が既定の方式の約半分になります。Debugは既定のままです）。APKは開発用の鍵で署名されます（配布用の署名は扱いません）。

画面の上から、ROMの名前と状態、画面、コマンド（ROM・Demo・Pause・Reset・State・Settings）、メッセージ、画面のボタンが並びます。ボタンは複数の指で同時に押せ（十字キーの斜めは2方向）、押している間だけ有効です。押すと端末の「タップの振動」の設定に従って小さく振動します。「ROM」はAndroidのファイルの選択で開き（8 MiBまで）、「Demo」は4つの組み込みデモから選びます。「State」は枠1への保存（Save）・復元（Load）と巻き戻し（Rewind）、「Settings」はボタンの代わりに、Mute・Volume（初期70%）・Button haptics・Rumble（振動カートリッジで端末を振動させるか）・Choose boot ROM / Remove boot ROMを出します。端末の音量ボタンでも音量が変わります。

電池セーブ・状態の枠はアプリの内部の領域にWindowsと同じ形式で保存し、5秒ごとの定期保存もします。最後に開いたROMと選んだBoot ROMはアプリの中にコピーし、次の起動で最後のROMを開き直します（電源投入の状態から、電池RAM付き）。ホームへ移る・画面を消す・ファイルを選ぶ間は一時停止して電池RAMと実行状態を保存し、戻ると実行中だったときだけ再開します。Androidが背景のアプリを終えた後に戻ったときは、保存した実行状態から続けます。設定は`Preferences`に保存します。Androidの自動バックアップは使いません（ROMのコピーを外へ送らないため）。

確認や自動操作では、adbの起動の値で開くものを指定できます（`adb shell am start -n com.gonfox.gameboy/<アクティビティ> --ei demo 1`、`--es rom <ファイル>`、`--es bootrom <ファイル>|none`、`--ez muted true`、`--ei volume 70`。ファイルは端末の`Android/data/com.gonfox.gameboy/files`に`adb push`で置けます）。動作の記録はlogcatのタグ`GonFox.GameBoy`に出ます。詳しくは[現行仕様](Specification.md)を参照してください。

## 無料で試すROM

| ROM | 用途・起動 |
| --- | --- |
| [メガデモ（音楽付き）](../Roms/MegaDemo/README.md) | デモの一覧で選ぶか`Roms/MegaDemo/megademo.gb`を開く。波・プラズマ・軌道の3演出と4チャネルの曲。Selectで演出と音色、左右で速度とテンポ、上下で量と効果音の音程、Aで模様とメロディーの音色、Bで方向、Startで演出と曲の停止/再開（ROM自身の消音で、アプリのミュート・音量とは別）。下端に値を表示 |
| [RPGデモ（USAGI QUEST）](../Roms/RpgDemo/README.md) | デモの一覧で選ぶか`Roms/RpgDemo/rpgdemo.gb`を開く。背景がスクロールしてロゴが落ちるタイトル、勇者8人と難易度3段の選択、コマンド式の戦闘、全ボタンのKEY TESTで、各画面にBGM。タイトルはStart/Aで進めてメニュー、選択は左右・Selectで勇者・上下で難易度・Aで開始・Bで戻る、戦闘は十字キーとAでコマンド・Selectで交代・Startで一時停止、KEY TESTはStart+Selectで戻る |
| [背景デモ](../Roms/BackgroundDemo/README.md) | デモの一覧の既定（「Run demo」で起動）。方向でスクロール、Start保持で自動移動停止、Aで反転、Bで背景非表示、Selectで原点 |
| [MBC1版背景デモ](../Roms/BackgroundDemo/README.md) | `Roms/BackgroundDemo/mbc1-demo.gb`を開く。同じ操作でスクロール位置を電池RAMへ保存 |
| [音声確認ROM](../Roms/SoundCheck/README.md) | デモの一覧で選ぶか`Roms/SoundCheck/sound-check.gb`を開く。左だけ・右だけ・両方（三角波）・両方（ノイズ）を0.5秒ずつ繰り返す |
| [セーブカウンター](../Roms/SaveCounter/README.md) | `Roms/SaveCounter/save-counter.gb`を開く。起動/ResetごとのカウントをSCXと濃淡で確認 |
| dmg-acid2 | `Roms/External/dmg-acid2/dmg-acid2.gb`を開く。公式のDMG描画テスト |
| rtc3test | `Roms/External/rtc3test/rtc3test.gb`を開く。MBC3の時計のテスト（上下で選びAで実行。全項目が合格なら結果が黒、不合格は灰色） |
| mealybug-tearoom-tests | `Roms/External/mealybug/`の`.gb`を開く。描画中のレジスタの書き込みが効く位置を画像で見るテスト。期待画像は`Roms/External/mealybug/expected/DMG-blob`（Boot ROMが残すロゴと®が写る期待画像もあり、このアプリの起動状態では出ない） |

自作ROMの生成ソース・MIT許諾表示を同梱します。メガデモ・RPGデモの再生成には固定版のRGBDS、参照画像の再生成とメガデモの音の比較（`Roms/MegaDemo/Compare-Audio.ps1`）にはbinjgbを使いますが、通常のビルド・テストには不要です。公開テストROM（Mooneye・gbmicrotest・dmg-acid2・rtc3test・mealybug-tearoom-tests、game-boy-test-romsのAGE・firstwhite・SameSuite）とSameBoyのBoot ROMは、それぞれの許諾表示とともに`Roms/External`に含めています。固定版、成功条件、結果と制約は[互換性と検証](Compatibility.md)を参照してください。
