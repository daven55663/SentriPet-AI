# Your own themes: theme.json

**English** | [繁體中文](custom-themes.zh-TW.md)

Make your own look for the pets out of pictures and a layout file — and share it as a folder (since 2.4,
[#24](https://github.com/daven55663/SentriPet-AI/issues/24)). A theme is only data: SentriPet never runs anything from it.

*Settings → Looks → Your own themes → Open themes folder* opens the `themes` folder of the settings folder; the first time
it puts the example theme *Little Cloud* (`cloud/`) in it. Every folder there with a `theme.json` is a theme: it shows up
in the right-click menu → *Change look* and on the settings page. After changing a theme, press *Reload*.

| System | Folder |
|---|---|
| Windows | `%APPDATA%\SentriPet\themes\` |
| macOS | `~/Library/Application Support/SentriPet/themes/` |
| Linux | `~/.config/SentriPet/themes/` |

The example is also in the repository: [examples/themes/cloud](../examples/themes/cloud).

## How it is laid out

Each AI gets a **card** of the same size; on it you place **elements** (pictures, texts, bars, rings, boxes) at x/y
positions in pixels, top-left is 0,0. The cards are put in a row, a column or a grid, optionally on a background.

```jsonc
{
  "version": 1,
  "name": { "en": "Little Cloud", "zh-TW": "小雲朵" },   // or just "Little Cloud"
  "mood": { "en": "Floaty", "zh-TW": "輕飄飄" },
  "blurb": { "en": "A cloud per AI, its face follows the quota" },
  "layout": "row",              // row, column or grid
  "columns": 3,                 // grid only
  "gap": 6,                     // space between cards
  "background": { "color": "#801E293B", "radius": 16, "padding": 8 },   // optional, behind all cards
  "card": {
    "width": 132, "height": 178,
    "color": "#E6182235", "radius": 18, "border": "#40FFFFFF",       // or "image": "card.png"
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

`name`, `mood` and `blurb` can be one text or one per language (`zh-TW`, `zh-CN`, `en`, `ja`, `ko`); the language in use
is taken, then English. Comments (`//`) and trailing commas are fine.

## Boxes (`background`, `card`)

| Key | Meaning |
|---|---|
| `color` | Fill colour |
| `image` | A picture stretched over the box (instead of the colour) |
| `radius` | Rounded corners |
| `border`, `borderWidth` | Border colour and width (1 when only the colour is given) |
| `padding` | Space inside the box |

`card` also takes `width` and `height` (40–600) and `elements` (at most 60).

## Elements

Every element has `type`, `x`, `y`, and can have `when` (only show it `working` / `idle` / `data` / `nodata`) and
`meter` — the quota it is about: `headline` (the big number: the 5-hour quota when there is one; default), `primary` (the
one that runs out first), `secondary`, or a number (0 = the first quota of that AI, 1 = the second…).

| Type | Keys |
|---|---|
| `image` | `width`, `height`; `image` (one picture) or `states` (a picture per state, below); `animate`: `none`, `bob` (floats up and down), `breathe` |
| `text` | `text` (with placeholders), `size`, `bold`, `color`, `align` (`left`/`center`/`right`), `width`, `height` (taller than one line wraps), `font` (`ui`, `number`, `mono`) |
| `bar` | `width`, `height`, `track` (the empty part), `fill`, `radius` — filled up to what is left |
| `ring` | `width` (the diameter), `thickness`, `track`, `fill` — a ring that goes round as far as what is left |
| `rect` | `width`, `height`, `color`, `radius` |

**States** of a picture: `great` (60 % or more left), `good` (30–60 %), `worried` (12–30 %), `low` (under 12 %),
`empty` (used up), `unknown` (no numbers) and `working` (the AI is working right now; used when there is one). A state
without a picture uses `default`, or the first picture.

**Colours**: `#RGB`, `#RRGGBB` or `#AARRGGBB` (AA = opacity), and for texts, bars, rings and boxes also `level`
(green / yellow / red by what is left), `provider` (the AI's own colour), `provider-light`, `provider-dark`.

**Placeholders** in texts: `{name}` (the AI), `{pct}` (left, `58%`), `{used}` (used), `{meter}` (the quota's name),
`{reset}` (time until it resets), `{plan}`, `{status}` (the status line — or why there are no numbers).

## Pictures

- PNG (with transparency), JPG, WebP or BMP, inside the theme's folder (sub-folders are fine; `..` and absolute paths are
  not), at most 8 MB and 4096 × 4096 pixels.
- They are scaled to the element's size keeping their proportions; draw them at twice the size you show them for sharp
  results on high-DPI screens.

## When something is wrong

Nothing crashes. *Settings → Looks* lists what is wrong under *Your own themes*: a theme.json that can't be read makes the
theme unusable (it isn't in the menu); a wrong element (unknown type, missing picture, bad colour) is left out and the
rest is drawn; a mistyped placeholder or a wrong `when`/`meter` is reported and a default is used.

## Sharing

Zip the theme's folder and share it; others unzip it into their `themes` folder and press *Reload*. Themes are data only
— pictures and a JSON file — so they can't run anything.
