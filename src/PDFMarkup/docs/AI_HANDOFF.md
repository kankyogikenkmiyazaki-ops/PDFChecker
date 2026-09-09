# PDFMarkup 開発引き継ぎ — Ver1.51 修正・改善

最終更新：2026-09-09  
対象：PDFMarkup Ver1.5 → Ver1.51  
状態：**Ver1.5公開済み／Ver1.51 修正・改善項目整理開始**  
目的：別チャット・別AI・別PCでも、PDFMarkupの「現在の仕様・実装状態・PDF注釈設計・確認済み方針・次に触る場所」を取り違えず再開できるようにする。

---

# 1. このファイルの位置づけ

`AI_HANDOFF.md` は作業日記や全変更履歴ではない。

目的は、

> **現在のPDFMarkupを正確に把握し、次の修正・改善を安全に再開できること**

である。

このファイルには、現在のProjectを理解するために必要な、

- 現行Version
- 実装済み機能
- Project構成
- 主要Model / Service
- PDF注釈の保存方式
- 座標変換の現在仕様
- 複数ウィンドウの現在仕様
- ローカル設定
- 現在も有効な注意事項
- Ver1.51で改善する候補
- 次に触る場所

をまとめる。

## 1.1 HANDOFF更新ルール

原則：

1. 現在も有効な情報は維持する。
2. 仕様が変わった場合は、古い仕様を現行仕様として残さず書き換える。
3. 新規Model / Service / 保存項目を追加した場合は現在構成へ追加する。
4. 削除・移動・名前変更した場合は現在のProjectへ合わせる。
5. 完了した「次回作業」は、実装済み情報へ置き換える。
6. 解消した一時的な注意事項は削除してよい。
7. 過去チャットだけを根拠に存在しない機能を補完しない。
8. 実Project・実コード・実PDF・Git状態を優先する。
9. `bin/`、`obj/` 等の生成物は原則Project構成へ記載しない。
10. コードを作っただけで完成扱いにせず、User側でbuild・実画面・保存PDF・再読込・印刷を確認する。

> **AI_HANDOFF.mdは「過去の会話まとめ」ではなく、現在のPDFMarkupを再開するための現場図面。**

## 1.2 情報確認の優先順位

1. 現在ビルド対象になっているGit管理下の実ファイル
2. VS Code Explorer
3. 実画面
4. 実際に保存したPDF
5. Acrobat等で開いた保存後PDF
6. Git状態
7. この `AI_HANDOFF.md`
8. 過去Versionの資料
9. 過去チャット

状態表記：

```text
【確認済み】
User側の実動作・保存・再読込・配布等で確認済み

【コード確認済み】
アップロードされた現行コードに実装が存在することを確認

【方針決定】
次Versionで採用する方向を決定済み

【未実装】
現行コードにはまだ存在しない

【要検証】
実装前または実PDFでの互換性確認が必要

【将来候補】
Ver1.51以降でもよい
```

---

# 2. Version状態

## Ver1.5

```text
状態：公開済み
Project上のVersion：1.5.0
```

`PDFMarkup.csproj`：

```xml
<Version>1.5.0</Version>
<AssemblyVersion>1.5.0.0</AssemblyVersion>
<FileVersion>1.5.0.0</FileVersion>
```

Ver1.5は無料公開済み。

現在アップロードされたSourceもVer1.5を基準としている。

【コード確認済み】

- PDF表示
- 複数ページ
- ページサムネイル
- フリーハンド朱書き
- チェック描画
- Shift直線
- Ctrl+Shift矢印
- 消しゴム
- 選択
- 文字注釈
- Undo / Redo
- PDF注釈保存・再読込
- 上書き保存・別名保存
- 朱書き / チェック表示切替
- 描画色・太さ・透明度
- 口径・コメント
- 最近使ったPDF
- 複数PDFを別ウィンドウで開く
- ウィンドウ位置・サイズ保存
- 左右パネル状態保存
- PDF座標 / Rotate調査用Debug表示

## Ver1.51

```text
状態：修正・改善項目整理開始
位置づけ：Ver1.5を実際に使った結果と利用者要望を反映する改善版
```

Ver1.51では大規模な再設計より、

> **実利用で出た不便を小さな単位で改善する**

ことを優先する。

2026-09-09時点で、外部利用者から具体的な改善要望が1件届いている。

主な要望：

```text
1. 現在よりさらに拡大したい
2. Shift直線で45°近辺に吸着せず、微妙な角度でも直線を引きたい
```

この要望をVer1.51改善項目へ含める。

---

# 3. 開発環境

```text
Language      C#
GUI           WPF
Framework     .NET 10 / net10.0-windows
IDE           VS Code
OS            Windows
App Name      PDFMarkup
```

主要Package：

```text
PDFium.WindowsV2  1.1.4
PDFiumSharpV2     1.1.4
PDFsharp          6.2.4
```

役割：

```text
PDFiumSharp
→ PDFページ表示・ページサイズ取得・サムネイル描画

PDFsharp
→ PDF辞書情報取得
→ /MediaBox / CropBox / Rotate確認
→ Ink / FreeText注釈保存・読込

WPF Canvas
→ 画面上の朱書き・チェック・選択・文字配置
```

注意：

このHANDOFF作成環境には `dotnet` コマンドが入っていなかったため、アップロードされたSourceのbuildは今回再実行できていない。
User側ではVer1.5公開済みだが、Ver1.51作業開始時は改めて現在Repositoryでbuild確認する。

---

# 4. Git基準

今回渡されたZIPには `.git` が含まれていないため、現在のcommitはこのファイルから決め打ちしない。

Ver1.51再開時に必ず実Repositoryで：

```powershell
git status
git branch --show-current
git log -1 --oneline --decorate
dotnet build PDFMarkup.csproj
```

を確認する。

Gitの実状態をHANDOFFや過去チャットから推測しない。

---

# 5. Repository主要構成

現在アップロードされた主要Source：

```text
PDFMarkup/
│
├─ App.xaml
├─ App.xaml.cs
├─ AssemblyInfo.cs
│
├─ Assets/
│  └─ PDFMarkup.ico
│
├─ Models/
│  ├─ PageInfo_debug.cs
│  ├─ StrokeModel.cs
│  └─ TextAnnotationModel.cs
│
├─ Services/
│  ├─ DrawingService.cs
│  ├─ PdfService.cs
│  ├─ SettingsService.cs
│  └─ TextService.cs
│
├─ MainWindow.xaml
├─ MainWindow.xaml.cs
└─ PDFMarkup.csproj
```

現状 `MainWindow.xaml.cs` は約6,500行あり、主要UIロジックの大部分が集中している。

Ver1.51で複数選択・ウィンドウ同期などを追加するとさらに大きくなるため、必要なら機能単位のpartial class / Service分離を検討する。
ただし、分割自体を目的に大改修しない。

---

# 6. 主要Model

## 6.1 StrokeModel

1本の線・フリーハンド・直線・矢印を保持する基本Model。

主項目：

```text
PdfPoints
Thickness
Color
Opacity
Mode
Diameter
Comment
SelectionBounds
```

現在は、

> **1回の描画ストローク = 1 StrokeModel**

として扱う。

`SelectionBounds` は描画点から再計算し、細い線でも選択しやすいようpaddingを持つ。

## 6.2 DrawingMode

```text
Markup  → 朱書き
Check   → チェック
```

## 6.3 StrokeColor

現行：

```text
朱書き3色
- Red
- Blue
- Green

チェック10色
- Yellow
- Orange
- Pink
- LightBlue
- LightGreen
- Purple
- Brown
- Gray
- Cyan
- Magenta
```

Ver1.51候補：

> **チェック色を最低18色程度まで増やし、実PDF上で見分けやすい色へ調整する。**

## 6.4 TextAnnotationModel

PDF上へ配置する文字注釈。

主項目：

```text
Text
PdfPosition
Color
Opacity
FontSize
Mode
Diameter
Comment
```

文字はPDF保存時に `/FreeText` 注釈として保存する。

## 6.5 PageInfo

座標ずれ調査用。

保持：

```text
PDFium Width / Height
Rotate
MediaBox
CropBox
```

現在のA3 / Rotate問題調査で使用したDebug Model。

---

# 7. 主要Service

## DrawingService

担当：

```text
45°近辺への8方向スナップ
Canvas → PDF座標変換
PDF → Canvas座標変換
Canvas範囲判定
矢印サイズ計算
矢羽根座標計算
```

現在の角度スナップ：

```text
基準：0 / 45 / 90 / 135 / 180 / 225 / 270 / 315°
許容角度：±7.5°
```

したがって現在のShift直線は、

> **すべて45°単位になるわけではなく、45°系の方向から±7.5°以内の場合だけ吸着する。**

例：40°は45°との差が5°なので45°へ吸着する。

## PdfService

担当：

```text
ページ数取得
ページサイズ取得
PageInfo取得
サムネイル描画
PDFページ描画
Ink注釈保存・読込
FreeText注釈保存・読込
PDF注釈Debug表示
```

PDF表示はPDFium、注釈保存はPDFsharp。

## TextService

担当：

```text
文字入力開始・確定・キャンセル
文字注釈選択
文字色変更
口径変更
コメント編集
透明度変更
文字サイズ変更
文字注釈描画
文字注釈削除
ページ別文字注釈保持
```

## SettingsService

保存先：

```text
%APPDATA%\PDFMarkup\settings.json
```

現在保存：

```text
最近使ったPDF 最大5件
MainWindow位置・サイズ
最大化状態
左パネル開閉・幅
右パネル開閉・幅
```

Ver1.51で角度設定・描画設定同期を実装する場合、保存項目追加候補。

---

# 8. 現在の画面構成

```text
上部
├─ Menu
└─ ToolBar

左
└─ ページ一覧 / サムネイル

中央
└─ PDF表示 + DrawingCanvas

右
├─ 描画設定
│  ├─ モード
│  ├─ 色
│  ├─ 太さ
│  └─ 透明度
└─ 注釈情報
   ├─ 口径
   └─ コメント

下部 StatusBar
├─ 現在PDFパス
├─ 保存状態
├─ ページ
└─ 倍率
```

左右パネルは開閉・幅変更可能。

---

# 9. 現在のファイル操作

【コード確認済み】

```text
Ctrl+O
→ PDFを開く

Ctrl+S
→ 保存

Ctrl+Shift+S
→ 名前を付けて保存
```

保存前に未保存変更がある場合、終了・ファイル切替時に確認する。

上書き保存時：

```text
元PDF
 ↓
同一フォルダへ一時PDF保存
 ↓
保存成功
 ↓
File.Replaceで元PDFと置換
```

保存途中に失敗した場合、元PDFを残す方向。

---

# 10. 複数PDF / 複数ウィンドウ — 現在仕様

【コード確認済み】

PDFを開く時：

```text
同じPDFがすでに開いている
→ そのMainWindowを前面へ

現在Windowが空
→ 現在Windowで開く

現在Windowで別PDFを表示中
→ 新しいMainWindowを作成して開く
```

したがって、2つのPDFを並べて確認すること自体は現状可能。

ただし、各 `MainWindow` はそれぞれ：

```text
_currentDrawingMode
_currentStrokeColor
_currentStrokeThickness
_currentStrokeOpacity
_lastMarkup...
_lastCheck...
```

を個別に持つため、

> **描画設定はウィンドウ間で同期しない。**

Ver1.51候補：Section 22参照。

---

# 11. 最近使ったPDF

【コード確認済み】

PDF未選択時のStartPanelに最近使ったPDFを最大5件表示する。

表示：

```text
ファイル名
フォルダパス
```

操作：

```text
クリックで開く
1件削除
履歴をすべてクリア
```

注意：

ここで表示するフォルダパスは「最近使ったPDF」一覧の補助情報。
Ver1.51で削除予定の「左下フルパス表示」とは別。

---

# 12. PDFドラッグ＆ドロップ — Ver1.51候補

【未実装】

現在 `MainWindow.xaml` に `AllowDrop` / `Drop` 等はなく、`App.xaml` も `StartupUri="MainWindow.xaml"` の通常起動。
コマンドライン引数処理もない。

Ver1.51で追加したい：

```text
エクスプローラーからPDFをMainWindowへドロップ
→ PDFを開く
```

さらに可能なら：

```text
PDFをPDFMarkup.exe / ショートカットへドロップ
→ PDFMarkup起動
→ 渡されたPDFを開く
```

目的：

> Acrobat等を既定PDFアプリのままにしても、PDFMarkupへ簡単に持ち込めるようにする。

既に別PDFを表示中のWindowへドロップした場合は、現在の複数Window方針と合わせ、別Windowで開く方向が自然。

---

# 13. 描画モード

現在：

```text
朱書き
チェック
```

各モードで最後に使用した：

```text
色
太さ
透明度
```

をMainWindow内で保持する。

初期値：

```text
朱書き
Color      Red
Thickness 1.0
Opacity   255

チェック
Color      Yellow
Thickness 8.0
Opacity   96
```

現在はMainWindowごとのメモリ状態であり、全Window共通ではない。

---

# 14. フリーハンド / Shift直線 / Ctrl+Shift矢印

## フリーハンド

通常描画。

1回MouseDownしてMouseUpするまでが1 `StrokeModel`。

## Shift

【コード確認済み】

描画途中にShiftを押すと、ストローク開始位置から現在位置までの直線Previewへ切り替える。
MouseUp時に直線として確定。

現在は確定時に `SnapToEightDirections()` を通す。

## Ctrl+Shift

【コード確認済み】

矢印。

現在は線の始点側に矢羽根を付ける。

重要：

> **Ctrl+Shiftはすでに矢印で使用しているため、Ver1.51の角度固定切替へ流用しない。**

---

# 15. 角度設定 — Ver1.51方針

外部利用者から、40°程度の直線を引こうとすると45°へ吸着するという要望が届いた。

現行コードでは45°との差が±7.5°以内の場合だけ吸着するため、40°は45°へ補正される。

【方針決定】

Shiftは直線描画として残し、角度吸着を設定可能にする方向。

イメージ：

```text
角度吸着 OFF
→ Shift = 自由角度の直線

角度吸着 ON
→ 設定した基準角度へ吸着
```

設定UIは、

```text
基準角度の候補を選べる
+
必要なら直接数値を変更できる
```

形を候補とする。

例：

```text
15°
30°
45°
90°
カスタム
```

ただし候補を増やしすぎない。

ユーザーが直接 `40` などへ変更できる形にする。

【方針決定】

> **角度設定は基本設定として保存し、次回起動後も前回値を残す。**

保存先候補：`SettingsService` の `settings.json`。

実装前に決める項目：

```text
角度入力値は「固定角度」か「刻み角度」か
吸着許容角度を現行±7.5°のまま使うか
角度吸着OFFを基本にするか
Ver1.5互換として45°吸着ONを初期値にするか
```

---

# 16. ズーム — 現在仕様と利用者要望

現在：

```text
MinimumZoomFactor = 0.25
MaximumZoomFactor = 5.0
ZoomStep          = 1.2
```

全体表示を100%としている。

PDFページ画像は `PdfService.RenderPage()` で：

```text
scale = 2.0
```

の固定倍率でBitmapを生成している。

外部利用者要望：

> 細かい箇所をクリック指定したいので、現在よりさらに拡大したい。

【Ver1.51候補】

最大Zoomを上げる。

ただし、

> **MaximumZoomFactorだけ上げると、固定2倍で生成したBitmapをさらに拡大するだけになる可能性がある。**

ため、実装時は：

```text
最大倍率
拡大時のPDF表示解像度
メモリ使用量
描画速度
クリック位置の座標精度
```

を実PDFで確認する。

単に最大値だけ変更して完成扱いにしない。

---

# 17. パン操作

【コード確認済み】

PDFをつかんで移動するHandツールあり。

ズーム時にScrollViewerのHorizontal / VerticalOffsetを使って表示位置を動かす。

拡大率を増やす場合は、パン操作が重くならないかも合わせて確認する。

---

# 18. 選択 — 現在仕様

現在：

```text
StrokeModel? _selectedStroke
```

で、線注釈は1件だけ選択する。

文字注釈も `TextService` 側で1件選択。

選択した注釈に対して：

```text
口径
コメント
色
太さ
透明度
文字サイズ（文字のみ）
```

を変更できる。

Deleteで削除、Escで選択解除。

現在、複数選択用Collectionはない。

---

# 19. 複数選択・口径一括変更 — Ver1.51候補

【未実装】

目的：

> 同じ口径の線を1本ずつ選択せず、まとめて口径変更できるようにする。

操作案：

```text
選択モード
 ↓
何もない場所からドラッグ
 ↓
矩形で複数注釈を選択
 ↓
必要ならShift+クリック
  ├─ 選択済み → 解除
  └─ 未選択   → 追加
 ↓
口径変更
 ↓
選択中すべてへ反映
```

AutoCADに近い操作感を目標とする。

囲み選択は将来：

```text
左→右
→ 完全に枠内の注釈

右→左
→ 枠へ触れた注釈
```

のWindow / Crossing方式も候補。

Ver1.51で最初から両方式が重い場合は、まず単一の矩形選択から始めてもよい。

Undo：

> **複数注釈の一括変更は、1回のUndoでまとめて戻す。**

現在の `UndoAction` は基本的に単一Stroke / Text編集を前提としているため、複数編集用の履歴構造追加が必要。

---

# 20. 消しゴム

【コード確認済み】

```text
通常ドラッグ
→ 触れたストローク部分だけ消去

Ctrlを押しながら
→ 触れたストローク全体を削除
```

部分消去は1ドラッグを1回のUndo / Redoとして扱う。

---

# 21. Undo / Redo

現在 `UndoActionType`：

```text
AddStroke
AddText
DeleteText
DeleteStroke
EraseStrokeParts
EditDiameter
EditComment
EditColor
EditThickness
EditOpacity
EditFontSize
```

ページごとにUndo / Redo Stackを保持する。

Ver1.51で複数選択・一括変更を追加する場合は、

```text
複数Strokeの変更前Snapshot
複数Strokeの変更後Snapshot
```

等を1操作として持てる構造が必要。

---

# 22. 複数ウィンドウ間の描画設定同期 — Ver1.51候補

現在、複数PDFは複数 `MainWindow` で開けるが、描画設定は別々。

【方針決定】

各Windowには現在と同じ描画設定UIを表示したまま、同期ONのWindow同士では：

```text
朱書き / チェック
色
太さ
透明度
現在使用する描画設定
```

を共通化する方向。

どれか1つのWindowで設定を変えると、同期ONのほかのWindowも同じ表示・設定へ変える。

ただし、

> **すでに描いた注釈は変更しない。これから描く設定だけ同期する。**

PDFごとに独立して残すもの：

```text
注釈データ
Undo / Redo
ページ
ズーム
パン位置
選択状態
未保存状態
```

さらに各Windowに：

```text
描画設定を同期 ON / OFF
```

を持たせる案。

OFF：

```text
そのWindowだけ独立設定
変更しても他Windowへ反映しない
```

ONへ戻す：

```text
現在の共通設定へ合わせる
```

実装方式候補：

```text
Application全体で共有するDrawingSettingsState
+
各MainWindowのIsDrawingSettingsSyncEnabled
+
設定変更通知
```

`SettingsService` のファイル監視でリアルタイム同期する方式は避け、同一アプリプロセス内の共有Stateを優先する。

---

# 23. チェック色拡張 — Ver1.51候補

現行チェック色：10色。

【方針決定】

> **最低18色程度まで増やしたい。**

ただし数だけ増やすのではなく、

```text
白背景
黒線図面
カラー図面
スキャンPDF
```

で見分けやすい色を調整する。

色追加時に必要：

```text
StrokeColor enum追加
XAML ColorPalette追加
GetMediaColor / GetRgb等の色変換追加
PDF保存・再読込確認
既存PDF互換確認
```

---

# 24. 左下フルパス表示削除 — Ver1.51候補

現在 `UpdateCurrentPdfWindowInfo()` で：

```csharp
StatusText.Text = _currentPdfPath;
```

として、StatusBar左下へフルパスを表示している。

無料公開版の記事・説明画像を作る際、ユーザー名やローカルフォルダ構成が写る可能性があり邪魔になる。

【方針決定】

> **左下StatusBarのフルPDFパス表示は削除する。**

内部の `_currentPdfPath` は保存・再読込等に必要なので残す。

ファイル名はWindowタイトル：

```text
PDF Markup - <fileName>
```

へ表示済み。

「最近使ったPDF」一覧のDirectoryPath表示は別機能なので、削除対象とは限らない。

---

# 25. PDF注釈保存方式 — 最重要

## 25.1 線 / フリーハンド

現在：

```text
1 StrokeModel
 ↓
1 PDF /Ink Annotation
```

保存内容：

```text
/Type       /Annot
/Subtype    /Ink
/F          4            ← Printフラグ
/T          PDFMarkup
/Contents   人間向けコメント表示
/PDFMarkupData  JSON
/CA         不透明度
/C          色
/BS /W      太さ
/InkList    座標
```

`/Contents`：

```text
朱書き｜口径:φ150

または

朱書き｜口径:φ150｜コメント:...
```

チェックも同様。

`/PDFMarkupData`：

```text
Mode
Color
Opacity
Diameter
```

をJSON保存。

## 25.2 文字

現在：

```text
TextAnnotationModel
 ↓
PDF /FreeText Annotation
```

保存：

```text
/Contents        実際の文字
/PDFMarkupData   Text用metadata JSON
/CA              透明度
/C               色
/DA              Font / FontSize / RGB
/F               4（Print）
```

---

# 26. フリーハンドがコメント一覧で細かく分かれる問題

現在はMouseDown～MouseUpごとに1 `StrokeModel` を作り、PDF保存時に1 `StrokeModel` ごとに1 `/Ink` 注釈を作る。

そのため、手書きで文字を書くと：

```text
1画目 → 1注釈 / 1コメント
2画目 → 1注釈 / 1コメント
3画目 → 1注釈 / 1コメント
...
```

となり、Acrobat等のコメント一覧が見づらくなる。

【改善候補】

これは「PDFMarkup上のグループ機能」より、

> **複数の連続フリーハンドストロークを1つのPDF Ink注釈として保存する**

方向を先に検討する。

PDFの `/InkList` 自体は複数のstroke arrayを保持できる形式で、現行 `LoadInkAnnotations()` も `/InkList` 内の各配列を読み取っている。

しかし現行 `AddInkAnnotation()` は：

```text
1 StrokeModel
→ strokeArray 1個
→ InkListへ1個だけ追加
→ Annotation 1件
```

となっている。

【要検証】

小さなPoCで：

```text
複数StrokeModel
 ↓
1 Ink Annotation
 ↓
InkListへ複数stroke array
```

として保存し、

```text
PDFMarkup再読込
Acrobat表示
Acrobatコメント一覧
印刷
削除
色 / 太さ / 透明度
```

を確認する。

「どこまでを1注釈とするか」は別途仕様決定が必要。

候補：

```text
一定時間内の連続ストローク
明示的な確定操作
描画モード切替まで
```

自動判定を複雑にしすぎない。

---

# 27. 外部PDF注釈との互換 — 要注意

【コード上の要確認事項】

現在 `LoadInkAnnotations()` は `/Subtype /Ink` の注釈を読み込み、`/T = PDFMarkup` かどうかで絞っていない。

同様に `LoadTextAnnotations()` も `/Subtype /FreeText` を読み込み、PDFMarkup作成注釈だけには限定していない。

一方、保存時の `RemoveExistingPdfMarkupAnnotations()` が削除するのは：

```text
/T = PDFMarkup
/PDFMarkupDataあり
旧PDFMarkup Contents形式
```

等のPDFMarkup注釈だけ。

したがって、Acrobat等で元から作成されたInk / FreeText注釈があるPDFでは、

```text
外部注釈をPDFMarkupが読み込む
 ↓
元の外部注釈は保存時に削除されない
 ↓
読み込んだ内容をPDFMarkup注釈として追加する
```

可能性がコード上はある。

【要検証】

Ver1.51作業中に、Acrobat等でInk / FreeText注釈を入れたテストPDFを作り、保存後に重複しないか確認する。

問題が出る場合は、

```text
PDFMarkup注釈だけを編集対象として読み込む

または

外部注釈を表示専用として別管理する
```

方向を検討する。

これはPDF互換性に関わるため、UI改善より優先度が高くなる可能性がある。

---

# 28. 座標変換 / Rotate — 現在仕様

PDF表示：PDFium。  
PDF辞書：PDFsharp。

現在 `PageInfo` で：

```text
PDFium Width / Height
/Rotate
MediaBox
CropBox
```

を取得できる。

過去に「横長ページ = 回転PDF」と推測したことで、Rotate=0のA3横ページまで補正してしまう問題があった。

現在は：

```text
pageInfo.Rotation == 90
```

の場合だけ既存の270°方向補正を適用する。

コードコメント上、現在のPoCで確認した補正を維持している。

Debug Shortcut：

```text
Ctrl+Shift+I
→ 現在ページのPDFium / Rotate / MediaBox / CropBox / Canvas情報

Ctrl+Shift+A
→ 現在ページのInk注釈生データ
```

座標問題を再度触る場合は、見た目だけで推測せずこの実情報を使う。

---

# 29. 朱書き / チェック表示切替

【コード確認済み】

```text
朱書き表示 ON / OFF
チェック表示 ON / OFF
```

を別々に切り替えられる。

非表示にした注釈は画面上だけでなく、選択状態も必要に応じて解除する。

保存処理では全ページの現在データを扱うため、表示OFFと削除は別。

---

# 30. 保存前の全ページ注釈読込

現在はページを表示した時にそのページの注釈を読み込む方式。

保存前に `EnsureAllPageAnnotationsLoadedForSave()` を使い、まだ画面表示していないページの注釈も読み込んでから全ページ保存する。

目的：

> 開いていないページの既存PDFMarkup注釈を保存時に失わない。

Ver1.51で注釈構造を変更する場合、この処理との整合も必ず確認する。

---

# 31. ローカル設定の現在仕様

保存先：

```text
%APPDATA%\PDFMarkup\settings.json
```

現行：

```json
{
  "RecentFiles": [],
  "WindowPlacement": {
    "Left": 0,
    "Top": 0,
    "Width": 1400,
    "Height": 850,
    "IsMaximized": false
  },
  "PanelLayout": {
    "IsLeftPanelOpen": true,
    "LeftPanelWidth": 220,
    "IsRightPanelOpen": true,
    "RightPanelWidth": 260
  }
}
```

※実際の値は端末ごとに異なる。

Ver1.51追加候補：

```text
角度吸着 ON / OFF
基準角度
必要なら描画設定の初期値
```

複数Window間リアルタイム同期Stateはsettings.jsonではなく、アプリ内共有Stateを優先する。

---

# 32. Ver1.51 改善候補一覧

2026-09-09現在：

```text
A. 利用者要望
1. 最大拡大率を上げる
2. 拡大時の表示解像度を確認する
3. Shift直線の角度吸着を設定可能にする
4. 基準角度を選択 + 直接入力できるようにする
5. 角度設定を基本設定として保存する

B. 複数選択
6. ドラッグ矩形で複数注釈を選択
7. Shift+クリックで個別追加 / 解除
8. 選択中の口径を一括変更
9. 一括変更を1回のUndoとして扱う

C. 複数PDF
10. 複数Window間で描画設定を同期
11. Window単位で同期ON / OFF
12. 同期対象：朱書き / チェック、色、太さ、透明度等

D. ファイル操作
13. WindowへPDF Drag & Dropで開く
14. 可能ならexe / shortcutへのPDF Drop起動
15. 左下StatusBarのフルパス表示削除

E. 描画
16. チェック色を最低18色程度へ増加・調整
17. フリーハンド複数ストロークを1 Ink注釈へまとめられるかPoC

F. 安全性 / 互換
18. Acrobat等の既存Ink / FreeTextを含むPDFの保存互換性確認
```

全部を一度に実装しない。

---

# 33. Ver1.51 推奨実装順

現在の優先順候補：

```text
Step 0
現在Repositoryのgit status / build確認
 ↓

Step 1
左下フルパス非表示
→ 小さく安全、説明記事にもすぐ反映できる
 ↓

Step 2
利用者要望：Zoom上限・画質検証
 ↓

Step 3
利用者要望：Shift直線の角度設定
→ 現行Shift / Ctrl+Shift矢印を壊さない
 ↓

Step 4
PDF Drag & Drop
 ↓

Step 5
複数選択
→ 矩形選択
→ Shift+クリック
→ 口径一括変更
→ Undo
 ↓

Step 6
複数Window描画設定同期
→ 共通State
→ Windowごとの同期ON/OFF
 ↓

Step 7
チェック色拡張
 ↓

Step 8
フリーハンド複数Stroke = 1 Ink注釈 PoC
 ↓

Step 9
外部Ink / FreeText互換テスト
```

Step 8 / 9はPDF保存構造へ触れるため、小さなテストPDFで分離検証してから本体へ入れる。

---

# 34. Ver1.51で壊さないもの

```text
Ver1.5で公開済みのPDF表示
既存PDFMarkup注釈の読込
上書き保存
別名保存
印刷可能な注釈 /F=4
A3 / Rotate補正
ページ別Undo / Redo
文字注釈
最近使ったPDF
複数PDF別Window表示
左右パネル状態保存
```

改善のために既存動作を一気に作り直さない。

---

# 35. 実装確認の基本セット

修正ごとに最低限：

```text
dotnet build
 ↓
実画面起動
 ↓
A4 PDF
 ↓
A3横 PDF
 ↓
複数ページ PDF
 ↓
朱書き
 ↓
チェック
 ↓
Shift直線
 ↓
Ctrl+Shift矢印
 ↓
文字
 ↓
保存
 ↓
PDFMarkupで再読込
 ↓
Acrobat等で表示
 ↓
印刷確認が必要な変更では印刷
```

PDF注釈保存方式へ触った場合は特に：

```text
上書き保存
別名保存
再保存
削除
Undo / Redo
別ページを開かずに保存
既存Acrobat注釈ありPDF
```

を確認する。

---

# 36. 現在も有効な注意事項

## MainWindow.xaml.csが大きい

約6,500行。

小修正は既存位置へ入れてよいが、複数Window共有Stateや複数選択など独立性の高い機能はService / Model分離を検討する。

ただし「きれいにするための全面リファクタリング」はVer1.51では優先しない。

## 座標を見た目で推測しない

A3 / Rotate問題で実績あり。

```text
Ctrl+Shift+I
Ctrl+Shift+A
```

やPDF辞書実値を使う。

## PDF保存変更は必ず実PDFで確認

PDFsharpで保存できた = Acrobatでも期待どおり、とは決めつけない。

```text
表示
コメント一覧
印刷
再読込
```

まで見る。

## 既存外部注釈

現行LoadはPDFMarkup以外のInk / FreeTextも読むため、外部注釈入りPDFは要確認。

## 複数Window

現在は同一Process内の複数 `MainWindow`。

設定同期は新しいexeプロセス同士を同期する話ではなく、まず同一アプリ内Window間同期を対象にする。

---

# 37. 次回チャット開始手順

最初に実Repositoryで：

```powershell
git status
git branch --show-current
git log -1 --oneline --decorate
dotnet build PDFMarkup.csproj
```

次に：

```text
PDFMarkup_AI_HANDOFF.md
MainWindow.xaml
MainWindow.xaml.cs
Models/StrokeModel.cs
Services/DrawingService.cs
Services/PdfService.cs
Services/SettingsService.cs
```

を確認。

Ver1.51で最初に触る候補：

```text
1. 左下フルパス非表示
2. Zoom改善
3. Shift角度設定
```

利用者要望を先に反映するなら：

```text
Zoom
→ Shift直線
→ 角度基本設定保存
```

を優先してよい。

---

# 38. 現在地まとめ

```text
PDFMarkup Ver1.5

PDFiumでPDF表示
 ↓
WPF Canvasで朱書き / チェック / 文字
 ↓
StrokeModel / TextAnnotationModel
 ↓
PDFsharpでInk / FreeText注釈へ保存
 ↓
PDFMarkup固有情報を/PDFMarkupDataへJSON保存
 ↓
再起動後もPDF注釈から復元
```

現在の操作：

```text
フリーハンド
Shift直線（45°系へ±7.5°以内で吸着）
Ctrl+Shift矢印
文字
選択
消しゴム
Undo / Redo
Zoom / Pan
ページ一覧
複数PDF別Window
```

Ver1.51：

```text
公開後に実際の利用者から改善要望が届いた
 ↓
Zoomをさらに拡大
Shift直線の角度吸着を選択可能にする
基準角度を選択 + 直接入力
基本設定へ保存
 ↓
使いながら出た改善も追加
```

追加候補：

```text
複数選択 → 口径一括変更
複数Window描画設定同期
Window単位同期OFF
PDF Drag & Drop
左下フルパス削除
チェック18色程度
フリーハンド複数strokeの1 Ink注釈化PoC
外部Ink / FreeText互換確認
```

長期原則：

> **PDF自体を壊さず、標準PDF注釈をできるだけ利用する。**

> **PDFMarkup固有情報は必要最小限をPDF注釈へ残し、再読込できる状態を維持する。**

> **実際に使って出た不便を優先し、Versionごとに小さく改善する。**

> **座標・PDF内部仕様は推測せず、実データを見て判断する。**

> **AI_HANDOFF.mdは、別チャットでも現在のProjectを安全に再開できる状態を維持する。**
