# 音声確認ROM

左右の出力と4チャネルを確認する、自作の32 KiB DMG ROM Onlyです。[MITライセンス](LICENSE)で利用できます。WPFの「Open ROM」で`sound-check.gb`を選ぶか、コマンドラインで渡して起動します（`Example.GameBoy.WpfHost.exe Roms/SoundCheck/sound-check.gb`）。

0.5秒ずつ、次の4つを繰り返します（2秒で一巡）。画面は使わず、LCDはOFFのままです。入力ボタンも使いません。

| 時間 | 音 | 出力先 |
| --- | --- | --- |
| 0〜0.5秒 | CH1 矩形波（duty 50%、周期値6D6＝約440 Hz、音量12） | 左だけ |
| 0.5〜1秒 | CH2 矩形波（duty 25%、周期値739＝約659 Hz、音量12） | 右だけ |
| 1〜1.5秒 | CH3 三角波（周期値6D6＝約220 Hz、出力100%） | 両方 |
| 1.5〜2秒 | CH4 ノイズ（15 bit、512 Tごと、音量10） | 両方 |

区切りはTimer割り込み（4,096 Hzを64回で64 Hz）を32回数えて作ります。NR50は左右とも最大（77）です。

`./Roms/SoundCheck/Build.ps1`でSM83命令を出力します。外部アセンブラー不要。SHA-256: `6af87bfdf71bdf121d3ee24c5213df7873cd20bfc9101e9311728e0f7c7798c0`。

任天堂ロゴは含みません。ローダーのロゴ警告は意図したもので、Boot ROMを省略するエミュレーター用です。実機での動作は確認していません。

Core.Testsの`SoundCheckTests`で、各区間の左右（片側は完全に0、両側は左右一致）をPCMから確かめます。Platform.Testsの`RunnerAudioTests`では実行スレッドが受け渡すPCMを確かめます。実際の出力（WASAPI・AudioTrack）の左右・音量・ミュート・連続再生は、このROMを鳴らして確かめます（[互換性と検証](../../docs/Compatibility.md)）。
