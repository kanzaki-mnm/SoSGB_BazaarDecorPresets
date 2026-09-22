# Bazaar Decor 効果付き配置プリセット調査

調査日: 2026-09-17。対象: PC版 STORY OF SEASONS: Grand Bazaar。

## 結論と確度

実装の見込みは高い。配置読取、枠ごとの変更、編集確定、効果集計の内部APIが存在する。名前付き複数プリセットは今回調べた範囲では未発見で、Mod側に保存・選択機能を作る方針が妥当。

公式にサポートされたMod SDKを発見したという意味ではない。根拠は既存のBepInEx IL2CPP Modと、ローカルのIl2CppInterop生成ラッパー。ラッパーではメソッドのシグネチャは確認できるが、ネイティブ側の処理順序や副作用までは読めない。今回ゲーム起動、配置変更、Mod実装・インストールは行っていない。

外部の公開Modソースにも、BepInExとゲームのinterop DLLを参照する開発方法が記載されている: https://github.com/joe-aquiare/sosgb-plugins

## 利用候補API

以下のパスは `decompile_Assembly-CSharp_dll/BokuMono/` 基準。行番号は現在のローカルコード。

| 用途 | API / データ | 根拠 |
|---|---|---|
| 現在配置の読取 | `BazaarManager.GetPutPartsDataList(PartsCategory)` / `GetPutPartsData(PartsCategory, int index)` | `BazaarManager.cs:11869`, `:11882` |
| 編集中配置の読取 | `GetEditPartsData()` / `GetEditPutPartsDataList(PartsCategory)` / `GetEditSettingCustomPartsId(PartsCategory, int)` | 同 `:11832`, `:11896`, `:11909` |
| 枠ごとの設定 | `SetCustomParts(uint itemId, PartsCategory category, int index)` | 同 `:11732` |
| 編集開始 | `OpenBazaarCustomMenu()` | 同 `:11686` |
| 確定・終了候補 | `SaveAndCloseBazaarCustom(Il2CppSystem.Action endBack)` | 同 `:11783` |
| 取消・終了候補 | `CloseBazaarCustom(Il2CppSystem.Action endBack)` | 同 `:11795` |
| モデル読込候補 | `LoadBazaarCustomPartsModel(Il2CppSystem.Action loaded)` | 同 `:11771` |
| 編集配置の効果集計 | `GetEditCustomPartsEffectList(...)`（配置リストを渡すオーバーロードもあり） | 同 `:11923`, `:11936` |
| バフ設定候補 | `SetupPartsBuff()` | 同 `:12904` |
| 任意配置の効果集計候補 | `BokuMono.API.Bazaar.RequestCompositeCustomPartsEffect(...)` → `RequestCustomPartsBuffParams(...)` | `API/Bazaar.cs:79`, `:95`, `:107`, `:123` |
| マスタ取得 | `CustomPartsMaster.GetMasterData(uint id)` | `Data/CustomPartsMaster.cs:42` |
| 所持品の調査先 | `BazaarCustomPartsStorageManager : StorageManager`、`ParcelItemDataArray(int)`、基底の `GetStack(uint)` | `BazaarCustomPartsStorageManager.cs:13`, `:305`; `StorageManager.cs:993` |
| 編集中使用数 | `GetEditSettingCustomPartsCount(uint id, PartsCategory)` | `BazaarManager.cs:11819` |
| 枠数設定 | `BazaarCustomSetting.CustomSettings`、各設定の `PutTentNum`, `PutShelfNum`, `PutOrnamentSNum`, `PutOrnamentLNum`, `PutOrnamentSpNum` | `BazaarCustomSetting.cs:326`, `:46`以降 |
| 画面更新候補 | `UIBazaarCustomPage.OnRefresh()` / `UpdateCachePartsList()` / `OnRefreshTab()` | `UIBazaarCustomPage.cs:1055`, `:1078`, `:1126` |
| 通常選択の観測先 | `UIBazaarCustomPage.OnDeside(BazaarCustomItemData)` / `OnFocusIn(...)` | 同 `:1197`, `:1292`。綴りは実コードどおり |

上記は呼出し可能な入口の候補一覧。`SetCustomParts`だけでモデル・所持数・効果・UIがすべて更新される保証はない。確定メソッドとバフ設定・モデル読込をむやみに重複呼出しせず、通常操作の呼出し順を先に観測する。

`CanGetParts`は名称だけで所持判定と断定できない。所持品の格納区画、配置中アイテムが所持数に含まれるか、同一IDを複数配置する場合の数量規則は要確認。

## 保存する内容

`BazaarManager.CustomData` は `List<PutPartsData> PutPartsDataDic` と `BazaarShelfUpgradeCount` を持つ。`PutPartsData`はカテゴリと `List<uint> DataDic` を持つ（`BazaarManager.cs:35`, `:48`, `:106`, `:121`）。

プリセットでは通常のC#データへコピーし、JSON等に次を保存する案がよい。

```text
schemaVersion
presets[]
  name: "加工品用" / "料理用" など
  slots[]
    category: Tent / Shelf / OrnamentS / OrnamentL / OrnamentSp
    index: カテゴリ内の設置位置
    itemId: uintのパーツマスタID
```

ゲームオブジェクトやIL2CPPポインタは保存しない。バフの数値を固定保存せず、選んだパーツからゲーム側で再計算する。`BazaarShelfUpgradeCount`などの進行度はプリセットから復元しない。

オブジェだけならOrnament系3種を対象にできる。店全体のバフ構成を切り替えるならテント・棚も含む設計が候補。将来的には適用対象カテゴリを選択できる形に拡張できる。

`ToSaveData()` / `FromSaveData(CustomData, int currentDevelopLevel)`（同 `:12008`, `:12020`）も存在するが、セーブ読込用の初期化や進行度処理を伴う可能性があり、プリセット切替の第一候補にはしない。`CustomData(CustomData)`が深いコピーかも未確認。

## 既存実機ログから確認できたこと

`SoSGB_BazaarDecorTransmog/logs/diagnostic-0.1.1-second.log:190` にマスタ163件、読取エラー0の記録。`:209` の `Layout` では、テント1枠・棚3枠・小オブジェ4枠・大オブジェ3枠の現在配置がカテゴリ・index・IDで取得されている。これはそのセーブの観測例であり、全プレイヤーの枠数ではない。

同ログ `:191` には `snapshot:ToSaveData` の配置読取時のNullReferenceExceptionがある。編集モード開始の同期return直後にも編集データが未準備となるケースが既存解析で報告されている。初期化完了を確認して読む必要があり、`IsCustomMode`だけに頼らない。

過去のログがあるため、配置読取の可否をゼロから調べ直す必要はない。ただしプリセットを一括適用して確定し、見た目とバフが更新されることの実証にはなっていない。

## 推奨する最初の実装

1. 通常の装飾編集画面で、現在配置を名前付きで保存する。
2. 同じ画面でプリセットを選び、存在するID・カテゴリ一致・所持数・現在利用できる枠を全件検証する。不足があれば適用前に理由を表示する。
3. 編集前の配置を退避し、通常操作と同じ設定経路で編集配置へ反映する。入替え時の一時的な同一ID重複や数量不足を避ける処理は、通常の数量規則を観測して設計する。
4. 通常のプレビューと効果表示を更新し、ゲーム既存の確定・取消操作を使う。
5. 実機で確定／取消、A→B→A、同一ID複数枠、未所持・未解放枠、エリア再入場、通常セーブ後の再読込を確認する。加工品用・料理用それぞれで効果表示と代表商品の売価も比較する。

既存の `SoSGB_BazaarDecorTransmog` は外見差替え用。今回の効果付き配置プリセットとは目的が異なる。配置読取やマスタ取得は参考にできるが、モデルIDだけ差し替える方式では今回のバフ切替要件は満たせない。同プロジェクトの `Presets.cs` もVisualPreset用で、現csprojのCompile対象には含まれていない。

調査段階の結論としては、BepInEx IL2CPP + Harmonyを使い、通常の編集画面にプリセット保存・読込を追加する構成が有望。残る主な不確定点は保存形式ではなく、設定・モデル更新・効果再計算・確定の実際の連携手順。
