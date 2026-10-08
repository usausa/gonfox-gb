# Background / input demo

CPU・PPU・Joypadの統合を確認する、自作の32 KiB DMG ROM Onlyです。[MITライセンス](LICENSE)で利用できます。WPFの「Run demo」から起動するか、`background-demo.gb`を開いてください。

方向キーでスクロール、Start保持で自動移動停止、A保持で濃淡反転、B保持で背景非表示、Selectで原点へ戻ります。背景の初期化後はVBlank割り込みとHALTで1フレームずつ更新します。

`./Roms/BackgroundDemo/Build.ps1`でSM83命令とタイルを出力します。外部アセンブラー不要。SHA-256: `9053de305c39aed9780a74057e1fc44e99d9cc8ac3a62b5eb7d4ccdd82e9c0f8`。

任天堂ロゴは含みません。ローダーのロゴ警告は意図したもので、Boot ROMを省略するエミュレーター用です。実機のBoot ROMによる起動は検証していません。

## MBC1・セーブ付きの版

`./Roms/BackgroundDemo/Build.ps1 -Mbc1`で`mbc1-demo.gb`を生成します。同じMITライセンスで、ROM 64 KiB、タイプ03、電池RAM 32 KiBです。SHA-256: `2bdc7882666d81731b8a9936670049e8a4e1b8feee07f1f73ffa27f12bb8c812`。引数なしで生成するROM Only版のバイト列は変更していません。

WPFの「Open ROM」で選びます。操作と画面はROM Only版と同じで、毎フレームの入力処理後にスクロールX/YをA000/A001へ書き、次回起動時に復元します。通常は自動スクロールするため、保存位置を確認するときは一時停止してSCX/SCYを見てください。上下入力で変化するSCYは自動では変わりません。

「Save RAM」、ROM切り替え、通常終了のほか、定期保存（RAMが5秒変わらなければ。自動スクロール中のように変わり続けても60秒ごと）でホストがファイルへ保存します。これはROM内のRAMへの書き込みとは別です。Resetでも位置を保持します。全RAMバンクの切り替えはMooneyeとMbc1Testsで検証し、このデモは描画・入力・保存の統合確認に使います。

Core.Testsの`DeterministicReplayTests`で両版の入力時刻・実消費時間・全メモリー・画像を比較し、MBC1版では入力で変更した位置のExport/Importと再起動後の画像も検証します。`StateRomTests`では走査途中からの復元も比較します。[互換性と検証](../../docs/Compatibility.md)を参照してください。
