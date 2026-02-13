# Wind - Windows Tab Manager

デスクトップ上の複数ウィンドウをブラウザのタブのように一つの画面にまとめて管理できる Windows アプリケーションです。

## できること

### タブでウィンドウを管理
- 起動中の任意のアプリケーションウィンドウをタブとして取り込める
- タブのクリックやキーボードショートカットで瞬時に切り替え
- 不要になったタブを閉じるか、元のウィンドウとして解放できる

### タイル表示
- 複数のウィンドウをグリッド状に並べて同時に表示できる
- 画面分割で複数アプリを見比べながら作業可能

### コマンドパレット
- `Ctrl + Shift + P` でコマンドパレットを起動
- キーワード検索でコマンドをすばやく実行

### クイック起動
- よく使うアプリケーションをショートカットとして登録
- ウィンドウ選択画面からワンクリックで起動＆タブ化

### スタートアップ連携
- Wind 起動時に自動で開くアプリケーションを設定可能
- Windows 起動時に Wind 自体を自動起動する設定にも対応

### キーボードショートカット
| ショートカット | 動作 |
|---|---|
| `Ctrl + Tab` | 次のタブに切り替え |
| `Ctrl + Shift + Tab` | 前のタブに切り替え |
| `Ctrl + 1-9` | 指定番号のタブに直接移動 |
| `Ctrl + W` | 現在のタブを閉じる |
| `Alt + P` | コマンドパレットを開く |

ショートカットは設定画面からカスタマイズ可能です。

### 設定・カスタマイズ
- テーマ切替 (ライト / ダーク)
- タブヘッダーの表示位置 (上部 / サイド)
- 終了時の動作 (アプリを閉じる / ウィンドウを解放 / Wind を終了)

## 動作環境

- Windows 10 / 11
- .NET 8.0 Runtime

## ビルド・実行

```bash
# ビルド
dotnet build src/Wind/Wind.csproj

# 実行
dotnet run --project src/Wind
```

または、ビルド済み実行ファイルを直接起動:
```bash
src\Wind\bin\Debug\net8.0-windows\Wind.exe
```

## 使い方

1. Wind を起動する
2. 「+ Add Window」ボタンをクリック
3. 一覧から取り込みたいウィンドウを選択
4. ウィンドウがタブとして取り込まれる
5. タブをクリックまたはショートカットキーで切り替え

## アーキテクチャ

```
Wind/
├── Interop/          # Win32 API 連携 (ウィンドウ埋め込み・ホットキー)
│   ├── NativeMethods.cs
│   ├── WindowHost.cs
│   └── WindowResizeHelper.cs
├── Models/           # データモデル
│   ├── TabItem.cs
│   ├── TabGroup.cs
│   ├── WindowInfo.cs
│   ├── TileLayout.cs
│   ├── HotkeyBinding.cs
│   ├── AppSettings.cs
│   └── CommandPaletteItem.cs
├── Services/         # ビジネスロジック
│   ├── WindowManager.cs
│   ├── TabManager.cs
│   ├── HotkeyManager.cs
│   ├── SettingsManager.cs
│   └── DragDropService.cs
├── ViewModels/       # MVVM ViewModel
│   ├── MainViewModel.cs
│   ├── WindowPickerViewModel.cs
│   ├── SettingsViewModel.cs
│   └── CommandPaletteViewModel.cs
├── Views/            # UI コンポーネント
│   ├── TabBar.xaml
│   ├── WindowPicker.xaml
│   ├── SettingsPage.xaml
│   └── CommandPalette.xaml
└── Converters/       # 値コンバーター
```

## 技術スタック

- .NET 8.0 + WPF
- WPF-UI 3.0.4 (Fluent Design / Mica バックドロップ)
- CommunityToolkit.Mvvm (MVVM フレームワーク)
- Microsoft.Extensions.DependencyInjection

## 既知の制限事項

- 管理者権限で実行中のプロセスのウィンドウは取り込めない場合がある
- UWP アプリケーションのサポートは限定的
- 一部のアプリケーションは取り込み後に機能が制限される場合がある
- Chromium 系ブラウザ (Chrome, Edge) はイベント検知に特殊な処理が必要
- Explorerの表示が安定しない
- 単一インスタンスアプリ (Excel 等) を埋め込むと、タスクバーから同アプリを新規起動できない (詳細は下記)

### Excel 等の単一インスタンスアプリの埋め込みに関する制限

Excel 2013 以降は 1 プロセスで複数ウィンドウを管理する SDI 方式を採用している。Wind に Excel を埋め込んだ状態でタスクバーから Excel を起動すると、Windows は新しいプロセスを作らず DDE メッセージで既存プロセスに「新しいブックを開け」と指示する。この指示は Wind に埋め込まれた Excel プロセスが受け取るため、新しいウィンドウが正常に表示されない。

**回避策:**

- `excel.exe /x` で起動すると完全に独立したインスタンスになり、タスクバーからの通常起動と干渉しなくなる
- Word / PowerPoint 等の他の Office 製品でも同様の問題が発生しうるため、同じく独立インスタンスオプションの利用を推奨する

## ライセンス

MIT License
