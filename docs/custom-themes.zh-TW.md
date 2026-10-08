# 自訂造型：theme.json

[English](custom-themes.md) | **繁體中文**

用圖片和一個版面檔做出自己的桌寵造型，也能整個資料夾分享給別人（2.4 起，[#24](https://github.com/daven55663/SentriPet-AI/issues/24)）。
造型只有資料：SentriPet 不會執行造型裡的任何東西。

**設定 → 造型 → 自訂造型 →「打開造型資料夾」** 會打開設定資料夾裡的 `themes`，第一次會放進範例造型「小雲朵」（`cloud/`）。
裡面每個有 `theme.json` 的資料夾就是一個造型，會出現在右鍵選單 →「換造型」和設定頁。改完造型後按「重新載入」。

| 系統 | 資料夾 |
|---|---|
| Windows | `%APPDATA%\SentriPet\themes\` |
| macOS | `~/Library/Application Support/SentriPet/themes/` |
| Linux | `~/.config/SentriPet/themes/` |

範例也在原始碼裡：[examples/themes/cloud](../examples/themes/cloud)。

## 版面的概念

每個 AI 一張一樣大的**卡片**，卡片上用 x／y 座標（像素，左上角是 0,0）擺**元素**：圖片、文字、進度條、圓環、方塊。
卡片排成一列、一欄或格狀，後面可以再加一個背景。

```jsonc
{
  "version": 1,
  "name": { "en": "Little Cloud", "zh-TW": "小雲朵" },   // 或直接寫 "小雲朵"
  "mood": { "en": "Floaty", "zh-TW": "輕飄飄" },
  "blurb": { "zh-TW": "每個 AI 一朵雲，表情跟著額度變" },
  "layout": "row",              // row（一列）、column（一欄）或 grid（格狀）
  "columns": 3,                 // grid 才用
  "gap": 6,                     // 卡片之間的距離
  "background": { "color": "#801E293B", "radius": 16, "padding": 8 },   // 可省略，所有卡片後面的背景
  "card": {
    "width": 132, "height": 178,
    "color": "#E6182235", "radius": 18, "border": "#40FFFFFF",       // 或 "image": "card.png"
    "elements": [
      { "type": "image", "x": 16, "y": 4, "width": 100, "height": 80, "animate": "bob",
        "states": { "great": "great.png", "good": "good.png", "worried": "worried.png", "low": "low.png",
                    "empty": "empty.png", "unknown": "unknown.png", "working": "working.png" } },
      { "type": "text", "x": 8, "y": 88, "width": 116, "text": "{name}", "size": 13, "bold": true, "align": "center" },
      { "type": "text", "x": 8, "y": 105, "width": 116, "text": "{pct}", "size": 22, "font": "number", "color": "level", "align": "center" },
      { "type": "bar", "x": 14, "y": 138, "width": 104, "height": 6, "fill": "level", "when": "data" }
    ]
  }
}
```

`name`、`mood`、`blurb` 可以只寫一種，或每種語言各一（`zh-TW`、`zh-CN`、`en`、`ja`、`ko`）：先用目前的語言，再用英文。
可以寫註解（`//`）和多餘的逗號。

## 方塊（`background`、`card`）

| 鍵 | 意思 |
|---|---|
| `color` | 填滿的顏色 |
| `image` | 拉滿整個方塊的圖片（取代顏色） |
| `radius` | 圓角 |
| `border`、`borderWidth` | 邊框顏色與粗細（只寫顏色時是 1） |
| `padding` | 內距 |

`card` 另外要寫 `width`、`height`（40～600）和 `elements`（最多 60 個）。

## 元素

每個元素都有 `type`、`x`、`y`，還可以寫 `when`（只在 `working` 工作中／`idle` 沒在工作／`data` 有資料／`nodata` 沒資料時顯示）
和 `meter`：這個元素講的是哪個額度——`headline`（大數字那個：有 5 小時額度就是它；預設）、`primary`（最先用完的）、`secondary`，
或數字（0 = 那個 AI 的第一個額度、1 = 第二個……）。

| 類型 | 鍵 |
|---|---|
| `image` | `width`、`height`；`image`（一張圖）或 `states`（每種狀態一張，見下面）；`animate`：`none`、`bob`（上下飄）、`breathe`（呼吸） |
| `text` | `text`（可用記號）和／或 `states`（每種狀態一段文字，見下面）、`size`、`bold`、`color`、`align`（`left`／`center`／`right`）、`width`、`height`（比一行高就換行）、`font`（`ui`、`number`、`mono`） |
| `bar` | `width`、`height`、`track`（空的部分）、`fill`、`radius`：填到剩下的比例 |
| `ring` | `width`（直徑）、`thickness`、`track`、`fill`：繞到剩下的比例 |
| `rect` | `width`、`height`、`color`、`radius` |

圖片和文字的**狀態**：`great`（剩 60% 以上）、`good`（30～60%）、`worried`（12～30%）、`low`（不到 12%）、`empty`（用完）、
`unknown`（沒有數字）、`working`（AI 正在工作；有寫時優先用）。沒有圖的狀態用 `default`，再沒有就用第一張；
沒有文字的狀態用 `default`，再沒有就用 `text`（也沒有就不顯示）。文字（`text` 和每個狀態的）也可以像 `name` 一樣每種語言各一：

```jsonc
{ "type": "text", "x": 8, "y": 120, "width": 116, "text": "{name}",
  "states": { "great": { "en": "All good!", "zh-TW": "精神飽滿！" }, "low": "快沒力了…", "working": "忙碌中 ✦" } }
```

**顏色**：`#RGB`、`#RRGGBB` 或 `#AARRGGBB`（AA = 不透明度）；文字、進度條、圓環、方塊還可以用 `level`（依剩餘量綠／黃／紅）、
`provider`（那個 AI 的代表色）、`provider-light`、`provider-dark`。

文字裡的**記號**：`{name}`（AI 名稱）、`{pct}`（剩下多少，`58%`）、`{used}`（用了多少）、`{meter}`（額度名稱）、
`{reset}`（距離重置多久）、`{plan}`、`{status}`（狀態文字，或沒有數字的原因）。

## 造型自帶的台詞

在 `theme.json` 旁邊放一個 `lines.json`，換上這個造型時桌寵就會說這些台詞（2.5 起，[#28](https://github.com/daven55663/SentriPet-AI/issues/28)），
每種心情寫一兩句，造型就有自己的個性。寫法和[你自己的 lines.json](custom-lines.zh-TW.md) 一樣：先依語言、再依事件分組
（每種心情的 `idleGreat`～`idleEmpty`、`working`、`poke`、`greeting`、`done`……）。你自己的台詞也照樣會說；
寫 `"mix": true` 會連內建的台詞一起混著說。範例造型「小雲朵」每種語言都有附台詞。

```jsonc
{
  "mix": true,
  "zh-TW": {
    "idleGreat": ["晴空萬里！{name} 還有 {pct}"],
    "idleLow": ["大雨要來了！{name} 只剩 {pct}"],
    "poke": ["軟綿綿的，別把我戳破～"]
  },
  "en": { "idleGreat": ["Clear skies! {name} has {pct} left"] }
}
```

## 圖片

- PNG（可透明）、JPG、WebP 或 BMP，放在造型的資料夾裡（可以有子資料夾；不能用 `..` 或絕對路徑），最大 8 MB、4096 × 4096 像素。
- 會依元素大小等比例縮放；畫成顯示大小的兩倍，高解析度螢幕上會比較清楚。

## 寫錯的時候

不會當掉。設定 →「造型」的「自訂造型」底下會列出哪裡有問題：theme.json 讀不懂時整個造型不能用（不會出現在選單）；
某個元素寫錯（不認得的類型、找不到圖片、看不懂的顏色）時略過那個元素，其他照樣畫；記號、`when`、`meter` 寫錯時會提醒並用預設值。
造型的 `lines.json` 寫錯的地方也會列在那裡，寫對的台詞照樣會說。

## 分享

把造型的資料夾壓縮起來分享（`lines.json` 也一起帶走）；別人解壓縮到自己的 `themes` 資料夾、按「重新載入」就能用。
造型只有圖片和 JSON，不會執行任何東西。
