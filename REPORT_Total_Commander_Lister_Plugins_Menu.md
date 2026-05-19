# Total Commander lister plugin "Plugins" menu behavior

> Research Date: 2026-05-19 | Confidence: HIGH | Sources: 10

## TL;DR

- In Lister's **Plugins** menu, Total Commander groups entries in this order: **Internal** first, then plugins pinned via **Define view method by file type**, then plugins whose **detect string matches the current file**, then **all remaining plugins**. Source: Christian Ghisler himself. [1]
- The menu is **not limited to alternative plugins only**. It is built from the full candidate set in those groups. So the **currently active plugin normally appears too** if it belongs to one of those groups. I found no official statement that the active plugin is excluded; the documented grouping logic implies inclusion. [1][2][3]
- The **detect string** in `[ListerPlugins]` / `x_detect=` directly influences whether a plugin is in the **matching** group for the current file and often whether it gets auto-selected before other plugins. Emptying or narrowing the detect string can push a plugin out of the top matching group or prevent auto-loading. [1][4][5][6]
- Detect strings are stored in `wincmd.ini`; plugin authors can provide an initial detect string through `ListGetDetectString`, but **Total Commander deliberately does not refresh detect strings on plugin update** because users may have customized them manually. [7][8]

## Key findings

### 1) Which entries appear in the Lister "Plugins" menu?

Christian Ghisler described the exact logic for the Lister Plugins menu:

1. `Internal`
2. Plugins configured via **Options -> Configure -> Multimedia -> Define view method by file type**
3. Plugins whose **detect string matches** the currently viewed file
4. All remaining plugins

This is the clearest official answer to "which plugins appear there?" It is not just "matching alternatives"; the menu contains both prioritized/matching entries **and** the rest of the installed plugins. [1]

Within each group, forum moderators/users explain that ordering follows the WLX plugin order from **Configuration -> Options -> Plugins -> Lister plugins (WLX) -> Configure**. Ghisler also confirmed that this order matters because Total Commander checks plugins in that chosen order until one can handle the file type. [1][2]

### 2) Does the currently active lister plugin appear in the menu?

**Practical conclusion: yes, normally it does.**

Why:

- Ghisler's grouping rule says the menu contains **all plugins configured by file type**, **all plugins whose detect string matches**, and then **all remaining plugins**. That leaves no documented exclusion bucket for "currently active plugin". [1]
- The same discussions treat the menu as a presentation of the plugin priority/candidate set, not a list of alternatives only. [1][2]
- Threads about cycling with key **4** also describe switching through all plugins applicable to the file type, plus internal modes, which is consistent with the active plugin being part of the available set rather than hidden from it. [3][9]

What I **did not** find: an explicit official sentence saying "the active plugin is shown in the menu". So this specific point is an inference from the official grouping logic plus related behavior discussions, but it is a strong one. [1][2][3]

### 3) How do detect strings / `_detect` entries influence menu appearance?

They matter a lot.

- Total Commander checks `[ListerPlugins]` in `wincmd.ini` and evaluates each plugin's `x_detect` expression against the current file. A plugin with a matching detect string lands in the menu's **matching plugins** group and is a candidate for auto-selection. [1][7]
- If a plugin has **no useful detect string**, Total Commander may need to load it more broadly or rely on plugin order, which can slow startup or cause less precise matching. Ghisler explicitly asked plugin authors to provide detect strings so TC doesn't need to load the plugin every time Lister is loaded. [8]
- Users/admins can manually edit `x_detect=` to **limit**, **expand**, or effectively **disable** where a plugin matches:
  - Example: `4_detect="MULTIMEDIA & ext="AVI" | force"` to steer AVI handling to a specific plugin. [5]
  - Example: `1_detect=""` to stop a graphics plugin from matching graphics automatically, letting internal handling win. [4]
  - Example: `xxx_detect=MULTIMEDIA | ext="WEBP"` so Imagine matches WebP. [6]
  - Example workaround for RTF: `0_detect="(FORCE & EXT="RTF" ) | !(EXT="RTF")"` so internal RTF opens first, but plugin remains reachable with `4`. [9]

So, concretely:

- **Matching detect string** -> plugin appears in the menu's **matching** section. [1]
- **No match / empty detect string** -> plugin falls out of that matching section and only appears later under **remaining plugins** (unless pinned by file-type view-method configuration). [1][4]
- **Pinned via Define view method by file type** -> plugin is moved above the matching group regardless of normal detect-order behavior. [1][2][10]

### 4) How do "Define view method by file type" entries relate to the menu?

These entries are a second, higher-priority mechanism.

Ghisler states they appear **above** normal detect-string matches in the Plugins menu. [1]

This means:

- A plugin can be surfaced near the top even if detect strings alone would not place it there. [1][10]
- This is the official way to "pin" a plugin for certain extensions or cycling sequences. Ghisler gave the example `4i,8,mmedia.wlx` for `*.mp3 *.mp2`, which causes internal/explorer/mmedia to cycle and places `mmedia` near the top for those files. [1]
- Forum examples for WebP confirm that this method can select a specific plugin as preferred for an extension independent of general plugin order. [10]

### 5) How persistent/custom are detect strings in `wincmd.ini`?

They are effectively user configuration once written.

- The 2023 "will not be changed" thread states that detect strings are **not updated when you update a plugin**, specifically to avoid overwriting user customizations. Ghisler explicitly confirms this. [7]
- A 2023 suggestion thread likewise notes that detect strings for WLX/WDX plugins are taken from the plugin's `ListGetDetectString` **if the plugin isn't already installed**; otherwise they should come from `wincmd.ini`. [11]

So `_detect` lines are not just cache; they are treated as authoritative user-editable settings after installation. [7][11]

## Direct answers to your questions

### Which plugins appear in the menu after F3 -> internal viewer -> Plugins?

- `Internal`
- Plugins pinned by **Define view method by file type**
- Plugins whose detect string matches the current file
- All other installed lister plugins

Source: Ghisler's official forum reply. [1]

### Does the currently active lister plugin appear there?

**Most likely yes.** The official logic describes category inclusion, not "alternatives only," and does not exclude the active plugin. I found no official source claiming the current plugin is hidden. [1][2][3]

### How do detect strings / `_detect` in `wincmd.ini` affect menu appearance?

They decide whether a plugin is treated as a **match for the current file** and therefore appears in the menu's higher **matching** section versus only in the lower **remaining plugins** section. Editing `_detect` can also suppress auto-use, narrow matching to selected extensions, or preserve internal view as default while still allowing plugin switching with `4`. [1][4][5][6][9]

## Gaps / caveats

- I did **not** find an explicit official sentence saying "the active plugin itself is shown in the Plugins menu." That conclusion is derived from the official grouping rules and surrounding behavior documentation. Confidence is still high because the documented logic leaves no special exclusion for the active plugin. [1]
- The TotalcmdWiki `wincmd.ini` page is useful background but incomplete and somewhat dated for plugin-specific details; forum statements by Ghisler are stronger evidence here. [12]

## Sources

| # | Source | Date | Credibility |
|---|---|---|---|
| 1 | [Sorting plugins in the "Plugins" menu in the lister](https://www.ghisler.ch/board/viewtopic.php?t=77662) | 2022-09 / 2025-08 | ★★★★★ |
| 2 | [Default Lister for a Filetype and Switching Listers in Quick View Panel](https://www.ghisler.ch/board/viewtopic.php?t=76767) | 2022-05 | ★★★★☆ |
| 3 | [How to use internal lister to view image files?](https://ghisler.ch/board/viewtopic.php?t=31517) | 2011-10 | ★★★★☆ |
| 4 | [How to use internal lister to view image files?](https://ghisler.ch/board/viewtopic.php?t=31517) - detect string can be emptied | 2011-10 | ★★★★☆ |
| 5 | [several similar lister plugins](https://www.ghisler.ch/board/viewtopic.php?t=25684) | 2010-03 | ★★★★★ |
| 6 | [Default Lister for a Filetype and Switching Listers in Quick View Panel](https://www.ghisler.ch/board/viewtopic.php?t=76767) - WEBP detect example | 2022-05 | ★★★★☆ |
| 7 | [[TC 11.01RC2 64 bit and below] The value of the DETECT line for Lister plugins is not updated](https://www.ghisler.ch/board/viewtopic.php?t=79976) | 2023-08 | ★★★★★ |
| 8 | [Detect strings for lister plugins](https://www.ghisler.ch/board/viewtopic.php?t=1870) | 2003-10 | ★★★★★ |
| 9 | [Lister plugins don't allow to switch to RTF view](https://www.ghisler.ch/board/viewtopic.php?t=56945) | 2020-01 | ★★★★★ |
| 10 | [Default Lister for a Filetype and Switching Listers in Quick View Panel](https://www.ghisler.ch/board/viewtopic.php?t=76767) - file-type method beats order | 2022-05 | ★★★★☆ |
| 11 | [More advanced plugin installation](https://www.ghisler.ch/board/viewtopic.php?t=80033) | 2023-09 | ★★★★☆ |
| 12 | [wincmd.ini - TotalcmdWiki](https://www.ghisler.ch/wiki/index.php/Wincmd.ini) | 2024-07 | ★★★☆☆ |

RESEARCH_COMPLETION_REPORT:
  from: "@research"
  to: "Kai"
  status: "complete"
  timestamp: "2026-05-19T00:00:00Z"
  duration: "8 minutes"
  report_file: "REPORT_Total_Commander_Lister_Plugins_Menu.md"
  sources_analyzed: 12
  sources_discarded: 6
  confidence: "HIGH"
  headline: "Total Commander’s Lister Plugins menu shows internal, pinned file-type methods, detect-string matches, and then all remaining plugins; detect strings directly control whether a plugin is treated as a current-file match."
