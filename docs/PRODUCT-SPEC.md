# Holiday Lights - Modern Edition 6.0

## Product and UX specification

| | |
|---|---|
| File | `docs/PRODUCT-SPEC.md` |
| Status | **Final specification, to be built as written.** Every choice below is a decision, not an option. Where this document is silent, the exact 5.4 behavior (as the code and the golden test data reproduce it) and `docs/ARCHITECTURE.md` (structure) apply; where they disagree with this document, this document wins. |
| Basis | Synthesis of three design proposals (a classic, a delightful and a simple one; the classic one as the base, with the other two grafted in), corrected for every weakness the fidelity, usability and feasibility reviews found. Conflicts are resolved in section 0. |
| Commission | "Make the modern edition intuitive, well thought out, and festive/even better than the original." |
| Inputs | A study of the original Holiday Lights 5.4 (its behavior, file formats, registry values, help and art) and platform prototypes on Windows 11. Those working notes are not part of the public repository; this specification, `docs/ARCHITECTURE.md` and the golden test data restate everything the build needs. |
| Date | 2026-10-08 |

### Conventions

* **[MUST]** ships in 6.0. **[NICE]** is a nice-to-have, prioritized in 7.4. Anything untagged is MUST.
* Text in "double quotes" is the exact UI string. `&` marks the access key, as in the 5.4 resources (WPF uses `_`).
  "..." in a label is the ellipsis character (U+2026). Every command that opens a window ends with it.
* Sizes are in DIP (1/96 inch) unless they say "px" (physical pixels).
* "5.4" and "classic" mean Holiday Lights 5.4 for Windows (Tiger Technologies, 2003), exactly as it behaves.
* **House style.** Title Case for page names, group headers, buttons, menu items, radio buttons and check boxes
  (the 5.4 style: "Add Bulb...", "Play the Chosen Songs", "Only When the Screen Saver is On"). Sentence case for
  descriptions, tooltips, status lines and messages. Every 5.4 label that survives is kept letter for letter,
  including its access key.
* No emoji anywhere in the UI. Glyphs come from Segoe Fluent Icons (code points in 4.7).
* Numbers, dates and times follow the Windows locale ("1,550", "Oct 8, 2026").

### Contents

0. Decisions (conflicts resolved)
1. Principles and target users
2. Information architecture: tray, Settings window, windows, first run, every 5.4 feature mapped, retirements
3. Pages and windows in detail
4. Visual and motion design
5. Lights behavior
6. Subsystems: Music Box, screen saver, bulbs and Bulb Editing, themes, credits, startup and hot keys, help,
   5.4 import, `.bul` files, install
7. Defaults, shipped themes, scope (MUST with build priority, NICE), acceptance scenarios, risks
* Appendices: A tooltips and descriptions, B keyboard map, C data model, D copy deck, E notes for the product owner

---------------------------------------------------------------------------------------------------------------------

## 0. Decisions (conflicts resolved)

> **Product-owner amendments (binding, added after the design panel):**
> * **PO-1** — "One frame around all displays" (5.2.4) is **MUST** for 6.0 (priority P1), offered as the second "Frame"
>   choice next to the default "Each Display". The commissioning user described the goal as "a bulb wreath around the
>   desktop", so both framings must exist and be correct on the reference PC (two 3840x2160 @150%, second at x = -3840).
> * **PO-2** — Implementation details that this spec leaves open are decided in `docs/ARCHITECTURE.md` and
>   `docs/CONTRACTS.md`; where they conflict with a product decision here, this spec wins (e.g. D24 user-file paths).
> * **PO-3** — For automated testing every component honours `HOLIDAYLIGHTS_DATA_ROOT` and the App accepts
>   `--data-root <dir>` and `--no-system-changes` (no Run key, file association, screen saver or shortcut changes).

The three proposals and their reviewers disagreed on the points below. Each line is the binding decision and its
reason in one sentence.

| # | Topic | Decision | Why |
|---|---|---|---|
| D1 | Pages and names | "Home" (new) comes first, then the five 5.4 pages in 5.4 order with 5.4 names: "Bulb Factory", "Music Box", "Screen Saver", "Themes" (in Sharing's old slot), "General". Caption "Holiday Lights Settings". The bulb editor stays "Bulb Editing". | Fans find every word where they left it (5.4 help T15/T30 call the arranging page the Bulb Factory); newcomers get one calm hub. |
| D2 | Tray clicks | One left or right click opens the menu at once (5.4, "click once, not twice"). A double-click closes that menu and opens Settings (5.4 help T4). Enter opens Settings. The menu is reordered by frequency of use; every 5.4 item keeps its exact label. | Keeps the documented 5.4 gestures without delaying the menu; puts the frequent actions first. |
| D3 | OK, Cancel, closing | Live apply as in 5.4, with "OK", "Cancel", "Help". Cancel restores the complete snapshot (fixing both 5.4 Cancel bugs) and brings back anything removed in the session; it never deletes something you added or made. The X button and tray "Exit Holiday Lights" keep changes. Esc never closes the window. Every page header has Undo/Redo and "Undo All Changes Since This Window Opened". | A window whose changes are already visible on the desktop must not discard them on X; Cancel stays for those who explored and want to go back. |
| D4 | Bulb Factory geometry | The 8 boxes frame a live, exact preview of the real screen; the bulb list sits beside the frame at full height (below it on narrow windows). The list keeps a "Details" view that reproduces the 5.4 row. | Boxes around a picture of the screen explain themselves; the 5.4 list-in-the-middle needed help text to explain. |
| D5 | Choosing bulbs | Single click selects a bulb and tries it on in the preview only. Double-click, Enter or the "Use for ..." button applies it to the target (default "Whole Frame"). Dragging keeps the exact 5.4 rules: box to box copies (Shift moves), back to the list removes. Double-clicking a bulb no longer opens Edit Bulb. | Browsing can never change the arrangement by accident; drag-and-drop fans lose nothing. |
| D6 | Show filter | Keeps the 5.4 words ("All Bulbs", "Built-In Bulbs", "Add-On Bulbs", categories) and resets to "All Bulbs" each time Settings opens. Sort and view are remembered. | A filter left on from last week hides bulbs without saying so. |
| D7 | Themes | 5.4 snapshot model: a theme is exactly the 13 classic values, including "Play the Chosen Songs". Loading replaces the current settings; edits never rewrite a theme silently. The 11 installer themes keep their exact names and can be deleted or replaced; "Restore Built-In Themes" brings them back. "Recent Settings" keeps the last 5 replaced states. | Fidelity, and no edits that "vanish" when the calendar moves on. |
| D8 | Music and themes | A global "Play Holiday Music" switch (a device preference, not in themes) gates all music; themes keep their 5.4 play mode. Off for newcomers. Imported users get it on unless 5.4 was set to "Never", and their first song waits until the welcome card closes. | Themes and the calendar can never start sound for someone who did not ask for music; theme data stays 5.4-exact. |
| D9 | Automatic themes | "Change Themes Automatically on Holidays" is on for newcomers and off for customized 5.4 imports. Holidays map to the classic themes (Christmas = "Christmas 1"); between holidays the lit "Classic Lights" theme shows. Picking a theme by hand turns it off, with one-click "Turn Back On". Switches happen only at boundaries. | Seasonal delight for newcomers; a fan's own lights are never replaced unasked; the lights never vanish between holidays. |
| D10 | 5.4 at factory defaults | When 5.4's settings equal its factory defaults and it has no user themes (true on the commissioning PC), the newcomer look applies (Automatic, music off) while everything else is still imported, and the welcome card offers one-click "Use My 2003 Lights" (the exact 5.4 state). Customized 5.4 installs are imported as they are. | Factory defaults carry no personal choice (on this PC 5.4 never worked); the 2003 state stays one click away. Two of three reviews preferred this. |
| D11 | Patterns and music | The five classic patterns are exact and are never re-timed. MUST new patterns: "Twinkle", "Slow Glow", "Chase Around the Screen", "Dance to the Music". Music changes lights only in "Dance to the Music". "Waves", "Combination" and beat-locking other patterns are NICE. | Exact classic timing (PO decision 5) and a controlled scope. |
| D12 | Look | One "Look" control: "Modern Glow" (default), "Bright Glow", "Classic 2003", "Custom" (selected automatically). "Classic 2003" = crisp pixels, no glow, no fading, 16-steps-per-second screen saver. | One switch back to 2003, one switch to more sparkle. |
| D13 | Reduced motion | Windows "Animation effects" off never stops the lights. "Limit Flashing to 3 Flashes per Second" keeps them flashing below the photosensitivity threshold and is on by default when animation effects are off at first run. | A product whose lights stop by default looks broken. |
| D14 | Energy saver | Default "Use Less Power": keep flashing at 300 ms or slower, no glow, no fading. | Festive and frugal; "Stop Flashing" and "Turn Off the Lights" remain choices. |
| D15 | Multi-monitor | "Each Display" (every display gets the full frame) is the default 6.0 mode. "One frame around all displays" (a single wreath around the whole desktop, algorithm in 5.2.4) is MUST in 6.0 as the second choice (product-owner amendment PO-1). Per-display check boxes and "Main Display Only" reproduce 5.4. | Correct on any layout, mixed DPI and per-display pausing. |
| D16 | Hot keys | Exactly one global hot key is on by default: the 5.4 job (switch On Desktop / On Top) on Ctrl+Alt+Shift+B (5.4 letter, imported letter kept). "Turn the Lights On or Off" exists but is off. An on-screen pill confirms each use; Ctrl+Alt combinations are checked against AltGr layouts; Ctrl+Shift+B may be re-recorded with a warning. | Ctrl+Shift+B is the browsers' bookmarks-bar key; nobody gets keys they did not ask for. |
| D17 | Removing files | Your bulbs, songs and pictures move to a private holding folder (so Undo and Cancel can bring them back) and go to the Recycle Bin when the Settings window closes. Bundled ones are hidden and can be restored. Undoable removals ask no question. | Real undo without depending on Recycle Bin restore APIs. |
| D18 | Notifications | Tray balloon notifications (Windows shows them as toasts) have no buttons; clicking one opens the matching page with an InfoBar that holds "Undo". At most one notification of each kind per day. | Feasible with H.NotifyIcon; nothing interrupts work. |
| D19 | Controls | WPF's built-in Fluent theme plus a small in-house control kit (3.0.1). WPF-UI is not used. | The built-in theme lacks InfoBar, ContentDialog, NumberBox and friends; WPF-UI would fight `ThemeMode=System`. |
| D20 | Previews | Every preview uses the real layout engine and a CPU compositor with additive glow into a `WriteableBitmap`. Theme cards and tiles are static until hovered or focused. | Exact WYSIWYG without per-card render loops. |
| D21 | Brightness | Fades, glows and the new patterns are DirectComposition opacity animations; the CPU sets them only at events and step boundaries. | Near-zero CPU on two 4K displays. |
| D22 | GIF bulbs | Adding a GIF creates the bulb file and opens "Bulb Editing" (5.4). The editor previews with the faithful 5.4 decoder and warns when a GIF will look different from a standard viewer. | What you see while editing is what the desktop shows. |
| D23 | About | The 5.4 ABOUT banner with its 500 ms flashing strip; the Tiger Technologies LOGO is not shown; Tiger Technologies and every artist and arranger are credited by name. | Credit without implying endorsement. |
| D24 | Your files | Your bulbs, songs and pictures live in `Documents\Holiday Lights\Bulbs`, `\Music` and `\Pictures` (this supersedes the user-bulb path in `ARCHITECTURE.md` 4.2). | "Open My Bulbs Folder" should open somewhere people recognize. |
| D25 | Wording | Labels keep the 5.4 Title Case and words ("Bulb Drawing", "Flash Settings", "On Desktop"); every group and 5.4 control gets a one-line plain-language description under it, and new surfaces (Home, Welcome) use plain words. | Fans recognize every label; newcomers read what it means without opening Help. |

---------------------------------------------------------------------------------------------------------------------

## 1. Principles and target users

### 1.1 Who we design for

| User | Situation | "At home" means | Success measure |
|---|---|---|---|
| **The returning fan** (the commissioning user) | Used 5.4 for years. Remembers: click the red bulb, a menu opens; Holiday Lights Settings; drag bulbs into the boxes; Ctrl+Shift+B; Christmas 1. Two 3840x2160 monitors at 150 %, the second at negative X; 5.4 now draws into a third of one screen. | The same pages, words, flash patterns and rhythm; their themes; their screen saver working again; both monitors framed sharply; a one-click way back to the exact 2003 look. | First launch: both monitors framed correctly, at the right size, within 2 s; no question must be answered. |
| **The newcomer** | Wants festive lights now. Will not read anything. Uninstalls software that is noisy or in the way. | Lights appear by themselves and match the season; one obvious place to change them; silence until asked. | Lights within 2 s with zero clicks; another theme in 2 clicks from the tray; one bulb on the whole frame in 3 actions. |
| **The collector and bulb maker** | Browses the 1,550 bulbs, builds arrangements, turns GIFs into bulbs for family. | Search, categories, favorites, try-on, undo, the classic Bulb Editing dialog with flavors, export. | Finds "snowman" bulbs in under 5 s; never loses an arrangement to a mis-drop; files open in 5.4 too. |
| **Keyboard, screen-reader and photosensitive users** | Cannot or should not use drag and drop; may be harmed by fast flashing. | Every action has a keyboard path and a spoken result; a flash-rate limit that keeps the lights festive. | Any arrangement can be built without a mouse with Narrator or NVDA; flashing stays below 3 flashes per second when limited. |

Reference configuration for every example: the commissioning PC (Windows 11 26H2 build 26300; two 3840x2160 at
150 % = 144 DPI; primary at (0,0), second at (-3840,0); 72 px bottom taskbar on each; work area 3840x2088).

### 1.2 Principles (ranked; an earlier one wins a conflict)

1. **Lights first.** The lights appear before any window and power on with a short wave. Nothing blocks them. Every
   change shows immediately on the real desktop and in an exact preview.
2. **Same words, same places, same rhythm.** Every 5.4 term, label and access key survives where its function
   survives. Layout, the 60 ms tick, the five flash patterns and the GIF decoding are reproduced bit for bit.
3. **Better, and one click from 2003.** Glow, smooth fading, sharp high-DPI art, new patterns, seasonal themes and
   every-monitor framing are on by default; "Classic 2003" switches the look back in one click.
4. **Never in the way, never a surprise.** The lights never take clicks, focus or common shortcuts; they step aside
   for full-screen apps, games and presentations; nothing plays sound and nothing changes Windows settings unless
   the user asked.
5. **Instant and reversible.** Live apply, Undo/Redo, a Cancel that really restores, removed files that come back.
6. **Nothing lost silently.** Every 5.4 feature is kept, moved or explicitly retired with a reason (2.6, 2.7).
7. **Respect the art and the artists.** Bulbs are drawn as their artists drew them; every artist and arranger is
   credited; shipped and imported files are never modified.
8. **Festive, not cheesy.** Color and motion come from the original pixel art and from real light; the chrome is
   calm Windows 11 Fluent. One celebratory moment per occasion (first lights, a theme change, the About banner).
9. **Accessible by construction.** Full keyboard operation, UI Automation names and notifications, high contrast,
   reduced motion and a flash limit.
10. **Calm machine.** GPU composition, near-zero CPU, nothing runs while nobody can see it, no network access.

### 1.3 Vocabulary (5.4 terms kept unless marked new)

| Term | Meaning | Notes |
|---|---|---|
| Bulb | One kind of decoration (Standard Bulbs, Snow Family, an add-on `.bul`). | 5.4. |
| Built-In Bulb | One of the 49 bulbs that came inside Holiday Lights 5.4. | 5.4 ("Built-In Bulbs" filter). |
| Add-On Bulb | A bulb in a `.bul` file: the 1,501 bundled ones and any you add. | 5.4 ("Add-On Bulbs" filter). |
| My Bulbs | Add-on bulbs you added or made (they live in your My Bulbs folder). | New filter. |
| Edge / Corner | The 4 edges and 4 corners of a screen; together the **8 boxes** of the arrangement. | 5.4 help said "edge"; the Bulb Editing combo keeps its 5.4 words "Top Side" etc. |
| Arrangement | Which bulbs are in the 8 boxes: up to 6 bulb types per edge, 1 bulb per corner. | 6 on every edge is new (PO decision 4). |
| Flavor | An alternative picture of the same bulb on one edge (Standard Bulbs has 5 colors). | 5.4; used only in Bulb Editing and help. |
| Flash Pattern / Flash Speed | How and how fast the bulbs change. | 5.4. |
| Bulb Drawing | Where the bulbs are drawn: On Desktop (behind or in front of the icons) or On Top of all windows. | 5.4. |
| Theme | A saved set of bulb, music and screen saver settings (13 values). | 5.4. |
| Automatic themes | Theme changes on holidays ("Theme Calendar"). | New. |
| Music Box | The background music player. | 5.4. |
| Bulb Factory | The Settings page where you arrange bulbs. | 5.4 tab name. |
| Bulb Editing | The dialog where you edit a bulb you made, with its flavors. | 5.4 caption. |
| Look | How bulbs are drawn: pixel scaling, glow, fading. | New. |
| Glow | Soft real light around lit bulbs that brightens what is behind them. | New. |
| Try-on | Showing a bulb in the preview before using it. | New. |
| Target | The place a double-clicked bulb goes ("the whole frame", "the top-left corner"). | New. |

### 1.4 Measurable goals (each is an acceptance test in 7.5)

| # | Goal | Measured how |
|---|---|---|
| G1 | Fresh install: lights committed on every display <= 2 s after process start with a warm cache, <= 6 s cold; zero clicks. | Time to the first committed DirectComposition frame, logged. |
| G2 | Change the theme in <= 2 clicks from the tray (click icon, Themes, click theme). | Usability script. |
| G3 | Put one bulb on the whole frame in <= 3 actions from the tray (double-click icon, Bulb Factory, double-click a bulb). | Usability script. |
| G4 | Every MUST feature is operable by keyboard alone and announced correctly by Narrator and NVDA. | Accessibility pass (Appendix B). |
| G5 | At default settings with Settings closed: <= 0.5 % of one CPU core while animating, 0 DComp commits while paused or hidden, < 200 MB working set. | ETW / Task Manager on the reference PC. |
| G6 | No modal dialog at application start, ever. | Code review + test. |
| G7 | A 5.4 user's settings, themes, categories and added files are present after first start with no question asked. | Import tests with registry fixtures (golden `legacy-registry.json` and customized variants). |
| G8 | Layout fidelity: positions equal the golden layouts of 5.4 (`tests/HolidayLights.Tests/Golden/layout`); the classic patterns produce their film strips. | Golden tests. |

---------------------------------------------------------------------------------------------------------------------

## 2. Information architecture

### 2.1 Map of every surface

```
Holiday Lights (red bulb icon in the notification area)
|-- Tray menu ......................................................................... 2.2
|-- Holiday Lights Settings (window, left navigation) .................................. 2.3, 3.1-3.7
|     |-- Home                (new: the everyday hub)
|     |-- Bulb Factory        (5.4 tab 0: arrange bulbs)
|     |-- Music Box           (5.4 tab 1)
|     |-- Screen Saver        (5.4 tab 2)
|     |-- Themes              (in the place of the retired 5.4 tab 3 "Sharing")
|     |-- General             (5.4 tab 4)
|     '-- footer: Help, About Holiday Lights
|-- Dialogs over Settings: Bulb Editing, Edit Categories, New Category, Bulb Credits, Choose a Bulb, Save Theme,
|     Theme Calendar, Restore Built-In Themes, Import Details, Change Hot Key, Color, confirmations ..... 3.3, 3.6-3.8
|-- About Holiday Lights (window) ..................................................... 3.9
|-- Holiday Lights Help (window) ...................................................... 3.10
|-- Welcome card (first run; welcome back after a 5.4 import) ........................ 3.11
|-- On-screen pill (hot key feedback), notifications ................................. 3.12
|-- The lights: one layer per display ................................................ 5
'-- The screen saver: the same program as a per-user .scr (/s /p /c) .................. 6.2
```

### 2.2 Tray icon and tray menu

**Icon.** The 5.4 red Christmas bulb (group icon `ICON`) redrawn as a crisp multi-size icon (16, 20, 24, 32 px for the
tray; 16-256 px for the app). Two states, each with a light-taskbar and a dark-taskbar variant (chosen from
`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\SystemUsesLightTheme`, updated on
`WM_SETTINGCHANGE "ImmersiveColorSet"`):

| State | Icon | Tooltip (up to 3 lines) |
|---|---|---|
| Lights on (also while resting automatically, 5.12) | lit red bulb with a white highlight | "Holiday Lights" / "<theme>" + " - " + "behind your icons" \| "in front of your icons" \| "on top of all windows" \| "resting while a full-screen app is open" \| "resting during a presentation" / "Playing: <song>" (third line only while a song plays) |
| Lights off (user) | the same bulb unlit (dark red, grey highlight) | "Holiday Lights" / "Lights off" |

"<theme>" is the theme whose values equal the current settings, "Automatic: <theme>" while Automatic themes chose
it, or "Custom Settings".

**Mouse and keyboard** [MUST]:

* **One left or right click opens the menu at once**, on button release, at the cursor, never covering the taskbar
  (H.NotifyIcon `MenuActivation = LeftOrRightClick`, `NoLeftClickDelay = true`). This is 5.4's "click once (not
  twice)" (help T4).
* **Double-click opens Settings** (5.4 help T4): the first click opens the menu; the second click arrives as
  `WM_LBUTTONDBLCLK` (H.NotifyIcon `TrayMouseDoubleClick`), which closes the menu and opens Settings on its last
  page (first time: Home). Acceptance test 7.5 #11 verifies this on the reference PC.
* Keyboard (Win+B, arrows to the icon): Enter or Space opens Settings; Shift+F10 or the Menu key opens the menu.
* The icon is re-added after Explorer restarts (`TaskbarCreated`; 5.4 lost it).

**Menu** [MUST] (WPF `ContextMenu`, Fluent-styled, follows light/dark). Frequent actions first; every 5.4 item keeps
its exact label and access key.

```
+------------------------------------------------------+
| o~o~o~o~o~o~o~o~o~o   Halloween                       |   header: live strip of the active top edge
|                       Automatic theme                 |
+------------------------------------------------------+
| [x] Show Lights                                       |
+------------------------------------------------------+
| (o) Bulbs On Desktop                                  |
| ( ) Bulbs On Top of All Windows   Ctrl+Alt+Shift+B    |
+------------------------------------------------------+
|     Themes                                         >  |
| [ ] Play Holiday Music                                |
|     Next Song                                         |
+------------------------------------------------------+
|     Holiday Lights Settings...            (bold)      |
|     Holiday Lights Help                               |
|     About Holiday Lights                              |
+------------------------------------------------------+
|     Exit Holiday Lights                               |
+------------------------------------------------------+
```

| # | Item (exact) | Kind | Shortcut text shown | Action | Enabled |
|---|---|---|---|---|---|
| - | header | not focusable | | 120 x 20 DIP strip of the active arrangement's top edge with its corners, animated with the current pattern (frame 0 under reduced motion); line 1 the theme name or "Custom Settings"; line 2 "Automatic theme", "Chosen by you" or empty. | |
| 1 | "Show &Lights" | check | the "Turn the Lights On or Off" hot key, when enabled | Shows or hides the lights on every display (5.12.1). Music is not affected. Persisted. | always |
| 2 | "Bulbs On &Desktop" | radio (5.4) | the location hot key, shown on whichever radio is not selected | Bulb Drawing = On Desktop (behind or in front of the icons per "Behind the Desktop Icons", 3.2.8). | always |
| 3 | "Bulbs On &Top of All Windows" | radio (5.4) | (same rule) | Bulb Drawing = On Top. | always |
| 4 | "Th&emes" | submenu | | below | always |
| 5 | "Play Holiday &Music" | check | | The global music switch (6.1.1). Turning it on while the play mode is "Never" sets the mode to "Always". | always |
| 6 | "&Next Song" | command | | Starts another random checked song now (shuffle bag); during an "Intermittently" gap, starts the next song at once. | music switch on and a song is playing or waiting |
| 7 | "Holiday Lights &Settings..." | command, **bold default** (5.4) | | Opens Settings on its last page (first time: Home). | always |
| 8 | "Holiday Lights &Help" | command (5.4) | | Opens Holiday Lights Help on its Contents (3.10). | always |
| 9 | "&About Holiday Lights" | command (5.4) | | Opens About Holiday Lights (3.9). | always |
| 10 | "E&xit Holiday Lights" | command (5.4) | | Lights fade out (300 ms), music fades out (500 ms), the icon disappears, the program exits. An open Settings window keeps its changes; an open Bulb Editing dialog with unsaved changes asks "Discard Changes?" first. | always |

**Themes submenu** [MUST]:

* "&Automatic (Halloween Today)": check item mirroring "Change Themes Automatically on Holidays"; the label names the
  theme the calendar resolves for today ("Automatic (Classic Lights Today)" between holidays). Choosing it while
  unchecked turns Automatic themes on and applies today's theme.
* separator; one radio item per theme, A-Z (classic, new and your own themes together). The radio sits on the theme
  whose 13 values equal the current settings (none when the settings were changed after loading).
  Choosing a theme loads it (6.4.2) with the theme transition (4.4), records Recent Settings, and turns Automatic
  themes off.
* separator; "&Manage Themes..." opens Settings on Themes.

### 2.3 The Settings window

**Identity.** Caption "Holiday Lights Settings" (5.4). WPF built-in Fluent theme (`ThemeMode=System`), Mica, light or
dark following Windows, accent from Windows. Single instance: opening it again brings the existing window forward.

**Size and position** [MUST]. Default 1240 x 800 DIP, clamped to 92 % of the work area of the display that holds the
mouse pointer (a 1366 x 768 laptop gets about 1256 x 670). Minimum 760 x 560. Resizable and maximizable. Opens
centered on that display the first time; later at the remembered size, position and maximized state, re-clamped if
the display layout changed.

**Layout.**

```
+- Holiday Lights Settings ------------------------------------------------------------ [_] [ ] [X] -+
| o.o.o.o.o.o.o.o.o.o.o.o.o.o.o.o.o.o.o.o.o.o.o.o.o.o.o.o.o.o  (string of lights, 24 DIP, 4.3.1)    |
+-------------------+--------------------------------------------------------------------------------+
| [=]               |  <Page title>                          <page actions>  [Undo v] [Redo]          |
| (H) Home          |  <one-line description>                                                        |
| (B) Bulb Factory  |                                                                                |
| (M) Music Box     |  <page content, 3.1 - 3.7>                                                     |
| (S) Screen Saver  |                                                                                |
| (T) Themes        |                                                                                |
| (G) General       |                                                                                |
|                   |                                                                                |
| (?) Help          |                                                                                |
| (i) About Holiday |  Changes take effect right away.                    [  OK  ] [ Cancel ] [ Help ] |
|     Lights        |                                                                                |
+-------------------+--------------------------------------------------------------------------------+
```

**Navigation pane.** 220 DIP wide; below a window width of 1200 DIP it collapses to a 48 DIP icon rail (labels as
tooltips; the [=] button opens it as an overlay). Header: app icon + "Holiday Lights".

| Order | Page (exact) | Icon (Segoe Fluent Icons) | Shortcut | Origin |
|---|---|---|---|---|
| 1 | "Home" | Home (U+E80F) | Ctrl+1 | new |
| 2 | "Bulb Factory" | Lightbulb (U+E82F) | Ctrl+2 | 5.4 tab 0 "Bulb Factory" |
| 3 | "Music Box" | MusicNote (U+EC4F) | Ctrl+3 | 5.4 tab 1 "Music Box" |
| 4 | "Screen Saver" | TVMonitor (U+E7F4) | Ctrl+4 | 5.4 tab 2 "Screen Saver" |
| 5 | "Themes" | Color (U+E790) | Ctrl+5 | 5.4 Load/Save Theme dialogs; sits where tab 3 "Sharing" was |
| 6 | "General" | Settings (U+E713) | Ctrl+6 | 5.4 tab 4 "General" |
| footer | "Help" | Help (U+E897) | F1 | 5.4 "Help" button (opens the Help window on the current page's topic) |
| footer | "About Holiday Lights" | Info (U+E946) | | 5.4 tray item |

Ctrl+Tab / Ctrl+Shift+Tab cycle pages (5.4 tab keys); F6 moves focus between the navigation, the page and the bottom
bar.

**Page header** [MUST]. Title (Fluent Title, 28 DIP), one line of description in secondary text, and at the right the
page's own actions plus an **Undo** split button and a **Redo** button. Undo's tooltip names the step ("Undo: Use
Candy Canes for the whole frame"); its menu has "Undo All Changes Since This Window Opened". When the title, the page's
buttons and Undo/Redo don't fit on one row, the page's buttons move to a row under the header so the title stays
readable (review r1 #48).

**Bottom bar** [MUST] (window level, always visible): caption "Changes take effect right away." at the left; "OK",
"Cancel", "Help" at the right (5.4 buttons and order).

**Apply model** [MUST] (5.4 live apply, its bugs fixed):

* Every control applies at once: the lights, music and screen saver change immediately, and the settings file is
  written (atomically, debounced 500 ms).
* When the window opens, a snapshot of every setting in the Cancel scope is taken and the undo stack starts empty.
  The Cancel scope is everything except "Show Lights" and the music transport (pause, next song, play now).
  Changes made from the tray or by a hot key while the window is open are recorded like changes made in the window.
  Automatic theme changes never happen while the window is open (they wait until it closes, 5.11).
* **"OK"** closes the window and keeps everything.
* **The X button, Alt+F4 and tray "Exit Holiday Lights" also keep everything.** The changes are already on the
  desktop; keeping them is what the user sees. *Change from 5.4, where these behaved like Cancel.*
* **"Cancel"** restores the snapshot completely, then closes: arrangement, flash pattern **and** speed (5.4 bug
  fixed), Smooth Fading, Bulb Drawing and Behind the Desktop Icons, displays, size, look, songs, play mode, Play
  Holiday Music, volume, MIDI output, every screen saver value **including Background Color and the custom colors**
  (5.4 bug fixed), Show On, Smooth Motion, hot keys, startup, rest rules, accessibility options, Automatic themes and
  the calendar, favorites, hidden items, category overrides, and any theme loaded while the window was open (5.4).
  Cancel also **brings back everything removed in this session** (bulbs, songs, pictures, themes) from the holding
  folder. It **never deletes** anything added, created, saved, edited, renamed or imported in this session.
  Tooltip: "Closes this window and undoes every change made since you opened it. Things you added or saved are kept."
* **Esc never closes the window**: it clears the search box, cancels a drag and closes flyouts and dialogs, and a
  second Esc must never throw away a session. *Change from 5.4, where Esc meant Cancel.* Cancel has no access key.
* **Undo / Redo** (Ctrl+Z; Ctrl+Y or Ctrl+Shift+Z; not inside text boxes): up to 50 steps for the life of the
  window. Every applied change is one step, including file operations: adding a bulb, song or picture (undo moves the
  copy to the holding folder), removing one (undo brings it back), saving, renaming or deleting a theme, saving a bulb
  in Bulb Editing (undo restores the previous file). "Undo All Changes Since This Window Opened" undoes every step
  (it is Cancel without closing, except that it also undoes additions) and asks nothing.
* **Holding folder.** Removed or undone files are moved to `%LOCALAPPDATA%\Holiday Lights\Removed\<session>\` and are
  sent to the Recycle Bin (`IFileOperation` with recycle-on-delete) when the window closes. Files left behind by a
  crash are recycled at the next start.
* The first time the window is ever closed, a notification says "Your lights stay on" / "Holiday Lights keeps running
  in the notification area. Click the red bulb to come back." (3.12).

**Which page opens** [MUST]:

| Trigger | Page |
|---|---|
| Tray "Holiday Lights Settings...", tray double-click, Enter on the tray icon, starting the program while it runs | last page used (first time: Home) |
| Windows' screen saver "Settings" button (`/c`), legacy command `settings` | Screen Saver |
| Opening a `.bul` or `.gif` (double-click in Explorer, `--open`, dropping on the program) | Bulb Factory, new bulb selected |
| Tray Themes > "Manage Themes...", clicking a theme-change notification | Themes |
| Welcome card "Open Settings", Home "Change Bulbs..." | Home / Bulb Factory |

**Page skeleton** [MUST]. Groups are Fluent cards with Title Case headers (the 5.4 group boxes kept their names).
Under each group header (and under any control that had a 5.4 "What's This?" popup) a one-line description in
secondary text says what it does in plain words (Appendix A). Home, Music Box, Screen Saver, Themes and General scroll
as a whole; Bulb Factory fills the window and scrolls inside its columns. Status and problems appear as InfoBars at
the top of the page or group they concern; the only modal boxes are the confirmations listed in 3.8.

### 2.4 Windows and dialogs

| Window | Modal? | Opened from | Spec |
|---|---|---|---|
| Holiday Lights Settings | no | tray, second launch, `/c`, files | 2.3, 3.1-3.7 |
| Bulb Editing | modal over Settings | "Edit Bulb..." on a bulb you made; after adding a GIF | 3.3.1 |
| Edit Categories | modal | "Edit Categories..." on a built-in or someone else's bulb | 3.3.2 |
| New Category | modal over the editor | "New Category..." / "New..." | 3.3.2 |
| Bulb Credits | modal | context menus, selected-bulb bar | 3.3.3 |
| Choose a Bulb | modal | Screen Saver "Use a Bulb as the Animation..." | 3.8.4 |
| Save Theme | modal | "Save Settings As Theme..." | 3.6.3 |
| Theme Calendar | modal | Themes "Edit Holidays..." | 3.6.4 |
| Restore Built-In Themes | modal | Themes, General | 3.6.5 |
| Import Details | modal | General, welcome card | 3.8.5 |
| Change Hot Key | modal | General | 3.8.2 |
| Color | modal | Screen Saver | 3.8.3 |
| About Holiday Lights | no | tray, Settings footer | 3.9 |
| Holiday Lights Help | no | tray, Help button, F1 | 3.10 |
| Welcome card | no | first run; first run after a 5.4 import | 3.11 |

### 2.5 First run and later starts [MUST]

#### 2.5.1 Newcomer (no `HKCU\Software\Tiger Technologies\Holiday Lights`)

1. **Install** per user, no administrator rights (6.10). The installer's last page has "Start Holiday Lights"
   (checked).
2. **0-2 s after launch:** the tray icon appears and the lights are committed on every display with the
   **first-run power-up** (4.3.2): dark bulbs, a wave of light running clockwise, all bulbs bright for a moment, then
   the pattern. The theme is today's holiday theme from Automatic themes (on): on October 8 that is "Halloween"; on a
   day without a holiday it is "Classic Lights" (5.11). "Play Holiday Music" is off. Bulb Drawing is On Desktop,
   behind the icons.
3. **About 2.5 s after launch** (after the wave): the **Welcome card** (3.11) opens, centered on the main display.
   It never blocks the lights; closing it in any way keeps what it shows.
4. The Welcome card is shown once. It is never shown when Windows starts the program at sign-in.

#### 2.5.2 Holiday Lights 5.4 found, at its factory defaults (the commissioning PC)

"Factory defaults" means: every value under the 5.4 key equals its 5.4 default
(missing values count as defaults; `Path`, `Current Version`, `User`, `Serial Number` and `Last Converted Picture *`
are ignored), "Disabled Music" is empty, "Included Bulb Categories" is empty, and every subkey of `Themes` is one of
the 11 installer themes with its installer values. This is exactly the live registry of the commissioning PC
(golden `legacy-registry.json`).

1. The import (6.8) runs before the first frame and brings over everything (themes, hot key letter and state,
   custom colors, files). The 5.4 current settings are kept as a Recent Settings entry named "Holiday Lights 5.4
   Settings".
2. Because there is no personal choice to preserve (and on this PC 5.4 never drew correctly), the **newcomer look**
   applies: Automatic themes on (Halloween on October 8), "Play Holiday Music" off, "Automatically Start Holiday
   Lights" on (visible on the Welcome card), with the power-up wave.
3. The Welcome card opens in its "Welcome back!" variant (3.11) with: "Holiday Lights 5.4 was using its standard
   settings, so your lights now follow the holidays. Its 11 themes are included." and **"Use My 2003 Lights"**, which
   applies the 5.4 settings exactly (Christmas 1 bulbs, Flash Together every 300 ms, Snow and Santa Candle screen
   saver), turns Automatic themes off and turns "Play Holiday Music" on (5.4 "Always"); music starts when the card
   closes. It also shows the 5.4 leftovers with one-click fixes (broken screen saver, 5.4 still running, 5.4 starting
   with Windows).

#### 2.5.3 Holiday Lights 5.4 found, customized

1. The import (6.8) runs before the first frame and applies the 5.4 settings as they are: arrangement, flash
   pattern and speed, Bulb Drawing, songs and play mode, every screen saver value, hot key, custom colors, category
   overrides, startup state, files, and every theme under its own name.
2. Automatic themes stay **off** (fans never had them; one click turns them on). "Play Holiday Music" is **on**
   unless 5.4 was set to "Never"; the first song starts only after the Welcome card closes, so the music toggle on the
   card can be used first.
3. The lights come on with the fan's own arrangement and the first-run power-up.
4. The Welcome card opens in its "Welcome back!" variant: what was imported, the 5.4 leftovers with one-click fixes,
   "Show Import Details", "What's New in 6.0" and "Start Fresh Instead" (newcomer defaults: Automatic themes on, music
   off; the imported themes stay; the replaced settings go to Recent Settings).

#### 2.5.4 Every later start

| How started | Lights | Window |
|---|---|---|
| Sign-in (Run value, `--autostart`) | yes, with the short power-up (1.0 s), unless "Show Lights" is off | none |
| Start menu or any shortcut, not running | yes (short power-up) | none (5.4 behavior); the tray icon is there |
| Start menu while running | unchanged | Settings on its last page (5.4: second launch opens Settings) |
| Windows starts the screen saver (`/s`) | the saver (6.2) | none |
| Windows' screen saver "Settings" button (`/c`), not running | none (settings-only session: no lights, tray, hot keys or music; exits when the window closes - 5.4 `settings` mode) | Settings on Screen Saver |
| Double-click a `.bul` or open a `.gif` | yes | Bulb Factory with the new bulb selected (6.9) |

### 2.6 Every 5.4 feature and its new home

Status: **Kept** (same behavior and wording), **Improved** (same intent, better), **Changed** (different behavior,
reason given), **Fixed** (a 5.4 bug), **Revived** (dead 5.4 code made useful), **Moved**, **Replaced**, **Retired**
(2.7). Ids are 5.4 control, command and resource ids.

| # | 5.4 feature (id) | Status | New home | What changes and why |
|---|---|---|---|---|
| **Program and tray** | | | | |
| 1 | Tray icon, red bulb, tip "Holiday Lights" | Improved | Tray (2.2) | Sharp at every scale; lit/unlit states; light/dark taskbar variants; richer tooltip. |
| 2 | Left or right button-down opens TaskbarMenu | Kept | Tray (2.2) | Opens on button-up so a double-click can be recognized. |
| 3 | Double-click opens Settings (WM_LBUTTONDBLCLK) | Kept | Tray (2.2) | Closes the menu the first click opened, then opens Settings (5.4 usually just toggled the menu). |
| 4 | Menu 304 "Holiday Lights &Help" | Kept | Tray item 8 | Opens the new Help window. |
| 5 | Menu 303 "&About Holiday Lights" | Kept | Tray item 9 | New About window (3.9). |
| 6 | Menu 407 "Tell a &Friend..." | Retired | - | 2.7 #2. |
| 7 | Menu 404 "&Uninstall Holiday Lights" | Moved | General > Reset and Uninstall; Windows Settings > Apps | 2.7 #12. |
| 8 | Menu 402 "O&rder Form...", 403 "&Enter Serial Number..." | Retired | - | 2.7 #1. |
| 9 | Menu 406/405 "Bulbs On &Desktop" / "Bulbs On &Top of All Windows" (radio) | Kept | Tray items 2-3 | Exact wording; the hot key is shown on the item it would switch to. |
| 10 | Menu 401 "Holiday Lights &Settings..." (bold default) | Kept | Tray item 7 | Opens on the last page used. |
| 11 | Menu 106 "E&xit Holiday Lights" | Changed | Tray item 10 | An open Settings window keeps its changes (5.4 silently reverted them). |
| 12 | Hot key Ctrl+Shift+B toggles desktop/on top (505/506) | Changed | General > Hot Keys (3.7) | Same job and letter; default modifiers Ctrl+Alt+Shift; any combination; failures shown; on-screen pill. |
| 13 | Second launch opens Settings (FindWindow + WM_COMMAND 401) | Kept | Single instance (6.6) | Mutex + named pipe. |
| 14 | Command lines `open <file>`, `settings`, `reset`, `s <hwnd>`, `p <hwnd>` | Kept / Changed | Command line (6.6.3) | Legacy forms accepted; `s`/`p` replaced by `/s` `/p` handled by the same program; `reset` deletes 6.0 settings only. |
| 15 | `.bul` association written to HKCR on every start (needed admin) | Fixed | 6.9 | Per-user, written once, repaired if broken. |
| 16 | First-run "Welcome" help topic, repeated until the tray menu was opened | Improved | Welcome card (3.11) | Shown once; shows where the tray icon is. |
| 17 | Upgrade "What's New" help topics | Improved | Welcome back card + Help "What's New in 6.0" | |
| 18 | Low MIDI Volume prompt (`midiOutGetVolume(1)`, Sndvol32) | Replaced | Music Box InfoBar | 2.7 #6. |
| 19 | Payment Reminder, trial locks, saver trial banner | Retired | - | 2.7 #1. |
| 20 | Error message boxes ("Unexpected Holiday Lights Error", drawing error, "Help Files Missing", "Helper DLL Missing") | Changed | Log + InfoBars + at most one notification (6.6.5) | 2.7 #11. |
| 21 | Rebuild on WM_DISPLAYCHANGE | Improved | 5.2.3 | Also on scale, work area, taskbar, Explorer restart, displays added or removed. |
| 22 | Tray icon lost after an Explorer restart | Fixed | 2.2 | |
| 23 | "Automatically &Start Holiday Lights" (501; Startup-folder shortcut, applied on OK) | Changed | General > Startup Settings | Same label; per-user Run value; applied at once; Windows' own startup switch is shown. |
| 24 | `Path` value, `<system>\Holiday Lights.scr`, hidden `reset.mid` | Retired | - | 2.7 #14. |
| 25 | Helper DLL, "Slow Bulb Reminder", SLOW badge, "Prevent Slow Bulb Warning" | Retired | - | 2.7 #7. |
| 26 | 256-color palette code, `SetPriorityClass(HIGH)` around drawing | Retired | - | 2.7 #10. |
| 27 | Animation frozen while a mouse button is held | Fixed | 5.5 | |
| **Settings window** | | | | |
| 28 | Modeless single-instance "Holiday Lights Settings" | Kept | 2.3 | Resizable; per-monitor DPI; remembers size, position and page. |
| 29 | Tabs Bulb Factory, Music Box, Screen Saver, Sharing, General | Kept / Changed | Navigation | Same names and order; "Home" added first; Sharing replaced by Themes (2.7 #3-4). |
| 30 | OK (1) / Cancel (2) / Help (51) | Kept | Bottom bar | Cancel restores everything; X and Exit keep changes; Esc does not close (2.3). |
| 31 | Live apply with a Cancel snapshot; Cancel left flash settings and background color unreverted | Fixed | 2.3 | Full snapshot; removed items come back; Undo/Redo added. |
| 32 | "?" button and 57 What's This popups | Replaced | Descriptions, tooltips, F1 (Appendix A) | WinHelp is not part of Windows 11; the texts survive. |
| 33 | Static 60 "Click the Sharing tab to share your new bulbs with the world!" | Changed | InfoBar after saving a bulb | Points to "Export Bulb File...". |
| 34 | Rescan of the music and picture folders on WM_ACTIVATE | Improved | Folder watchers (6.1, 6.2) | Lists update live. |
| **Bulb Factory tab** | | | | |
| 35 | Bulb list 100 (32x32 icon, bold name, description; 4 rows) | Improved | Bulb List beside the frame (3.2.5) | Tiles and a "Details" view that reproduces the 5.4 row; search, favorites, animated previews. |
| 36 | Eight boxes 101-108 around the list; top/bottom 6, left/right 5, corners 1 | Changed | Frame editor around the live preview (3.2.2) | 6 bulb types on every edge (PO decision 4); boxes surround a picture of the screen (D4). |
| 37 | Drag list to an edge inserts at the drop point; a full edge overwrites the bulb under the cursor | Kept | 3.2.7 | A full edge accepts a drop only on a chip, which is then replaced (the highlight shows it first). |
| 38 | Box to box copies | Kept | 3.2.7 | Shift moves. |
| 39 | Box to the list removes | Kept | 3.2.7 | Also outside the frame, the Delete key and the chip's remove button. |
| 40 | Corner drop replaces | Kept | 3.2.7 | |
| 41 | No reordering inside a box | Improved | 3.2.7 | Drag within a box, or Ctrl+arrows. |
| 42 | Hand cursor over draggable bulbs (missing on the first list row) | Fixed | 3.2.7 | |
| 43 | BulbMenu "Show" > All Bulbs (1006), Built-In Bulbs (1007), Add-On Bulbs (1008), categories (2000+) | Kept | "Show" filter + context menu "Show" (3.2.5) | Adds Favorites, In Use, My Bulbs, Removed Bulbs; still resets to All Bulbs when Settings opens; still offered on empty space. |
| 44 | BulbMenu 1002 / button 118 "Edit Bulb..." / "Edit Categories..."; double-click a row opened the editor | Kept / Changed | Selected-bulb bar, context menu | Double-click now uses the bulb (D5); Edit stays one click away. |
| 45 | BulbMenu 1003 "&Remove Bulb" (deleted the file; arrangement not saved) | Fixed | Context menu | Your bulbs go to the holding folder then the Recycle Bin; bundled bulbs are hidden (restorable); the arrangement is updated and saved. |
| 46 | BulbMenu 1004 "&Send to Tiger Technologies..." | Replaced | "Export Bulb File..." | 2.7 #4. |
| 47 | BulbMenu 1001 "Bulb &Credits" (COPYRIGHT dialog) | Kept | 3.3.3 | Adds the picture and the source; credits also shown in the selected-bulb bar. |
| 48 | Dead command 1005 (fill every slot with one bulb) | Revived | "Use Everywhere" and the "Whole Frame" target | |
| 49 | "&Add Bulb..." 117 (.bul/.gif); only the first dropped file; same-name files refused | Improved | Bulb List toolbar; drop anywhere (3.2.10) | All dropped files; name clashes renamed; duplicates recognized by content. |
| 50 | Instruction texts 109/110 | Kept | Bulb Factory description line | Rewritten for the new geometry. |
| 51 | "Bulb Drawing" 116: "On &Desktop" 119 / "On &Top" 120 | Kept | Bulb Factory and Home (3.2.8), General details (3.7) | Adds "Behind the Desktop Icons" (layer modes, 5.1). |
| 52 | "Flash Settings" 111: combo 112 (5 patterns), trackbar 113, "Slow"/"Fast" 114/115 | Kept | Bulb Factory and Home (3.2.8) | Exact mapping; 4 new patterns after the classic five; "Smooth Fading". |
| 53 | "Load Theme..." 701 / "Save Settings As Theme..." 702 on three tabs | Kept | Bottom-left of Bulb Factory, Music Box, Screen Saver | "Load Theme..." now opens the Themes page. |
| 54 | Locked (trial) bulbs dimmed | Retired | - | 2.7 #1. |
| 55 | Damaged bulb: WARNING picture, "This bulb is damaged and may cause problems."; start-up box offering deletion | Kept / Changed | 3.2.9 | Same picture and text; no start-up box; never deleted automatically. |
| **Bulb Editing (BULBEDIT), Edit Categories, New Category** | | | | |
| 56 | Name 100, Description 101, Author 102, Copyright 103 (79 characters) | Kept | 3.3.1 | Same labels and limits. |
| 57 | Categories 300, "New Category..." 301 (NEWCATEGORY: 39 characters, letter or digit, "\|" becomes a space) | Kept | 3.3.1, 3.3.2 | An existing name is checked instead of ignored. |
| 58 | Slot combo 200 ("Bulb List Preview", "Top Side", ...), flavor combo 201 ("Flavor 1"-"Flavor 8", " *") | Kept | 3.3.1 | Adds a 3x3 slot map. |
| 59 | Preview 202 animated at the flash speed; white 32x32 square over a DITHER-dimmed frame | Kept | 3.3.1 | Adds zoom, play/pause, frame stepper, arrow-key nudging. |
| 60 | "Change..." 203 (GIF; de-duplicated; 37 animations max), "Remove Flavor" 204 (flavors 2-8) | Kept | 3.3.1 | A GIF can also be dropped on the preview. |
| 61 | Texts 205/206 per slot | Kept | 3.3.1 | Verbatim. |
| 62 | Save/Cancel via `tempedit.tmp`; compaction; writes into `.bul` files | Changed | 6.3 | Atomic write of a new compacted file; bundled and imported files never written. |
| 63 | "Edit Categories" (BULBEDITCATEGORIES) for built-in and downloaded bulbs | Kept | 3.3.2 | Categories stored in settings, never in files. |
| 64 | A GIF becomes a new bulb file and the editor opens; author from RegisteredOwner and mail-client keys | Kept / Changed | 3.2.10 | Author defaults to the Windows account display name. |
| 65 | Faithful 5.4 GIF decoding | Kept | 5.3.4 | PO decision 3. |
| **Music Box tab** | | | | |
| 66 | Song list 200 with check boxes, sorted by upper-cased name | Kept | 3.4.3 | Same order; adds length, type and arranger columns. |
| 67 | Click a song name = play now | Changed | Play button, double-click, Enter | A single click no longer starts music. |
| 68 | MUSICMENU "&Play Now" (1201) / "&Remove Song" (1202) | Kept | Row context menu | Your songs go to the holding folder then the Recycle Bin; bundled songs are hidden; adds "Show in Folder". |
| 69 | "Add &Song..." 201 copies into the music folder; dropped files; `.lnk` shortcuts | Kept | 3.4.3 | Multiple files; name clashes renamed; a shortcut plays with the engine of its target (5.4 quirk fixed). |
| 70 | "Music Volume" group 212-214 (Sndvol32) | Replaced | Volume slider (3.4.2) | 2.7 #6. |
| 71 | "Play the Chosen Songs" 203, radios 204-208 | Kept | 3.4.4 | Exact labels and access keys; the new "Play Holiday Music" switch gates them (D8). |
| 72 | Shuffle bag, 1 s / 60-180 s gaps, first song at once | Kept | 6.1.2 | The "Intermittently" pause is now honored when tabs change (5.4 bug). |
| 73 | `reset.mid` before every MIDI song (2 s freeze) | Fixed | 6.1.3 | Reset messages sent directly. |
| 74 | Music errors shown only while Settings is open | Changed | InfoBars + tray tooltip (3.4.6) | |
| 75 | Trial-locked songs | Retired | - | 2.7 #1. |
| **Screen Saver tab and the saver** | | | | |
| 76 | "Windows Display Properties" 300, texts 301/302, "Open &Display Properties" 303 | Changed | Status card (3.5.2) | "Use Holiday Lights as My Screen Saver", "Start After", "Stop Using It", repair of the broken 5.4 entry, "Windows Screen Saver Settings...". |
| 77 | "Text Message" 304: edit 305 (255 characters; leading spaces removed), "Clear" 306, "Format &Text..." 307 (font, 18-100 pt, effects, color) | Improved | 3.5.4 | Inline font, size, bold, italic, underline, strikeout and color. |
| 78 | "Animation" 308: list 309 (25 + add-on bulbs), "Style" 310/311 | Kept | 3.5.5 | Animated tiles; "Use a Bulb as the Animation..."; style rules exact. |
| 79 | "Background Picture" 312: list 313, Center/Tile/Stretch 314, "&Add..." 315, "&Background Color..." 317 (16 custom colors), menu Preview/Remove | Kept | 3.5.6 | Thumbnails; PNG added; bundled pictures hidden instead of deleted. |
| 80 | "&Preview Screen Saver" 316, double-click 309/313, menu 1101 (each silently made Holiday Lights the system saver) | Fixed | 3.5.3 | Preview never changes Windows settings. |
| 81 | Saver drew only on the primary monitor | Improved | 3.5.7 | Every display; "Main Display Only" reproduces 5.4. |
| 82 | Saver sprites frozen with "Don't Flash" | Fixed | 6.2.3 | |
| 83 | Saver trial banner | Retired | - | 2.7 #1. |
| 84 | `.scr` stub: `s`/`p` hand-off, "Sorry; can't find Holiday Lights..." text, Win9x passwords and `/a` | Replaced / Retired | 6.2 | 2.7 #14, #20. |
| 85 | Configure (`/c`) opens Settings on the Screen Saver tab; settings-only session | Kept | 2.5.4, 6.2.1 | |
| 86 | JPEG converted to `%TEMP%\Holiday Lights Bitmap.bmp`, "Last Converted Picture" values | Retired | - | 2.7 #21. |
| **Sharing tab** | | | | |
| 87 | "Downloading New Bulbs" link 408 | Replaced | Bundled 1,501 bulbs, "Add Bulb...", "Open My Bulbs Folder" | PO decision 2. |
| 88 | "Uploading Your Own Bulbs" list 403, "&Send Bulb..." 407, Bulb Uploading (with copyright-status radios), Sending Bulb | Retired | "Export Bulb File..." | 2.7 #4, #19. |
| **General tab** | | | | |
| 89 | "Startup Settings" 500-502 | Kept / Changed | General > Startup Settings | See #23. |
| 90 | "Bulb Drawing Hot Key" 503-506 ("Use &Hot Key: Ctrl + Shift +" + letter) | Changed | General > Hot Keys | See #12. |
| **Themes** | | | | |
| 91 | Save Theme (THEMESAVE: 63 characters, pre-filled with the last name, "Replace Theme?") | Kept | 3.6.3 | Same texts. |
| 92 | Load Theme (THEMELOAD: list, Delete, "Remove This Theme?") | Improved | Themes page (3.6) | Cards with previews; delete is undoable. |
| 93 | Theme contents: exactly 13 values | Kept | 6.4.1 | |
| 94 | 11 installer themes | Kept | 6.4.3 | Exact names and values; 8 new themes added. |
| 95 | Loading deleted values the theme lacked | Fixed | 6.4.2 | A missing value gets its default. |
| 96 | Theme names containing "\" created nested keys | Retired | - | 2.7 #16. |
| **About, help, files** | | | | |
| 97 | About box: ABOUT banner, ABOUTFLASH strip every 500 ms, LOGO, "version 5.4", tigertech link, copyright text, "Order Form" | Changed | About window (3.9) | Banner and strip kept; "Modern Edition 6.0"; credits; the creator and a link to the project's home page; no LOGO or order form. |
| 98 | WinHelp `hlights.hlp` (94 topics in 7 books) | Replaced | Help window (3.10) | Same books, payment book removed, topics updated. |
| 99 | "Holiday Lights Help" Start menu shortcut, "Tell a Friend" `.url` | Retired | - | 2.7 #13. |
| 100 | "Included Bulb Categories" registry values | Moved | Settings (`bulbs.categoryOverrides`) | Imported. |
| 101 | "Custom Color 0"-"15" | Kept | Color dialog | Imported. |
| 102 | Region caches, `categ:` records and flags written into `.bul` files | Retired | - | 2.7 #15. |
| 103 | E-mail defaults read from Outlook Express/Netscape keys (creating foreign keys) | Retired | - | 2.7 #18. |
| 104 | Quarter-size art for the Display Properties preview | Retired | - | 2.7 #17. |
| 105 | "OR fringe" and On-Top clipping of Party Hats / Pastel Easter Eggs | Retired | - | Artifacts of GDI masks, not of the art. |
| 106 | Unused resources (LIGHTS accelerators, LIGHTS/WIN95 menus, DEBUGGING dialog) | Not applicable | - | Never reachable in 5.4. |

### 2.7 Retired features and why

| # | Retired | Why | What the user gets instead |
|---|---|---|---|
| 1 | Order Form wizard, Enter Serial Number, Payment Reminder, trial locks of bulbs, songs and animations, trial banner | PO decision 1: free and fully unlocked. | Everything unlocked. |
| 2 | "Tell a Friend..." and its Start menu shortcut | The web page is gone; PO decision 1. | "Export Bulb File..." to share bulbs. |
| 3 | Sharing tab "Downloading New Bulbs" (link to tigertech.com) | The site is gone; PO decision 2. | All 1,501 add-on bulbs bundled and searchable; "Add Bulb...", drag and drop, double-click a `.bul`. |
| 4 | Bulb uploading ("Send to Tiger Technologies...", Bulb Uploading, Sending Bulb) | The upload server is gone; nothing can receive the file. | "Export Bulb File..." saves a copy to e-mail or post anywhere. |
| 5 | Links to tigertech.com in About and help | PO decision 1; no network access. | About credits Tiger Technologies by name. |
| 6 | "Music Volume" button and the first-run "Low MIDI Volume" prompt | `Sndvol32.exe` no longer exists; the prompt read MIDI device 1, which does not exist on modern PCs. | A real volume slider, a "Windows Volume Mixer" link, and an InfoBar when Holiday Lights is muted in the mixer. |
| 7 | Helper DLL, "Slow Bulb Reminder", SLOW badge | Per-pixel-alpha composition draws shape-changing bulbs correctly at no extra cost; a 32-bit hook cannot load into 64-bit Explorer. | Nothing to worry about. |
| 8 | "Open Display Properties" | Renamed in Windows. | "Windows Screen Saver Settings..." opens the same dialog. |
| 9 | What's This ("?") button | WinHelp is not part of Windows 11. | Visible descriptions, tooltips with the same texts, F1. |
| 10 | 256-color palette handling, high-priority drawing | No 256-color displays remain; priority boosts harm other apps. | - |
| 11 | Modal error boxes naming support@tigertech.com ("Unexpected Holiday Lights Error", "Help Files Missing", "Helper DLL Missing") | Modal boxes interrupt work; the support address and the help file are gone. | A log file, inline InfoBars and at most one notification per session (6.6.5). |
| 12 | "Uninstall Holiday Lights" in the tray menu | Clutters the most-used menu and invites accidents; Windows lists every app in Settings > Apps. | General > Reset and Uninstall > "Uninstall Holiday Lights...". |
| 13 | "Holiday Lights Help" Start menu shortcut | Help is inside the program. | Tray > "Holiday Lights Help". |
| 14 | `Path` registry value, system-folder `.scr`, hidden `reset.mid`, the `s`/`p` hand-off | The screen saver is the program itself; MIDI reset is sent as messages. | - |
| 15 | Writing region caches, categories and flags into `.bul` files | Shipped and downloaded files must stay pristine; a failed write could ruin a bulb. | Category edits are stored in settings; Bulb Editing writes a new file. |
| 16 | Theme names containing "\" | Registry artifact (nested keys). | Names may not contain \ / : * ? " < > \|. |
| 17 | Quarter-size bulb art for the tiny Display Properties preview | Had data bugs (North Pole Express threw, Party Hats offset). | The tiny preview scales the normal art. |
| 18 | E-mail lookup in Outlook Express / Netscape registry keys for author defaults | Obsolete; it even created foreign registry keys. | Author defaults to the Windows account display name. |
| 19 | Copyright-status radios of "Bulb Uploading" | Only meaningful for the dead upload. | The copyright field stays in Bulb Editing and is shown in Bulb Credits. |
| 20 | Win9x screen saver passwords (`/a`, PASSWORD.CPL) | Windows handles "On resume, display sign-in screen". | "Windows Screen Saver Settings..." link. |
| 21 | JPEG temp BMP cache and "Last Converted Picture" values | Pictures are decoded directly (WIC). | - |
| 22 | Payment-era help topics (Paying..., Serial Number, Contacting Tiger Technologies) | Free product, no support channel. | A "Credits" chapter. |

---------------------------------------------------------------------------------------------------------------------

## 3. Pages and windows in detail

### 3.0 Shared components

#### 3.0.1 Control kit [MUST]

WPF's built-in Fluent theme (`ThemeMode=System`) styles the standard controls. The controls below are not in it and
are built once, in `HolidayLights.App`, as templated WPF controls with `BasedOn` the Fluent styles, Fluent system
brushes (light, dark and High Contrast), AutomationPeers and full keyboard support. WPF-UI and other theme libraries
are not used (they replace `ThemeMode=System`'s resources).

| Control | Built from | Behavior |
|---|---|---|
| NavigationPane | `ListBox` | Icon + label items; footer items; collapses to a 48 DIP rail with tooltips; arrow keys move, Enter/Space selects. |
| PageHeader | `Grid` | Title, description, action area, Undo split button, Redo. |
| Card | `Border` | `CardBackgroundFillColorDefault`, 8 DIP radius, 16 DIP padding, Title Case header (BodyStrong 14) + optional description. |
| InfoBar | `ContentControl` | Severity Informational / Success / Warning / Error (Fluent glyph and colors), one sentence, at most one action button or link, optional close button. Placed at the top of the page or card it concerns; announced as a polite live region. |
| Snackbar | `Popup` in the page | Bottom center of the page content, 48 DIP high, max 560 DIP wide, inverted surface, text + one action ("Undo", "Show") + close. Visible 6 s, paused while hovered or focused. One at a time; a new one replaces the old. Polite live region. |
| ContentDialog | overlay `Grid` in the owning window | Dims the window, centered card with title, text, buttons. Enter = primary; Esc = Cancel; a destructive button is never the default. |
| Flyout, TeachingTip | `Popup` | Anchored, light-dismiss, Esc closes. TeachingTip adds an arrow, a title and a close button. |
| ToggleSwitch | `CheckBox` template | Track + thumb, "On"/"Off" text; Space toggles. |
| NumberBox | `TextBox` + spin buttons | Range validation, Up/Down = 1, PgUp/PgDn = 10, invalid text reverts on focus loss. |
| SegmentedControl | `RadioButton` group | Joined buttons ("Look", "Tiles/Details"); arrow keys move. |
| SplitButton | `Button` + menu | Undo with its menu. |
| HotKeyRecorder | `Button` | Shows key caps ("Ctrl" "Alt" "Shift" "B"); recording state (3.8.2). |
| TileGrid | `vwp:GridView` (VirtualizingWrapPanel 2.5.4) | Recycling virtualization, uniform spacing, one tab stop, arrow/Home/End/PgUp/PgDn navigation. |
| NightWell | `Border` | The dark gradient behind bulb art (4.2). |
| LightStage | `Image` + `WriteableBitmap` | The live preview (3.0.2). |
| FontPicker | virtualized `ComboBox` | Every installed family, each name drawn in its own face, type-to-search. |

#### 3.0.2 Light stage (live preview) [MUST]

One control renders every preview: Home, Bulb Factory, theme cards, the Welcome card, Bulb Editing's edge sample and
the screen saver preview.

* **Geometry.** A real display (physical size, DPI, work area, taskbar position from `GetMonitorInfo`) or, for theme
  cards and the Welcome card, the main display. The real layout engine (5.3) computes every position **in that
  display's physical pixels at its real scale and bulb size**; the stage then scales the whole frame uniformly to
  fit. Counts, gaps, truncation, alignment, corners and flavor cycling therefore match the desktop exactly.
* **Zoom (PO decision 8).** The stage may show part of the display at a legible size instead of fitting all of it, with
  the same layout and pattern: theme cards show the top-right corner at 0.4 of real size (`StageZoom` Auto/Corner; the
  cards' date and Current badges sit at the top-left); the Welcome cards show the main display's top-left corner the
  same way; Home's stage of every display adds a magnified top-left corner over each enabled display whenever its bulbs
  would be under 12 px; a try-on zooms the Bulb Factory stage into its target's corner. The Screen Saver page preview
  shows an enlarged top-left inset (3.5.3).
* **Backdrop.** The display's wallpaper (`IDesktopWallpaper::GetWallpaper(monitorId)` with its position mode; a
  slideshow shows its current image), else the background color, else (Spotlight or error) the night gradient
  (4.2). The taskbar area is drawn as a band (#202020 at 85 % opacity) so people see why the bottom edge sits where
  it does. Icons and windows are not drawn. Bezel: 1 DIP #3A3A3A outline, 6 DIP radius.
* **Rendering.** `CpuCompositor` (ARCHITECTURE 4.5) composites into a `WriteableBitmap` at the stage's device
  resolution: premultiplied source-over for bulbs and **additive** blending for glow, using the same sprites as the
  desktop pre-scaled to the stage scale by area averaging. It uses the same pattern engine, brightness model and step
  counter as the desktop, so the stage and the desktop blink in unison. It redraws once per flash step and at most
  30 times per second while fades run; it stops when the stage is not visible or the window is minimized, and while
  the lights rest because nobody can see the screen (session locked, display off, user away); it draws the current
  frame on resume.
* **Overlays.** Try-on pill "Trying On: <bulb> - <target>" (top-left); box highlight (2 DIP accent outline of the
  matching strip or corner while a box is hovered or selected, 1 s after selecting); "The lights are turned off." +
  "Turn On the Lights" (stage dimmed to 50 %); "No bulbs yet." (empty arrangement); "Not shown on this display"
  (display unchecked); "Preview unavailable - reconnecting" (graphics device lost).
* **Reduced motion.** Frame 0 of every bulb, lit, no try-on animation; the Bulb Factory and Home stages keep
  stepping with the pattern because they are the requested preview.

#### 3.0.3 Bulb art in the UI [MUST]

* Every picture of a bulb sits on a **night well** (4.2) in light and dark mode, so lit colors, glow and black
  cords read correctly.
* Art is scaled with the current "Pixels" style (MMPX + area averaging, or Crisp, 5.3.3) to the device pixels of
  the control; never bilinear (`RenderOptions.BitmapScalingMode=NearestNeighbor` on pre-scaled bitmaps). Tiles never
  enlarge art more than 4x.
* Animation in tiles, chips, cards and the tray header uses **one shared UI timer** that ticks with the flash step
  period but never faster than every 150 ms, advances only realized (visible) items, and stops while the Settings
  window is inactive or minimized. Theme cards and Home theme cards show frame 0 and animate only while hovered or
  focused. Under reduced motion every item shows frame 0 (lit) and animates only while hovered or focused.
* Damaged or undecodable art is drawn as the 5.4 `WARNING` bitmap (32 x 32).

#### 3.0.4 Page conventions [MUST]

* **Descriptions and tooltips.** Each group and each control that had a 5.4 "What's This?" popup shows its updated
  text as a visible description or, where space is short, as a tooltip after 500 ms (Appendix A). F1 opens Help at
  the topic of the focused control's page section.
* **Screen readers.** `AutomationProperties.Name` equals the visible label; composite items (boxes, chips, tiles,
  cards) have the names given in their sections; every arrangement, theme and song change raises a UIA notification
  (polite) that states the result ("Candy Canes now on the whole frame.").
* **Focus.** Fluent focus visuals (2 DIP); Tab order = reading order; lists and grids are single tab stops.
* **Access keys.** Where this spec marks one with `&`, use it (those keys are unique within their page or menu and
  keep every 5.4 key). Every other labeled control gets a unique access key within its page, preferring the first
  free letter of its label.

### 3.1 Home [MUST]

Purpose: everything most people ever change, on one screen, with the real lights in view.

```
Happy Halloween!                                                         [Peek]  [Undo v] [Redo]
Halloween - Automatic theme until Oct 31
+------------------------------------------------------------------------------------------------+
|   +-----------------------------+ +-----------------------------+                               |
|   | o o o o o o o o o o o o o o | | o o o o o o o o o o o o o o |   every display at its real   |
|   | o        Display 2        o | | o     Display 1 (Main)    o |   position: wallpaper,        |
|   | o o o o o o o o o o o o o o | | o o o o o o o o o o o o o o |   taskbar band, live lights   |
|   +-----------------------------+ +-----------------------------+                               |
+------------------------------------------------------------------------------------------------+
+Lights ------------------------------------------------------------------------------------------+
| Show Lights                                                                       [ On  (o)]    |
| Your lights are on behind your desktop icons on 2 displays. Next: Thanksgiving on Nov 1.       |
+------------------------------------------------------------------------------------------------+
Choose a Theme                                                                   [All Themes...]
+--------+ +--------+ +--------+ +--------+ +--------+ +--------+ +--------+ +--------+
|preview | |preview | |preview | |preview | |preview | |preview | |preview | |preview |
|Auto-   | |Hallo-  | |Thanks- | |Christ- | |New Year| |Classic | |Christ- | |Holiday |
|matic   | |ween    | |giving  | |mas 1   | |        | |Lights  | |mas Twi.| |Party   |
+--------+ +--------+ +--------+ +--------+ +--------+ +--------+ +--------+ +--------+
+Flash Settings -----------------------------+ +Bulb Drawing ------------------------------------+
| [Flash Together                        v]  | | (o) On Desktop     Below all your windows.       |
| Slow |--+--+--+--o--+--+--+--| Fast        | | ( ) On Top         Above all windows.            |
| One step every 0.30 s   [x] Smooth Fading  | |                                                  |
+--------------------------------------------+ +--------------------------------------------------+
+Bulb Size ---------------------------------------------------------------------------------------+
| ( ) Small   (o) Standard   ( ) Large   ( ) Extra Large       Standard Bulbs are 48 px tall here. |
+------------------------------------------------------------------------------------------------+
+Music -------------------------------------------------------------------------------------------+
| Play Holiday Music                                                                [ Off ( )]    |
| Music is off.                                                                    [Music Box...] |
+------------------------------------------------------------------------------------------------+
[Change Bulbs...]   [Screen Saver...]
```

| Element | Content and behavior |
|---|---|
| Title | The active settings' screen saver message (first line, at most 40 characters), else "Holiday Lights". Christmas 1 gives "Merry Christmas!", Halloween "Happy Halloween!". |
| Subtitle | "<theme> - Automatic theme until <end date>" / "<theme> - chosen by you" / "Custom settings" (the current values match no theme). |
| "Peek" | 3.2.3. |
| Light stage | All enabled displays at their real relative positions and relative sizes, full content width x 240 DIP; disabled displays drawn as outlines captioned "No lights". Clicking a display opens Bulb Factory previewing that display. Accessible name "Preview of your lights"; HelpText summarizes the arrangement. |
| "Show Lights" toggle (card "Lights") | Same setting as tray "Show Lights". Turning it on plays the short power-up (4.3.2). |
| Status line | First matching row of the status table below; at most one action link. |
| "Choose a Theme" | Eight cards (152 x 124 DIP: 144 x 81 preview on the main display's geometry and wallpaper, static frame 0; name; small date line for calendar themes "Oct 1 - Oct 31"). Card set, de-duplicated, in this order: "Automatic" (always first; shows today's theme, a calendar badge and "Halloween today"); the current theme; today's calendar theme; the next three different checked calendar themes; "Christmas 1"; the two themes loaded most recently; then "Classic Lights", "Christmas Twinkle", "Holiday Party" until there are eight. Selected card: 2 DIP accent border + check badge. Hover or focus for 300 ms: the Home stage shows that theme with the pill "Preview: Thanksgiving" (the desktop does not change) and the card animates. Click or Enter: loads the theme on the desktop (theme transition, Recent Settings, snackbar "Thanksgiving theme loaded." [Undo]) and turns Automatic themes off; the Automatic card turns them on. Shift+F10: "Load", "Manage Themes...". |
| "All Themes..." | Opens Themes. |
| "Flash Settings", "Bulb Drawing" | The same groups as on Bulb Factory (3.2.8), bound to the same settings, except that Home's Bulb Drawing shows only the two radios ("On Desktop", "On Top"); behind or in front of the icons is chosen on Bulb Factory and General. |
| "Bulb Size" | Radios "Small", "Standard", "Large", "Extra Large" (75, 100, 150, 200 % of each display's scale, 5.3.2); caption "Standard Bulbs are 48 px tall here." computed for the main display (tooltip lists every display). Same setting as General > Bulb Look. |
| "Music" card | "Play Holiday Music" toggle (the global music switch, 6.1.1) and one status line: "Music is off." / "Playing: <song>" + "Pause" + "Next Song" / "Next song in 1:24" / "Music plays only while the screen saver is on." / "No songs are checked, so no music will play." + "Music Box..." (opens Music Box). |
| "Change Bulbs...", "Screen Saver..." | Open Bulb Factory / Screen Saver. |

**Status line** (first matching row wins):

| Condition | Text | Action link |
|---|---|---|
| Lights off | "The lights are off." | "Turn On" |
| No display enabled | "No display is set to show lights." | "Choose Displays" (General) |
| Arrangement empty | "No bulbs yet." | "Change Bulbs" |
| No graphics device | "Your lights can't be drawn right now (no graphics device). Holiday Lights keeps trying." | - |
| Remote Desktop session | "The lights rest during Remote Desktop sessions." | - |
| Every enabled display has a full-screen app | "Resting while a full-screen app is open." | - |
| Some displays resting | "Resting on Display 2 while a full-screen app is open." | - |
| Presentation | "Resting during your presentation." | - |
| Energy Saver rule active | "Using less power while Energy Saver is on." (or "Not flashing while Energy Saver is on.") | "Change" (General) |
| Fallback to in front of the icons | "Windows isn't letting Holiday Lights draw behind the icons right now, so the bulbs are in front of them. Holiday Lights keeps trying." | "Try Again" |
| Fallback to on top | "The desktop isn't available right now, so the bulbs are on top of your windows. Holiday Lights keeps trying." | "Try Again" |
| Otherwise | "Your lights are on " + "behind your desktop icons" / "in front of your desktop icons" + " on <n> displays."; On Top reads "Your lights are shining on top of all windows on <n> displays." (review r1 #79); + (Automatic) " Next: <theme> on <date>." | - |

Keyboard: Tab order header actions, stage (Enter opens Bulb Factory), Lights toggle, theme cards (one tab stop;
arrows move; Enter loads), "All Themes...", Flash Settings, Bulb Drawing, Bulb Size, Music, the two buttons. Loading a
theme raises the UIA notification "<theme> theme loaded."

### 3.2 Bulb Factory [MUST]

Purpose: choose the bulbs for the 8 boxes (4 edges, 4 corners) with an exact live preview, from 1,550 bulbs; set the
flash pattern and where the bulbs are drawn. The 5.4 page's words and groups survive; its middle now shows your
screen.

#### 3.2.1 Wireframe, regions and sizes

Wide layout (content width >= 900 DIP; the default window):

```
Bulb Factory                                         [Peek] [Clear All Bulbs]   [Undo v] [Redo]
Drag bulbs into the boxes, or double-click one. Bulbs repeat along each edge in the order shown.
+TL-----+ +Top Edge  2/6 -------------------------+ +TR-----+  +Bulb List --------------------------+
| [JH]  | | [SB] [SF] [+]                          | | [JH]  |  | Double-click a bulb to use it for:  |
|       | | o.o.o.o.o.o.o.o.o.o.o.o (edge sample)  | |       |  |   the whole frame                   |
+-------+ +----------------------------------------+ +-------+  | [Search 1,550 bulbs    ] [Add Bulb...]|
+Left --+ +----------------------------------------+ +Right--+  | Show [All Bulbs (1,550) v]           |
| [SB]  | |                       [Display 1 v]    | | [SB]  |  | Sort [Original Order v] [Tiles|Det.] [...]|
| [SF]  | |   live preview of your screen:         | | [SF]  |  | 1,550 bulbs                          |
| [+]   | |   wallpaper, taskbar band, the real    | | [+]   |  | +------+ +------+ +------+           |
| o     | |   bulbs, glow and pattern              | | o     |  | |o~o~o | |  **  | | 8 8  |           |
| .     | |                                        | | .     |  | |Stand.| |Jolly | |Snow  |           |
| o     | |                                        | | o     |  | +------+ +------+ +------+           |
+-------+ +----------------------------------------+ +-------+  |   ... (virtualized, scrolls) ...     |
+BL-----+ +Bottom Edge  2/6 ----------------------+ +BR-----+  |--------------------------------------|
| [JH]  | | [SF] [JH] [+]                          | | [JH]  |  | [o~o~o] Standard Bulbs  [*] Built-In |
|       | | o.o.o.o.o.o.o.o.o.o.o (edge sample)    | |       |  | Standard lights, without the tangled |
+-------+ +----------------------------------------+ +-------+  | cord or burned-out bulbs!            |
Use a bulb for: [Whole Frame] [All Edges] [All Corners]  or select a box | 5 colors - flashes - 32 x 32 px |
+Flash Settings ---------------------------+ +Bulb Drawing ------------+ | Art: Joe Lachoff - Copyright ...|
| [Flash Together                      v]  | | (o) On Desktop           | | On your screen: Top, Right, Left|
| Slow |--+--+--+--o--+--+--+--| Fast      | |     [x] Behind the       | | [Use for the Whole Frame]       |
| One step every 0.30 s  [x] Smooth Fading | |         Desktop Icons    | | [Add To... v] [Edit Categories...]|
+------------------------------------------+ | ( ) On Top               | | [Bulb Credits] [...]            |
[Load Theme...]  [Save Settings As Theme...] +--------------------------+ +---------------------------------+
```

(SB) Standard Bulbs, (JH) Jolly Holly, (SF) Snow Family. "o.o.o" = animated edge sample.

| Region | Size (DIP) | Notes |
|---|---|---|
| Header | full width x 72 | Title, description, "Peek", "Clear All Bulbs", Undo/Redo. |
| Left column (frame editor and groups) | clamp(content width - 16 - 360, 540, 760) | Scrolls on its own when taller than the window; the frame editor stays at the top. |
| Corner boxes | 72 x 72 | |
| Top / Bottom Edge boxes | (column width - 160) x 72 | Chips 40 x 40 in a row + 16 DIP edge sample below. |
| Left / Right Edge boxes | 72 x preview height | Chips 32 x 32 in a column + 16 DIP edge sample at the outer side. |
| Preview | (column width - 160) x (width x display height / display width), at most 300 high | 3.2.3. |
| Quick targets row | 40 high | 3.2.4. |
| Flash Settings, Bulb Drawing | side by side (stacked below 640 DIP column width) | 3.2.8. |
| Right column (Bulb List) | the rest, min 340 wide, full height | 3.2.5, selected-bulb bar 3.2.6 at its bottom (about 168 high). |

Narrow layout (content width < 900 DIP): the frame editor fills the width at the top, then the quick targets, then
the Bulb List (minimum 420 high) with its selected-bulb bar, then Flash Settings and Bulb Drawing; the page scrolls.

Header actions: "&Peek" (3.2.3) and "Clear All &Bulbs" (empties all 8 boxes; one undo step; snackbar "Cleared all
edges and corners." [Undo]; no confirmation).

Bottom-left (the 5.4 place): "&Load Theme..." (opens Themes) and "Sa&ve Settings As Theme..." (Save Theme dialog,
3.6.3). The same two buttons, with the same access keys, sit at the bottom-left of Music Box and Screen Saver (5.4
showed them on these three tabs).

#### 3.2.2 The frame editor: 8 boxes [MUST]

**Model.** Top, Right, Bottom and Left Edge each hold an ordered list of 0-6 bulb types; Top-Left, Top-Right,
Bottom-Left and Bottom-Right Corner each hold 0 or 1 bulb. A bulb may appear in any number of boxes and repeatedly
in one edge (5.4). On screen, position i of an edge shows type `i mod n` with flavor `i div n` (5.4).

**Edge box.** Caption "Top Edge 2/6", "Right Edge 2/6", "Bottom Edge 2/6", "Left Edge 2/6" (12 DIP caption style).
**Chips** in order (left to right on top and bottom, top to bottom on left and right): a rounded 4 DIP square with a
night well showing the bulb's art for that edge, flavor 1, frame 0, fitted (when that art is nearly empty, as for
spacers, the bulb's 32 x 32 list preview is shown instead), plus a small order number. Hover or focus shows a remove
button (Cancel glyph) in the chip's corner. While the edge has fewer than 6 types, a dashed "+" placeholder follows
the last chip. The **edge sample** runs along the box: the first bulbs of that edge exactly as the layout engine
places them (types, flavors, spacing), fitted to 16 DIP, animated with the current pattern, the visible explanation
of the 5.4 rule "a repeating pattern of these bulbs along the top of your screen".

**Corner box.** Caption "Top-Left", "Top-Right", "Bottom-Left", "Bottom-Right"; the bulb's corner art, animated,
fitted at up to 1.5x; remove button on hover or focus; a dashed "+" when empty.

**Selection = target.** Selecting a chip, a "+" or a corner makes it the target (3.2.4): 2 DIP accent outline on it
and a 1 s accent highlight of the matching strip or corner in the preview. Hovering any box outlines its strip in
the preview.

**Box menus.** Right-click on empty space in a box, or Shift+F10 on its caption: "&Clear" (that box),
"Copy This Edge to &All Edges" (edges: the other three edges get this edge's list), "&Pick a Bulb..." (makes the box's
"+" the target and focuses the Bulb List search). Chip menu: 3.2.11.

**Accessible names.** Edge box "Top edge, 2 of 6 bulb types: Standard Bulbs, Snow Family." Chip "Standard Bulbs,
position 1 of 2 on the top edge." "+" "Add a bulb to the top edge, 2 of 6 used." Corner "Top-left corner: Jolly
Holly" / "Top-left corner: empty".

#### 3.2.3 The preview and Peek [MUST]

The middle of the frame is a light stage (3.0.2) of one display: the main display by default; with two or more
enabled displays a "Display 1 (Main)" selector sits in its top-right corner (it lists the enabled displays). Caption
under it: "Display 1 - 3840 x 2160 - 150 %". The stage shows the real arrangement, or the try-on result while a bulb
is tried on.

* **Click to select.** Clicking a bulb on an edge selects that bulb type's chip on that edge; clicking an empty
  edge band selects that edge's "+"; clicking a corner zone selects the corner box; clicking the middle selects the
  "Whole Frame" target.
* **Drop on the preview.** A bulb dropped on the stage goes to the nearest box: a corner zone (the outer 18 % of the
  width and of the height at each corner) replaces that corner; elsewhere the nearest edge appends it.
* **Peek** (header button "Peek", View glyph; also on Home): press and hold to see the real desktop while held; a
  click shorter than 300 ms, Enter or Space peeks for 3 s. Peek **cloaks** the Settings window with
  `DWMWA_CLOAK` (instant, no opacity change, works with Mica) and uncloaks it afterwards with focus restored; any key
  ends a timed peek early. Tooltip: "Hide this window for a moment so you can see your desktop."

#### 3.2.4 Targets: "Double-click a bulb to use it for: ..." [MUST]

There is always exactly one **target**: the place a bulb goes when it is double-clicked, when Enter is pressed on
it, or when the "Use for ..." button is pressed. **Selecting or hovering a bulb never changes the arrangement**; it
only tries the bulb on in the preview. Dragging works independently of the target (3.2.7).

| Target | Selected by | What using a bulb does | Sentence shown |
|---|---|---|---|
| **Whole Frame** (default each time the page opens) | "Whole Frame" button; clicking the middle of the preview | All four edges become exactly [this bulb] and all four corners become this bulb (5.4's dead command 1005, revived). | "the whole frame" |
| **All Edges** | "All Edges" button | All four edges become exactly [this bulb]; corners unchanged. | "all four edges (corners stay)" |
| **All Corners** | "All Corners" button | All four corners become this bulb; edges unchanged. | "all four corners" |
| A **chip** | clicking or focusing the chip; clicking that bulb in the preview | That chip's bulb is replaced; its position is kept. | "the 2nd bulb on the top edge" |
| An edge's **"+"** | clicking or focusing "+"; clicking an empty edge band | The bulb is added at the end of that edge, and the target moves to the new chip, so the next bulb used replaces it (trying several bulbs in the same spot). | "a new bulb at the end of the top edge (3 of 6)" |
| A **corner** | clicking or focusing the corner box | The corner becomes this bulb. | "the top-left corner" |

* The quick targets sit in a row under the frame: "Use a bulb for:" [Whole &Frame] [All Ed&ges] [All Cor&ners]
  "or select a box". The current quick target is shown pressed; selecting a box releases them.
* The sentence "Double-click a bulb to use it for: <target>" heads the Bulb List; the primary button of the
  selected-bulb bar reads "Use for <Target>" ("Use for the Whole Frame", "Use for the Top-Left Corner").
* **Try-on.** Hovering a tile for 500 ms, or keyboard focus resting on it for 500 ms, shows in the preview what using
  it for the target would do, with the pill "Trying On: <bulb> - <target>". It reverts 150 ms after the pointer or
  focus leaves the Bulb List. The desktop never changes during try-on.
* Every use is one undo step. Using a bulb for Whole Frame, All Edges or All Corners shows a snackbar "Using Candy
  Canes on the whole frame." [Undo]; every use raises the UIA notification "Candy Canes now on the whole frame."

#### 3.2.5 The Bulb List [MUST]

**Contents.** Every bulb that is not removed: the 49 built-in bulbs, the 1,501 bundled add-on bulbs and My Bulbs.
Only file headers (name, description, author, categories, preview window, sizes, frame counts) are read at start-up,
on a background thread, and cached (`%LOCALAPPDATA%\Holiday Lights\Cache\index.json`, invalidated by file size and
date); pictures decode lazily for realized items and are cached per DPI.

**Toolbar.**

| Control | Label | Default | Behavior | Keyboard |
|---|---|---|---|---|
| Search box | placeholder "Search 1,550 bulbs" (live count) | empty | Filters as you type (150 ms debounce). Matches name, categories, description, author and file name; every word must match; case- and accent-insensitive. Esc clears. Typing a printable character while the grid has focus starts a search. | Ctrl+F |
| "&Add Bulb..." | button (5.4 label) | | Open dialog "Bulb Files and GIF Files" (`*.bul; *.gif`), multi-select; 3.2.10. | Alt+A |
| "S&how:" | combo | "All Bulbs (1,550)" | Items (live counts): "All Bulbs (n)", "Favorites (n)", "In Use (n)", "For <Holiday> (n)" (only while today is in a checked holiday of the calendar: the bulbs in that holiday's categories - New Year: New Year; Valentine's Day: Valentine's Day; St. Patrick's Day: St. Patrick's Day; Easter: Easter, Spring; July 4th: July 4th, Flags; Halloween: Halloween, Autumn; Thanksgiving: Thanksgiving, Autumn; Christmas: Christmas, Winter; Chanukah: Hanukkah), "Built-In Bulbs (49)", "Add-On Bulbs (n)", "My Bulbs (n)", separator, every non-empty category "Name (n)" A-Z (built-in, bundled and your categories merged case-insensitively), and "Removed Bulbs (n)" when n > 0. **Resets to "All Bulbs" every time Settings opens (5.4).** | Alt+H |
| "So&rt:" | combo | "Original Order" | "Original Order" (the 49 built-ins in 5.4 table order, then add-ons A-Z: the 5.4 list order), "Name", "Newest First" (file date; built-ins last). While a search is active the order is "Best Match" (name prefix, then name word, then name substring, then category, then description, author or file name; ties by the chosen sort). Remembered. | Alt+R |
| View | segmented "Tiles" / "Details" | Tiles | Remembered. | |
| "..." | menu button | | "Open My &Bulbs Folder", "&Import Bulb Files from a Folder..." (adds every `.bul` in a chosen folder), "Show &Removed Bulbs" (switches Show), "Show &Damaged Bulbs" (only when there are some). | |

**Result line** under the toolbar: "1,550 bulbs" / "37 bulbs match "snow"" (polite live region).

**Tiles view.** Tiles 112 x 124 DIP, uniform spacing 8. Night well 96 x 72 showing a short run of the bulb as it
looks on the top edge (flavors 1, 2, 3 ... side by side at the bulb's real spacing, scaled to the well height but at
most 2x the bulb size setting, cropped to the width; spacers show their list preview), animated (3.0.3); name below
(2 lines, ellipsis). Badges: favorite star (filled when favorite; outline on hover or focus; click toggles), accent
dot "In use", "New" pill for 24 hours after a bulb was added, "Big" tag when any cell is larger than 64 px (tooltip
"Big bulbs make a thick border."), warning glyph for a damaged bulb.

**Details view** (the 5.4 row). Rows 56 DIP: 40 x 40 night well with the bulb's 32 x 32 list preview (the 5.4
white-square crop) at 1x, **bold name**, the description (2 lines), and right-aligned captions "<Built-In | Add-On |
My Bulb> - <author first line>" and the categories.

**Selection and activation.** Click selects (and tries on, 3.2.4); Ctrl/Shift+click multi-selects; double-click or
Enter uses the bulb for the target; Space selects (never applies); Ctrl+D toggles favorite; Shift+F10 or the Menu key
opens the context menu (3.2.11). With several bulbs selected, double-click and Enter do nothing; the selected-bulb bar
offers "Add To..." (adds them in selection order) and "Add to Favorites".

#### 3.2.6 Selected-bulb bar and the "Add To..." flyout [MUST]

At the bottom of the Bulb List (the 5.4 row text lives here):

* Art: a 128 x 72 night well with the top-edge run (animated) and, beside it, the corner art when the bulb has one.
* **Name** (bold), favorite star, source tag "Built-In", "Add-On" or "My Bulb".
* Description, verbatim.
* Facts: "<n> colors" (top-edge flavors; "1 color") - "flashes" (light bulbs, 5.4) / "animated" / "still" -
  "<w> x <h> px" (largest top-edge cell at 100 %), plus the "Big" tag.
* Credits: "Art: <author, first line> - <copyright>" and "This art may not be used for other purposes without the
  author's permission." (5.4 text).
* Usage: "On your screen: Top Edge, Bottom-Left" / "Not on your screen".
* Buttons: "&Use for the Whole Frame" (primary; text follows the target), "Add T&o..." (flyout), "&Edit Bulb..." or
  "&Edit Categories..." (3.3), "Bulb &Credits" (3.3.3), "..." (the remaining context-menu items).
* With nothing selected: "Select a bulb to see it here. Double-click a bulb to use it, or drag it into a box."

**"Add To..." flyout** - a miniature of the frame, so the button teaches the box model:

```
+ Add "Standard Bulbs" to ------------------------------+
| [Top-Left] [       Top Edge  1/6       ] [Top-Right]   |
| [ Left   ]                               [  Right  ]   |
| [  2/6   ]                               [   2/6   ]   |
| [Bottom-Left][     Bottom Edge 2/6    ][Bottom-Right] |
| [Add to Every Edge] [Use in All Corners] [Use Everywhere]|
+-------------------------------------------------------+
```

* An edge **adds** the bulb(s) at the end; a full edge (6/6) is disabled with the tooltip "This edge already has 6
  bulb types. Remove one first."
* A corner replaces its bulb.
* "Add to Every Edge" adds to every edge that has room; if some were full: InfoBar "The bottom edge is full, so the
  bulb was not added there."
* "Use in All Corners" sets all four corners; "Use Everywhere" makes this bulb the only type on all four edges and
  puts it in all four corners (5.4 command 1005). Tooltip: "Replaces every edge and corner with this bulb. You can
  undo it."
* Arrow keys move between the regions; Enter applies; Esc closes. Each choice is one undo step.

Wording rule used everywhere: **"Add to ..." appends; "Use for/in ..." replaces.**

#### 3.2.7 Drag and drop [MUST]

Sources: tiles and rows (one or many), chips, corner contents, files from Explorer. A drag starts after the system
drag distance (5.4 had none); Esc cancels. The drag visual is the bulb's list preview at 2x, 85 % opaque; the grab
cursor shows over every draggable bulb (5.4 missed the first list row).

| Drop | On | Result (one undo step) | Indicator |
|---|---|---|---|
| Bulb(s) from the list | an edge box, between chips | Inserted at that position (5.4); fills up to 6 types, then stops. | 2 DIP accent insertion bar |
| Bulb from the list | a chip of a **full** edge | Replaces that chip (the 5.4 overwrite rule, made explicit). | chip outline + "Replace" label |
| Bulb(s) from the list | a full edge, not on a chip | Refused. | box tints red; tooltip "This edge already has 6 bulb types. Drop onto a bulb to replace it." |
| Bulb from the list | a corner box | Replaces the corner. | box highlight |
| Bulb from the list | the preview | As if dropped on the nearest box (3.2.3). | stage highlights the box's strip |
| Chip or corner | another box | **Copies** (5.4). Hold **Shift** to move instead. | "+" badge on the drag visual while copying |
| Chip | another place in its own edge | Reorders (new). | insertion bar |
| Chip or corner | the Bulb List, or anywhere outside the frame | Removes it from its box (5.4), with a 150 ms fade. | drag visual shows "Remove from Top Edge" |
| `.bul` / `.gif` files | anywhere in the window | Imported (3.2.10). Dropped on a box: imported, then placed there. | cursor "Add to Holiday Lights" |
| Music files / picture files | anywhere in the window | Added to Music Box / Screen Saver pictures; snackbar "Added 2 songs." [Show] | |
| Any other file | anywhere | InfoBar "Holiday Lights can add bulb files (.bul), animated GIF pictures (.gif), songs and pictures." | |

During a drag every valid box shows a dashed accent outline and the box under the pointer fills with 10 % accent.
After a drop the chip settles (scale 0.8 to 1.0, 150 ms), the edge sample, the preview and the desktop update within
one frame, and a UIA notification announces the result ("Added Standard Bulbs to the top edge.").

#### 3.2.8 Flash Settings and Bulb Drawing [MUST]

Shown on Bulb Factory and on Home; both copies are bound to the same settings.

**"Flash Settings"** (5.4 group name):

| Control | Label | Default | Behavior |
|---|---|---|---|
| Pattern combo | accessible name "Flash Pattern" | "Flash Together" (or imported/theme) | Items show a 5-bulb animated demo (Standard Bulbs, or Mini Bulbs for the new patterns) and a second line. Order: header "Classic", "Don't Flash", "Flash Together", "Alternating", "Bulb Chase", "Random Flashing"; header "New in 6.0", "Twinkle", "Slow Glow", "Chase Around the Screen", "Dance to the Music" (and, when built, "Waves" and "Combination"). Applies at once. |
| Speed slider | "Slow" (left), "Fast" (right) | position 5 of 9 (300 ms) | 9 positions; position p (1 slowest .. 9 fastest) sets Flash Interval 10 - p (540 ... 60 ms per step; the 5.4 mapping). Value text "One step every 0.30 s". Disabled for "Don't Flash" with the text "Speed doesn't apply when bulbs don't flash." (5.4 disabled it too). For "Dance to the Music" the label reads "Speed Between Songs". At positions 8-9 a caution line: "Very fast flashing can be uncomfortable for some people." While "Limit Flashing to 3 Flashes per Second" is on, positions 8-9 are disabled with the note "Faster speeds are off because Limit Flashing is on (General)." A change takes effect at the next step boundary without resetting the step counter. |
| "S&mooth Fading" | check box | on | Light bulbs fade on and off instead of switching (5.8). Also set by the Look presets. |

Second lines of the pattern items (the "Don't Flash" caption describes the built-in bulbs: Don't Flash shows frame 0 of every bulb exactly as 5.4, 5.6): "Don't Flash" "The bulbs stay lit." / "Flash Together" "All bulbs change at the
same time." / "Alternating" "Every other bulb flashes." / "Bulb Chase" "The lights run along each edge." /
"Random Flashing" "Bulbs flash at random, repeating every 8 steps like the original." / "Twinkle" "Each light
twinkles on its own, like real twinkle lights." / "Slow Glow" "All lights slowly brighten and dim together." /
"Chase Around the Screen" "One light in three runs clockwise around the whole screen." / "Dance to the Music" "The
lights play along with the music. Between songs: Slow Glow." / "Waves" "Waves of light travel around the screen." /
"Combination" "A different pattern every few seconds."

**"Bulb Drawing"** (5.4 group name):

| Control | Label | Default | Behavior |
|---|---|---|---|
| Radio | "On &Desktop" (5.4) + secondary "Below all your windows." | selected | Bulbs on the desktop (layer modes 5.1). |
| Check box, indented, enabled with On Desktop | "Behind the Desktop &Icons" + secondary "Your icons stay in front of the bulbs and keep working." | checked | Checked: behind the icons (falls back automatically, 5.1). Unchecked: in front of the icons, still behind every window. |
| Radio | "On &Top" (5.4) + secondary "Above all windows. Clicks go through the bulbs." | | On top of all windows, below the taskbar; hidden on a display while a full-screen app runs there (5.12). |
| Status line | (only while a fallback is active) warning glyph + the matching Home status text | | "Try Again" retries at once. |
| Link | "More Display Settings..." | | Opens General > Where Bulbs Are Drawn. |

#### 3.2.9 Empty, loading and error states [MUST]

| Situation | What shows |
|---|---|
| Arrangement empty (Blank Slate) | Boxes show "+" only; preview overlay "No bulbs yet. Double-click a bulb to fill the whole frame, or drag bulbs into the boxes." |
| First start, add-on bulbs still being indexed | Built-in bulbs appear at once, then skeleton tiles and "Loading add-on bulbs... 640 of 1,501" in the result line; search covers what is loaded and refreshes. Later starts read the cache (<= 1 s). |
| Search finds nothing | "No bulbs match "zombie dog"." + "Clear Search" + "Show All Bulbs". |
| Favorites empty | "No favorites yet. Click the star on any bulb to keep it here." |
| My Bulbs empty | "Bulbs you add or make appear here. Click Add Bulb... to add a .bul file or to turn an animated GIF into a bulb." |
| In Use empty | "No bulbs are on your screen. Double-click a bulb, or drag bulbs into the boxes." |
| Removed Bulbs | Each tile has "Restore"; the toolbar shows "Restore All". |
| A file in My Bulbs cannot be read | Warning InfoBar "1 bulb file couldn't be read: <file name>. It may be damaged." + "Show in Folder". The file is not listed (it appears under "Show Damaged Bulbs"); it is never deleted automatically. |
| One animation inside an otherwise readable bulb fails to decode | That animation is drawn as the 5.4 WARNING picture and the description reads "This bulb is damaged and may cause problems." (5.4 behavior and text); the bulb can still be used. |
| Arrangement refers to bulbs that are missing (file deleted outside the program) | Chip shows the warning glyph and the stored name (tooltip "This bulb's file is missing (Star.bul)."); InfoBar "2 bulbs in your arrangement are missing." + "Remove Missing Bulbs". Missing bulbs are skipped on screen. |
| Graphics device lost | Preview overlay "Preview unavailable - reconnecting"; the desktop recovers on its own (5.13). |

#### 3.2.10 Adding bulb files and GIFs [MUST]

Entry points: "Add Bulb...", dropping files anywhere on the window, "Import Bulb Files from a Folder...", double-click
of a `.bul` in Explorer, `--open "<file>"` and legacy `open <file>`.

| File | Result |
|---|---|
| `.bul` whose bulb content equals a bulb already present (6.3: header strings and GIF entry CRCs; region caches, sender record and flags ignored) | Nothing copied; that bulb is selected (Show switches to All Bulbs if needed); InfoBar "<name> is already in your bulbs." |
| `.bul`, new | Copied into My Bulbs as `<file name>.bul`, or `<file name> (2).bul` when that name exists; appears selected with the "New" pill; snackbar "Added <name>." [Undo]; InfoBar tip "Double-click it to use it, or drag it into a box." |
| `.bul`, damaged (5.4 rules) | Nothing copied; error InfoBar "Problem Importing File: Sorry, that bulb file is damaged and can't be used." (5.4 wording). |
| `.gif` | A new bulb is created in My Bulbs from the GIF (5.4): all 9 slots use it; the list-preview square is centered; name = the GIF's file name; description "No description is available for this bulb."; author = the Windows account display name; copyright "Copyright <year> <display name>"; file `<name>.bul` or `<name> 1.bul` ... (5.4 naming). Then **Bulb Editing** opens on it (5.4). Cancelling the editor keeps the new bulb (5.4); Undo removes it. Undecodable GIF: "Cannot Import GIF File: Sorry, this GIF file can't be imported. It may be damaged in some way." (5.4 wording). |
| Anything else | "Problem Importing File: Sorry, that type of file can't be added. Holiday Lights can add bulb files (.bul) and animated GIF pictures (.gif)." |

Several files are processed in order and summarized in one snackbar ("Added 3 bulbs. 1 file was already in your
bulbs." [Show]). Several GIFs dropped at once create one bulb each without opening the editor; they are selected under
My Bulbs.

#### 3.2.11 Context menus [MUST]

**Bulb List tile or row** (acts on the selection):

| Item | Shown for |
|---|---|
| **"&Use for <Target>"** (bold default, = double-click) | one bulb |
| "Add to &Top Edge", "Add to Ri&ght Edge", "Add to &Bottom Edge", "Add to &Left Edge" (disabled when full), "Add to E&very Edge" | all |
| "Use in All C&orners", "Use Every&where" | one bulb |
| separator | |
| "Add to &Favorites" / "Remove from &Favorites" (Ctrl+D) | all |
| separator | |
| "&Edit Bulb..." (5.4) or "Edit Categor&ies..." (3.3) | one bulb |
| "Bulb &Credits" (5.4) | one bulb |
| "E&xport Bulb File..." (Save As dialog; copies the `.bul`) | add-on bulbs |
| "Show in Fol&der" | add-on bulbs |
| "Use as Screen Saver &Animation" (sets the screen saver animation to this bulb) | add-on bulbs |
| "&Remove Bulb" (5.4; your bulb goes to the holding folder; a bundled bulb is hidden) / "Re&store Bulb" (in Removed Bulbs) | add-on bulbs only (5.4: built-ins cannot be removed) |
| separator | |
| "S&how" > the Show filter items | always; on empty space it is the only item (5.4) |

Removing asks nothing (it is undoable): the arrangement is updated and **saved** (5.4 bug fixed), themes that use the
bulb keep their reference, and a snackbar says "Removed <name>." [Undo].

**Chip / corner menu:** "Move &Left" / "Move &Right" ("Move &Up" / "Move &Down" on the left and right edges),
"&Copy To" > the other 7 boxes, "Mo&ve To" > the other 7 boxes, "Re&move", separator, "Edit Bulb..." / "Edit
Categories...", "Bulb Credits", "&Find in Bulb List" (selects and scrolls to it; Show switches to All Bulbs if
needed).

#### 3.2.12 Keyboard and screen reader [MUST]

* Tab order: header actions, the frame editor (one tab stop), the quick targets, Flash Settings, Bulb Drawing, the
  theme buttons, then the Bulb List (search, Add Bulb..., Show, Sort, View, "...", the grid (one tab stop), the
  selected-bulb bar).
* **Frame editor** (roving focus; **focus is selection**): entering it focuses the current target (or the Top Edge
  "+"). Arrow keys move between boxes spatially (Up from Left Edge goes to Top-Left, etc.); inside an edge,
  Left/Right (Up/Down on side edges) move between chips and "+". Ctrl+arrows move the focused chip within its edge.
  Delete or Backspace removes the focused chip or clears the corner. Enter moves focus to the Bulb List search box
  to pick a bulb for the focused place. Shift+F10 opens the chip or box menu. Ctrl+V adds the bulbs selected in the
  Bulb List to the focused edge (same as "Add to").
* **Bulb List grid:** arrows, Home/End, PgUp/PgDn move; Enter uses the focused bulb for the target; Shift+Enter
  adds it to every edge; Space selects; Ctrl+D favorite; Shift+F10 menu; printable characters start a search.
* Names: tiles "Candy Canes, built-in bulb, 4 colors, animated" (+ ", favorite", ", in use"); the frame editor
  HelpText summarizes the frame ("Top: Standard Bulbs. Right: Standard Bulbs, Snow Family. ... Corners: Jolly
  Holly."). Every change raises a UIA notification: "Candy Canes now on the whole frame.", "Ghosts replaced by Bats
  on the top edge.", "Removed Ghosts from the top edge.", "Moved Snow Family to the left edge."

### 3.3 Bulb Editing, Edit Categories, New Category, Bulb Credits [MUST]

"Edit Bulb..." opens **Bulb Editing** for a bulb in My Bulbs whose header says `locked` = 0 (made with Holiday Lights,
5.4 rule); for every other bulb (built-in, bundled, or someone else's) the same command reads "Edit Categories..."
and opens **Edit Categories** (5.4: "If someone else created the bulb, you'll be able to edit the categories").

#### 3.3.1 Bulb Editing

Modal over Settings. Caption "Bulb Editing - <name>". 980 x 640 DIP, resizable (minimum 860 x 600). The 5.4 groups,
labels and texts are kept; the slot map, zoom and samples are additions.

```
+ Bulb Editing - Star ------------------------------------------------------------------------ [X] +
| +Bulb Information--------------------------------+ +Animation Frames--------------------------------+|
| | The text below appears in the bulb list and the | | +----+----------+----+  [Top Side         v]     ||
| | bulb credits window.                            | | | TL |   Top    | TR |  [Flavor 1 *       v]     ||
| | Bulb &Name                                      | | +----+----------+----+                           ||
| | [Star                                    ] 4/79 | | |  L | Bulb List|  R |  This animation appears   ||
| | &Description                                    | | |    | Preview  |    |  along the edge of the    ||
| | [No description is available for this bulb.]   | | +----+----------+----+  screen. It can have more ||
| | &Author's Name and E-Mail Address               | | | BL |  Bottom  | BR |  than one flavor.         ||
| | [Pat Smith                                 ]    | | +----+----------+----+                           ||
| | [                                          ]    | | +---------------------------------------------+ ||
| | &Copyright Message                              | | |  preview on a night well (or checkerboard), | ||
| | [Copyright 2026 Pat Smith                  ]    | | |  centered, zoomable, animated               | ||
| | Ca&tegories                                     | | +---------------------------------------------+ ||
| | [x] Christmas  [ ] Winter  [ ] Light Bulbs ...  | | Zoom [-] 2x [+] [Pause] Frame 1 of 4 [Transparency]|
| | [New Category...]                               | | [Change...] [Remove Flavor] [Copy to All Sides] ||
| +-------------------------------------------------+ | Edge sample: o.o.o.o.o.o.o.o.o.o              ||
|                                                     | Press Change to choose a new animation.        ||
|                                                     | (The first flavor cannot be removed.)          ||
|                                                     +------------------------------------------------+|
|                                                                              [ Save ]  [ Cancel ]   |
+----------------------------------------------------------------------------------------------------+
```

| Control | Behavior |
|---|---|
| "Bulb &Name", "&Description" | Single line, at most 79 characters each (file format), live counter. The name is required ("Give your bulb a name."). |
| "&Author's Name and E-Mail Address", "&Copyright Message" | Two lines each, at most 79 characters including the line break (5.4 stored "Name\r\nE-mail"). |
| "Ca&tegories" | Check list of every known category (A-Z; the internal "_0"/"_1" are hidden); Space or click toggles. |
| "New Category..." | Opens New Category (3.3.2); the new category is added checked (5.4). |
| Slot map (3 x 3) | The frame metaphor: the 8 outer cells are the 4 sides and 4 corners, the center cell the "Bulb List Preview". Each cell shows its slot's first frame; click or arrow keys select. Same choice as the slot combo. |
| Slot combo | 5.4 names and order: "Bulb List Preview", "Top Side", "Left Side", "Right Side", "Bottom Side", "Top-Left Corner", "Top-Right Corner", "Bottom-Left Corner", "Bottom-Right Corner". |
| Flavor combo | "Flavor 1" ... "Flavor 8", with " *" appended when that flavor of the current side has an animation (5.4). Disabled for the preview and the corners. |
| Explanation texts | The 5.4 texts per slot, verbatim: e.g. "This animation appears along the edge of the screen. It can have more than one flavor." / "Press Change to choose a new animation.\n(The first flavor cannot be removed.)"; the 5.4 flavor explanation when a flavor is empty ("No animation has been chosen for this flavor...."). |
| Preview | The selected slot and flavor, centered on a night well ("Transparency" toggles a checkerboard), animated at the current flash speed with the **faithful 5.4 decoder**; zoom 1x-8x (default: the largest integer that fits); pause; frame stepper (Left/Right while paused). Larger pictures scroll. Dropping a GIF on it = Change. |
| White square (Bulb List Preview only) | The 32 x 32 window that becomes the list picture (5.4): drag with the mouse, or arrow keys (1 px) and Shift+arrows (8 px); outside the square is dimmed with the 5.4 DITHER pattern; black inner and white outer outline. A new preview animation centers the square (5.4 rule). |
| "Change..." | Chooses a GIF ("GIF Files" `*.gif`) for the slot and flavor; identical GIFs are stored once (CRC, 5.4); error "Cannot Import GIF File" / "A problem occurred when importing the GIF file. It cannot be used with this bulb." (5.4); when 37 different GIFs are used: "This bulb already uses 37 different animations, the most a bulb file can hold." |
| "Remove Flavor" | Enabled for flavors 2-8 that have an animation (5.4). |
| "Copy to All Sides" | Copies the selected side's flavors to the other three sides (one step of the dialog's own undo, Ctrl+Z). |
| Edge sample | A light-stage strip of the selected side repeated with all its flavors, as on the desktop. |
| GIF tip | InfoBar when the faithful decode of the chosen GIF differs from a standard decode: "This GIF only stores what changes between frames. Holiday Lights draws GIFs exactly like version 5.4, so some parts may look see-through. Saving the GIF with full frames avoids this." |
| "Save" | Writes a new, compacted, 5.4-compatible `.bul` (version 4) atomically and replaces the bulb's file; the file name never changes when the bulb is renamed (5.4). The previous file goes to the holding folder (Undo in Settings restores it). The bulb reloads; if it is on the screen the lights update. Settings then shows the InfoBar "Saved <name>. To share it, right-click it and choose Export Bulb File..." |
| "Cancel", Esc, X | No changes: closes. With changes: "Discard Changes?" / "Your changes to <name> will be lost." [Discard] [Keep Editing]. Categories created meanwhile disappear (5.4). |

Keyboard: Tab through the fields, the slot map (arrows), combos, preview (arrows move the white square), buttons;
Ctrl+S = Save; F1 opens "Making Your Own Bulbs".

#### 3.3.2 Edit Categories and New Category

* **Edit Categories** (built-in bulbs and add-on bulbs made by others; 5.4). Caption "Edit Categories - <name>".
  Text "Use the checkboxes to choose one or more categories for this bulb." (5.4). Check list; "&New..."; "Save";
  "Cancel". The result is stored in settings keyed by bulb id (`bulbs.categoryOverrides`); the bulb file is never
  touched. One undo step in Settings.
* **New Category**: "Create a new bulb category named:" (5.4); text box, at most 39 characters; "OK" is enabled when
  the name contains a letter or digit; "|" becomes a space; leading and trailing spaces are trimmed; an existing name
  (case-insensitive) is simply checked.

#### 3.3.3 Bulb Credits

Caption = the bulb's name (5.4). Content: the bulb's preview animation (2x on a night well), "This art may not be used
for other purposes without the author's permission." (5.4), group "Author" (the author field verbatim with its line
breaks; e-mail addresses and web addresses as selectable plain text, not links), group "Copyright Information"
(verbatim), and a caption with the source: "Built-in bulb from Holiday Lights 5.4", "Add-on bulb included with
Holiday Lights - <file name>", or "My Bulb - <file path>" + "Show in Folder". Button "OK".

### 3.4 Music Box [MUST]

Purpose: turn music on or off, choose the songs and when they play, set the volume, watch what plays.

#### 3.4.1 Wireframe

```
Music Box                                                                          [Undo v] [Redo]
Holiday Lights can play music in the background. It randomly chooses songs from among the checked songs.
+---------------------------------------------------------------------------------------------------+
| Play Holiday Music                                                                  [ On  (o)]   |
| Jingle Bells (Reggae)          Arranged by Dean Burris, 1999        0:42 =====o-------------- 2:13 |
| [Previous] [Pause] [Next Song]                         [Mute] Volume -----------o------- 60 %     |
|                                                                     Windows Volume Mixer          |
+---------------------------------------------------------------------------------------------------+
+Songs ------------------------------------------------------------+ +Play the Chosen Songs ---------+
| (All 46) (Christmas 31) (Chanukah 3) (Halloween 5) (New Year 2)   | | ( ) Never                     |
| (Patriotic 2) (Folk & Classics 4) (My Songs 0)   46 songs, 31 on  | | ( ) Only When the Screen Saver|
| +---------------------------------------------------------------+ | |     is On                     |
| |[x] |>| A Very Merry Christmas       1:58  MIDI  George Larson | | | ( ) Only When the Screen Saver|
| |[x] |>| Almost Time for Christmas    2:10  MIDI  George Larson | | |     is Off                    |
| |[x] |>| Angels We Have Heard On High 1:41  MIDI  George Larson | | | (o) Always                    |
| | ...                                                           | | | ( ) Intermittently            |
| +---------------------------------------------------------------+ | |     A song every 1 to 3       |
| To turn a song on or off, click its check box. To hear a song    | |     minutes.                  |
| now, click its play button.                                       | +-------------------------------+
| [Check All] [Check None] [Add Song...] [...]                      | +Lights and Music --------------+
+-------------------------------------------------------------------+ | [ ] Make the Lights Dance to  |
                                                                      |     the Music                 |
> Advanced                                                            +-------------------------------+
[Load Theme...]  [Save Settings As Theme...]
```

#### 3.4.2 Play Holiday Music and Now Playing

| Control | Default | Behavior |
|---|---|---|
| "Play Holiday &Music" toggle | off for newcomers and factory-default 5.4 imports; customized imports: on unless 5.4 was "Never" (6.8) | The global music switch (6.1.1): off = no music at all, whatever the theme says. Same setting as the tray item and the Home card. Turning it on while "Play the Chosen Songs" is "Never" sets it to "Always". Not part of themes. |
| Title, arranger, progress | | Song title (file name without extension); "Arranged by <arranger>, <year>" for bundled songs (6.1.4) or the file's artist tag; elapsed / length; read-only progress for MIDI, seekable for audio files. |
| Status text when idle | | "Music is off." / "Music is set to Never play." / "Music plays only while the Holiday Lights screen saver is showing." / "Next song in 1:24" / "Paused" / "Paused while a full-screen app is open" / "Paused during your Focus session" / "No songs are checked, so no music will play." |
| "&Pause" / "&Resume" | | Pauses mid-song and resumes from the same point (MIDI: program and controller state is re-sent). Lasts until resumed or the program restarts. |
| "Ne&xt Song" | | Starts another random checked song now (shuffle bag). |
| "Previous" | | Restarts the current song; pressed again within 3 s, plays the song that played before it. |
| Volume slider + "Mute" | 60 % | 0-100 %. MIDI scales every channel's CC7; audio files scale their output. Never changes the Windows mixer. |
| "Windows Volume Mixer" link | | Opens `ms-settings:apps-volume`. |

#### 3.4.3 Songs

* Columns: check box, play button, "Song", "Length", "Type" ("MIDI", "MP3", "WAV", "WMA", "AIFF", "AU", "MPEG",
  "M4A"), "Arranger". Order: the 5.4 order (upper-cased file name). The playing song shows an equalizer glyph in place
  of its play button.
* Sources: the 46 bundled songs (read-only, install folder) and your My Music folder
  (`Documents\Holiday Lights\Music`), including `.lnk` shortcuts to music files elsewhere (5.4; the shortcut's target
  type decides the player - 5.4 quirk fixed). Hidden and temporary files are skipped. The folder is watched; songs
  copied in by hand appear at once.
* **New songs start checked** (5.4 "Disabled Music" rule: the settings store the unchecked songs, so a song that
  appears in a folder is checked in the current settings). Loading a theme checks exactly the theme's songs.
* Category chips filter what is shown (not what plays): "All", "Christmas", "Chanukah", "Halloween", "New Year",
  "Patriotic", "Folk & Classics" (6.1.4) and "My Songs". The header shows "46 songs, 31 on".
* Mouse: click the check box toggles; the play button plays now (ignores the play mode and the check box, as 5.4
  "Play Now"); click elsewhere selects; double-click plays.
* Keyboard: Space toggles the focused song's check box (5.4); Enter plays it; Delete = Remove Song; Shift+F10 = menu.
* Context menu: **"&Play Now"** (bold, 5.4), "&Remove Song" (5.4), "Show in &Folder".
* "Chec&k All" / "Check Non&e" apply to the songs shown (one undo step).
* "Add &Song..." (5.4) - open dialog "Music Files" (`*.mid; *.rmi; *.mp3; *.wav; *.wma; *.aif; *.aifc; *.aiff; *.au;
  *.snd; *.mpeg; *.m4a`), multi-select; files are **copied** to My Music (5.4) and start checked. Same content
  already present: skipped ("<name> is already in your Music Box."). Same name, different content: saved as
  "<name> (2)". Dropping files on the window does the same.
* "..." menu: "Open My Music Folder", "Restore Removed Songs" (only when bundled songs were removed).
* Remove Song: your song goes to the holding folder (snackbar "Removed <title>." [Undo]); a shortcut is removed
  the same way; a bundled song is hidden ("Bundled songs aren't deleted. Use Restore Removed Songs to bring them
  back."). Removing the playing song starts another.
* A file Windows cannot decode stays listed with a warning glyph and the tooltip "Holiday Lights can't play this
  kind of file."; the shuffle skips it.
* Empty list: "There are no songs. Click Add Song..., or Restore Removed Songs."

#### 3.4.4 Play the Chosen Songs

Radios with the exact 5.4 labels and access keys, in 5.4 order: "&Never", "Only When the Screen Saver is &On", "Only
When the Screen Saver is O&ff", "&Always", "&Intermittently" (secondary text under it: "A song every 1 to 3
minutes."). Part of themes (6.4.1). Timing and shuffle exactly as 5.4 (6.1.2). While "Play Holiday Music" is off the
radios stay editable and an InfoBar inside the card says "Music is turned off. Turn on Play Holiday Music to hear the
chosen songs." [Turn On].

#### 3.4.5 Lights and Music; Advanced

* "Make the Lights &Dance to the Music": checking sets the flash pattern to "Dance to the Music" and remembers the
  previous pattern; unchecking restores it. It is checked whenever the pattern is "Dance to the Music". Secondary
  text: "Changes the flash pattern in the Bulb Factory. Classic patterns always keep their own timing."
* "Advanced" expander: "MIDI O&utput:" combo listing MIDI output devices; default "Microsoft GS Wavetable Synth" (the
  first device whose name contains "GS Wavetable", else device 0). Changing it restarts the playing MIDI song on the new
  device at once (MIDI cannot seek; review r1 #36).

#### 3.4.6 Problems

| Situation | Presentation |
|---|---|
| MIDI device in use by another app (`MMSYSERR_ALLOCATED`) | Warning InfoBar "Another app is using the MIDI synthesizer, so MIDI songs can't play right now. Holiday Lights tries again every 30 seconds." [Try Now]. Tray tooltip line 3: "Music is waiting for the synthesizer". |
| A song fails mid-way or cannot be opened | Skip to the next song after 1 s (5.4 stopped all music). After 3 failures in a row: InfoBar "Music stopped because several songs couldn't be played." [Try Again]. |
| No audio output device | InfoBar "No speakers or headphones are available. Music will start when one is connected." |
| Holiday Lights muted or at 0 in the Windows volume mixer (detected through the WASAPI session of this process; nothing shown if it cannot be determined) | InfoBar "Holiday Lights is muted in the Windows volume mixer." [Open Volume Mixer] (replaces the 5.4 Low MIDI Volume prompt). |
| A song was removed from disk while listed | It disappears from the list; if it was playing, the next song starts. |

### 3.5 Screen Saver [MUST]

Purpose: design what the screen saver shows, and make Holiday Lights the Windows screen saver only when asked.

#### 3.5.1 Wireframe

```
Screen Saver                                                                       [Undo v] [Redo]
When your computer is idle, Holiday Lights can show your bulbs around falling snow or other animations.
[Status card: Holiday Lights isn't your screen saver.        [Use Holiday Lights as My Screen Saver]]
[             Windows Screen Saver Settings...                                                    ]
+Preview -------------------------------+  +Text Message --------------------------------------------+
| live, animated miniature of the main  |  | [Happy Holidays!                                      ]  |
| display: bulbs, snow, Santa Candle,   |  | [                                                     ]  |
| bouncing text                         |  | Font [Arial          v]  Size [36 v]  [B] [I] [U] [S]  |
| [Preview Screen Saver]                |  | [##] [Text Color...]                            [Clear]  |
+---------------------------------------+  +---------------------------------------------------------+
+Animation -----------------------------------------+  +Background Picture --------------------------+
| (None) Angel Balloons Baubles Carolers Dancing ... |  | (None) Cake EasterBunny Flag Hearts ...     |
| (animated tiles, 25 + the chosen bulb)             |  | (thumbnails; + your pictures)        [...]  |
| [Use a Bulb as the Animation...]                   |  | Placement [Center v]   [Add...]            |
| Style [Bounce Off Sides v]                         |  | [##] [Background Color...]                  |
+----------------------------------------------------+  +---------------------------------------------+
+Displays and Motion ----------------------------------------------------------------------------------+
| Show On [All Displays v]      [x] Smooth Motion                                                       |
+------------------------------------------------------------------------------------------------------+
[Load Theme...]  [Save Settings As Theme...]
```

#### 3.5.2 Status card

Read on page show, on window activation and after every change. One variant:

| Condition | Text | Controls |
|---|---|---|
| `SCRNSAVE.EXE` is another saver or empty | (informational) "Holiday Lights isn't your screen saver." | "&Use Holiday Lights as My Screen Saver", link "Windows Screen Saver Settings..." |
| It is our `.scr` and the saver is active | (success) "Holiday Lights is your screen saver." | "Start Afte&r" combo (1, 2, 3, 5, 10, 15, 20, 25, 30, 45 minutes, 1 hour, 2 hours, plus the current value if different), "&Stop Using It", link |
| It points at Holiday Lights 5.4 (string 2 of the file is "TigerTechHolidayLights", or the file is missing and its name matches `HOLIDA~1.SCR` / `Holiday Lights.scr`; this PC: `C:\WINDOWS\system32\HOLIDA~1.SCR`) | (warning) "Your screen saver is still set to Holiday Lights 5.4, which can't start on this version of Windows." | "Use the &New Screen Saver" |
| Ours, but the screen saver is turned off (`ScreenSaveActive` = 0) | (warning) "Holiday Lights is selected, but the screen saver is turned off." | "&Turn It On" |
| A policy value exists under `HKCU\Software\Policies\Microsoft\Windows\Control Panel\Desktop` | (informational) "Your organization manages the screen saver on this PC." | link only; the look can still be designed |

* "Use Holiday Lights as My Screen Saver" / "Use the New Screen Saver" remember the previous `SCRNSAVE.EXE`,
  `ScreenSaveActive` and `ScreenSaveTimeOut`, then set `SCRNSAVE.EXE` to the **full long path** of the installed
  `Holiday Lights.scr`, `ScreenSaveActive` = 1 and keep the current timeout (10 minutes if none), through
  `SystemParametersInfo(..., SPIF_UPDATEINIFILE | SPIF_SENDCHANGE)`. One undo step.
* "Stop Using It" restores the remembered values (a remembered broken 5.4 path is not restored; "(None)" is used).
* "Start After" writes `SPI_SETSCREENSAVETIMEOUT`.
* "Windows Screen Saver Settings..." opens `control desk.cpl,,@screensaver` (password on resume lives there).
* These are the **only** controls that change Windows' screen saver settings.

#### 3.5.3 Preview

A live light stage of the screen saver for the main display (the real simulation at the display's DIP size, drawn
scaled), updated instantly on every change, running only while visible. "&Preview Screen Saver" (5.4 label) runs the
real full-screen saver in-process on the displays chosen in "Show On", **without changing any Windows setting** (5.4
bug fixed); any key, mouse button or pointer movement over 4 DIP ends it; music follows "Play the Chosen Songs" as if
the saver were running. Double-clicking an animation or picture tile does the same (5.4). Ctrl+Enter = Preview.
The miniature also shows the main display's top-left corner enlarged in an inset over that corner (one display DIP per
pixel, 40 % of the miniature, same simulation) so the bulb art is legible (PO decision 8). When the saver cannot start,
the page shows "The screen saver couldn't start." and the button works again.

#### 3.5.4 Text Message

| Control | Default | Behavior |
|---|---|---|
| Text box (multi-line, 255 characters) | "Happy Holidays!" (or theme/imported) | Leading spaces and blank lines are removed as you type (5.4). Live. |
| "Clear" | | Empties the message (5.4). |
| "&Font" picker | Arial | Installed families, each in its own face. A theme font that is not installed shows as "Creepy (not installed - using Chiller)" and the saver uses the substitute (6.2.3). |
| "Si&ze" | 36 pt | 18-100 pt (5.4 limits); list 18, 20, 24, 28, 32, 36, 40, 48, 60, 72, 96, 100; any value in range can be typed. 36 pt = 48 DIP (5.4 stored -48 px). |
| "Bold", "Italic", "Underline", "Strikeout" toggles | Bold on, others off | The effects of the 5.4 ChooseFont dialog (CF_EFFECTS). |
| "Text &Color..." with a swatch | red #FF0000 | Opens the Color dialog (3.8.3). |

#### 3.5.5 Animation

* Animated tiles (88 x 88) in the 5.4 list order (the sorted list box): "(None)", "Angel", "Balloons", "Baubles",
  "Carolers", "Dancing Demon", "Dreidels", "Easter Eggs", "Flags", "Flight Lights", "Gingerbread Man", "Halloween",
  "Happy Faces", "Heavens Above", "Leaves", "Santa", "Schtanna", "Shamrocks", "Singing Tree", "Skeleton", "Snow",
  "Snow Family", "Snow Flakes", "Thanksgiving", "Valentine's Hearts". Default "Snow".
* "Use a Bulb as the Ani&mation..." opens Choose a Bulb (3.8.4; add-on bulbs, as 5.4). The chosen bulb appears as an
  extra tile "Bulb: <name>" and is selected. If that bulb is later removed: "(None)" and the InfoBar "The bulb used
  for this animation was removed."
* "St&yle" combo: "Bounce Off Sides", "Gravity Well", "Falling Leaves", "Attraction". Disabled for (None), Snow, Snow
  Flakes and Balloons, with the description "<Animation> has its own movement that can't be changed." Picking Leaves
  selects Falling Leaves; Easter Eggs or Happy Faces selects Gravity Well; Halloween or Heavens Above selects
  Attraction; every other animation keeps the current style (5.4).
* Keyboard: arrows move between tiles, Space/Enter selects, Ctrl+Enter previews.

#### 3.5.6 Background Picture

* Thumbnail tiles: "(None)" first, then the 11 bundled pictures A-Z (Cake, Easter Bunny, Flag, Hearts, New Year
  Clock, Pot of Gold, Pumpkin, Santa Candle, Snowman, Turkey, Witch - the real picture, never the 5.4 license notice),
  then your My Pictures folder A-Z. Default "Santa Candle".
* "Placem&ent" combo: "Center" (x centered, y at one third of the free height when the picture is shorter than the
  screen; 5.4), "Tile", "Stretch" (5.4); "Fit" [NICE] (largest size without distortion, background color around it).
* "&Add..." (5.4) - "Picture Files" (`*.bmp; *.jpg; *.jpeg; *.gif; *.png`), multi-select, copied into My Pictures; the
  first one is selected. Same name, different content: "<name> (2)".
* "&Background Color..." (5.4 label) with a swatch - default black; opens the Color dialog. Part of themes; Cancel
  now restores it (5.4 bug fixed).
* Tile context menu: "&Preview Screen Saver" (bold, 5.4), "&Remove Picture" (5.4; yours: holding folder + snackbar
  Undo; bundled: hidden), "Show in &Folder". "..." menu: "Open My Pictures Folder", "Restore Removed Pictures".
* A picture file that went missing shows a warning tile "Picture not found"; the saver then runs without a picture
  (5.4).

#### 3.5.7 Displays and Motion

| Control | Default | Behavior |
|---|---|---|
| "Show &On" combo: "All Displays", "Main Display Only" | All Displays | All Displays: every display shows the background color, the animation and its own frame of bulbs; the picture and the message appear on the main display. Main Display Only: other displays stay black (the 5.4 behavior). |
| "Smooth Mot&ion" | on (off in Classic 2003) | Sprites and text are drawn at the display refresh rate with positions interpolated between the classic 60 ms simulation steps. Off = 16 steps per second, as 5.4. Same setting as General > Bulb Look. |

### 3.6 Themes [MUST]

Purpose: load, save and manage themes; change themes automatically on holidays; get back replaced settings.

#### 3.6.1 Wireframe

```
Themes                                     [Save Settings As Theme...] [Restore Built-In Themes...]
A theme is a collection of saved bulb, music and screen saver settings. Loading a theme will replace your
current bulb, music and screen saver settings.
+Automatic Themes -------------------------------------------------------------------------------------+
| Change Themes Automatically on Holidays                                               [ On  (o)]    |
| On a holiday, its theme replaces your bulb, music and screen saver settings. Your previous settings |
| are kept in Recent Settings.                                                                         |
| Today: Halloween (until Oct 31). Next: Thanksgiving on Nov 1.                    [Edit Holidays...] |
| [x] Tell Me When the Theme Changes                                                                   |
+------------------------------------------------------------------------------------------------------+
My Themes
+---------------------------+
| [preview]                 |
| Grandma's Lights          |
| Twinkle - 12 songs,       |
| Always - Santa            |
+---------------------------+
Holiday Lights 5.4 Themes
+------------------+ +------------------+ +------------------+ +------------------+ +------------------+
| [preview]        | | [preview]        | | [preview]        | | [preview]        | | [preview]        |
| Blank Slate      | | Chanukah         | | Christmas 1      | | Christmas 2      | | Easter Eggs      |
| Flash Together - | | Bulb Chase -     | | Flash Together - | | Flash Together - | | Alternating -    |
| no music - (None)| | 3 songs, Inter...| | 31 songs, Always | | 31 songs, Always | | no music - Easter|
|                  | |                  | | Nov 27 - Dec 30  | |                  | | Mar 14 - Mar 29  |
+------------------+ +------------------+ +------------------+ +------------------+ +------------------+
 ... Halloween (Current), July 4th, New Year, St. Patrick's Day, Thanksgiving, Valentine's Day
New Themes
 [Autumn Harvest] [Bubble Lights] [Christmas Twinkle] [Classic Lights] [Holiday Party] [Spring Garden]
 [Summer Nights] [Winter Wonderland]
> Recent Settings
```

#### 3.6.2 Theme cards

* Groups (each hidden when empty): **"My Themes"** (themes you saved, and 5.4 themes that are not installer
  themes), **"Holiday Lights 5.4 Themes"** (the 11 installer themes, exact names), **"New Themes"** (the 8 themes new
  in 6.0, 7.2). A-Z inside each group. All are ordinary themes: any can be loaded, replaced, renamed, duplicated or
  deleted (5.4 let you delete or replace the installer themes too).
* Card 200 x 176 (was 168, so the summary has two full lines; review r1 #29): preview 184 x 104 (the theme's arrangement on the main display's geometry and wallpaper, static
  frame 0; animated with the theme's own pattern while hovered or focused); **name**; summary "<pattern> - <n>
  songs, <play mode> - <animation>" ("no music" when no song is checked).
* Badges: "Current" (accent; the current settings equal the theme's 13 values); "Changed" (a shipped theme whose
  values differ from the original); the calendar date range of this year ("Oct 1 - Oct 31") when a checked holiday
  uses it; "1 bulb missing" (warning; tooltip names it).
* Click, Enter or double-click **loads** the theme: the values are copied (6.4.2), the lights do the theme transition
  (4.4), Recent Settings records the replaced values, Automatic themes turn off when they were on, and a snackbar says
  "<theme> theme loaded." [Undo] (plus " Automatic themes are off." when that happened, with "Turn Back On" in the
  InfoBar at the top of the page). Cancel in Settings also undoes it (5.4).
* "..." button (hover/focus) and Shift+F10 menu: "&Load", "&Rename...", "&Duplicate" ("<name> Copy"), "E&xport..."
  [NICE], "Restore &Original" (changed shipped themes), "&Delete". Delete asks nothing: the theme goes to the
  holding folder; snackbar "Deleted <name>." [Undo]; Cancel restores it. Rename: inline text box on the card, same
  rules as Save Theme.
* Keyboard: the grid is one tab stop; arrows move; Enter loads; F2 renames; Delete deletes; Shift+F10 menu.
* Empty state (all deleted): "There are no themes. Save your settings as a theme, or click Restore Built-In Themes."

#### 3.6.3 Save Theme dialog (5.4)

```
+ Save Theme ---------------------------------------- [X] +
| Save the current bulb, music and screen saver settings  |
| as:                                                     |
| [Christmas 1                                         ]  |
|                                      [ Save ] [ Cancel ]|
+---------------------------------------------------------+
```

Opened by "Save Settings As Theme..." (Bulb Factory, Music Box, Screen Saver, Themes; 5.4 places). Pre-filled with
the last loaded or saved theme name, all selected (5.4). 1-63 characters after trimming; may not contain
\ / : * ? " < > |; "Save" disabled until valid, with an inline note for invalid characters. An existing name
(case-insensitive) asks "Replace Theme?" / "A theme named "<name>" already exists.\n\nDo you want to replace it?"
(5.4); No keeps the dialog open. Saving writes the 13 current values; one undo step.

#### 3.6.4 Automatic themes and the Theme Calendar dialog

**Automatic Themes card** (Themes page):

| Control | Default | Behavior |
|---|---|---|
| "Change Themes &Automatically on Holidays" toggle | on for newcomers and factory-default 5.4 imports; off for customized 5.4 imports | 5.11. Same setting as the tray item "Automatic (... Today)". |
| Status line | | "Today: Halloween (until Oct 31). Next: Thanksgiving on Nov 1." / "Today: between holidays (Classic Lights). Next: Halloween on Oct 1." / off: "Automatic themes are off. Your lights stay as they are." |
| "&Edit Holidays..." | | Opens the Theme Calendar dialog. |
| "&Tell Me When the Theme Changes" | on | One notification per switch (3.12). |

**Theme Calendar dialog** (caption "Theme Calendar", 760 x 620, modal; changes apply at once and are undoable):

```
+ Theme Calendar --------------------------------------------------------------------------- [X] +
| When automatic themes are on, your lights change on these dates.                               |
|                                                                                                 |
|  Use  Holiday             Dates                                 This Year         Theme         |
|  [x]  New Year            [Dec 31] to [Jan 1]                   Dec 31 - Jan 1    [New Year   v]|
|  [x]  Valentine's Day     [Feb 1] to [Feb 14]                   Feb 1 - Feb 14    [Valentine's v]|
|  [x]  St. Patrick's Day   [Mar 10] to [Mar 17]                  Mar 10 - Mar 17   [St. Patri.. v]|
|  [x]  Easter              14 days before Easter to Easter Monday Mar 14 - Mar 29  [Easter Eggs v]|
|  [x]  July 4th            [Jul 1] to [Jul 5]                    Jul 1 - Jul 5     [July 4th   v]|
|  [x]  Halloween           [Oct 1] to [Oct 31]                   Oct 1 - Oct 31    [Halloween  v]|
|  [x]  Thanksgiving        Nov 1 to Thanksgiving Day             Nov 1 - Nov 26    [Thanksgivi v]|
|  [x]  Christmas           Day after Thanksgiving to Dec 30      Nov 27 - Dec 30   [Christmas 1 v]|
|  [ ]  Chanukah            The eight nights of Chanukah          Dec 4 - Dec 12    [Chanukah   v]|
|  [ ]  Winter              [Jan 2] to [Feb 28]                   Jan 2 - Feb 28    [Winter Won v]|
|  [ ]  Spring              [Mar 1] to [May 31]                   Mar 1 - May 31    [Spring Gar v]|
|  [ ]  Summer              [Jun 1] to [Aug 31]                   Jun 1 - Aug 31    [Summer Nig v]|
|  [ ]  Autumn              [Sep 1] to [Nov 24]                   Sep 1 - Nov 24    [Autumn Har v]|
|  Check the holidays you celebrate. When dates overlap, the shorter one wins.                   |
|  Between holidays, show [Classic Lights v]          Holidays for [United States v]             |
|  Today: Halloween. Next: Thanksgiving on Nov 1.                                                |
|  [Reset to Defaults]                                                                [ Close ]  |
+-------------------------------------------------------------------------------------------------+
```

* Fixed-date rows have two month/day pickers. Computed rows (Easter, Thanksgiving, Christmas in the US, Chanukah)
  show their rule as text and a "Use Fixed Dates" link that converts them to pickers.
* "This Year" shows the dates of the next occurrence that has not ended.
* "Between holidays, show": any theme (default "Classic Lights"). There is no "no lights" choice.
* "Holidays for": the Windows region by default; changes Thanksgiving, July 4th, the Easter computus and the
  hemisphere of the four season rows (5.11).
* "Reset to Defaults" restores every row, the between-holidays theme and the region-derived checks.
* [NICE] "Add Holiday..." for custom fixed-date rows.

#### 3.6.5 Restore Built-In Themes dialog

Lists the shipped themes (11 classic + 8 new) that are missing or differ from the original, each with a check box
(all checked): "Bring back these themes as they were originally:". [Restore] [Cancel]. Restoring overwrites
same-named themes (one undo step). Also reachable from General > Reset and Uninstall.

#### 3.6.6 Recent Settings

Expander at the bottom of Themes, collapsed by default: "Recent Settings - Holiday Lights keeps your settings from
before the last 5 theme changes." Before any theme replaces the current values (Themes page, Home, tray, Automatic
themes, Welcome card, "Use My 2003 Lights", "Start Fresh Instead", "Reset All Settings"), those values are recorded
here, newest first: "Before Halloween (automatic) - Oct 1, 2026 12:00 AM" [Restore]. Restoring applies them (and
records the values it replaces). The 5.4 import adds "Holiday Lights 5.4 Settings".

### 3.7 General [MUST]

Purpose: everything rarely changed. Groups in this order:

```
General                                                                            [Undo v] [Redo]
+Where Bulbs Are Drawn --------------------------------------------------------------------------------+
| +---------------------------+ +---------------------------+ +---------------------------+            |
| | (illustration)            | | (illustration)            | | (illustration)            |            |
| | (o) On Desktop, Behind    | | ( ) On Desktop, In Front  | | ( ) On Top of All Windows |            |
| |     the Icons             | |     of the Icons          | |                           |            |
| | Like Holiday Lights 2003: | | On your desktop, over     | | Always visible. Clicks go |            |
| | on your wallpaper, under  | | your icons, under every   | | right through the bulbs.  |            |
| | your icons. Recommended.  | | window.                   | |                           |            |
| +---------------------------+ +---------------------------+ +---------------------------+            |
| Ctrl+Alt+Shift+B switches between On Desktop and On Top.                                             |
+------------------------------------------------------------------------------------------------------+
+Displays ---------------------------------------------------------------------------------------------+
|  +-------------+ +-------------+      Standard Bulbs are 48 px tall on Display 1 and Display 2.     |
|  | [x] 2       | | [x] 1 Main  |      [Identify]   Main Display Only (Like 2003)                     |
|  +-------------+ +-------------+                                                                     |
+------------------------------------------------------------------------------------------------------+
+Bulb Look --------------------------------------------------------------------------------------------+
| Look   [Modern Glow] [Bright Glow] [Classic 2003] [Custom]                                           |
| Bulb Size  ( ) Small  (o) Standard  ( ) Large  ( ) Extra Large                                       |
| Pixels  (o) Smooth  ( ) Crisp      Glow [Soft v]     [x] Smooth Fading   [x] Smooth Screen Saver Motion|
+------------------------------------------------------------------------------------------------------+
+Startup Settings -------------------------------------------------------------------------------------+
| [x] Automatically Start Holiday Lights                                                               |
+------------------------------------------------------------------------------------------------------+
+Hot Keys ---------------------------------------------------------------------------------------------+
| [x] Switch Between On Desktop and On Top      Ctrl + Alt + Shift + B     [Change...]                 |
| [ ] Turn the Lights On or Off                 Ctrl + Alt + Shift + L     [Change...]                 |
+------------------------------------------------------------------------------------------------------+
+When the Lights Rest ---------------------------------------------------------------------------------+
+Accessibility ----------------------------------------------------------------------------------------+
+Files and Folders ------------------------------------------------------------------------------------+
+Holiday Lights 5.4 -----------------------------------------------------------------------------------+
+Reset and Uninstall ----------------------------------------------------------------------------------+
```

| Group | Control | Default | Behavior |
|---|---|---|---|
| **Where Bulbs Are Drawn** | Three illustrated option cards (one radio group): "On Desktop, Behind the Icons", "On Desktop, In Front of the Icons", "On Top of All Windows", with the captions shown | Behind the Icons | The same settings as Bulb Drawing (3.2.8) shown as the three layer modes (5.1). Illustrations: 160 x 96 vector drawings (wallpaper, two icon tiles, a window, a string of bulbs) showing the stacking. While a fallback is active, the chosen card shows the status line and "Try Again". The hot key line names the current location hot key (hidden when it is off). |
| **Displays** | Diagram of the real display arrangement, each display with its number, "Main", a "Show Lights" check box, tooltip "Display 2 - DELL U2723QE - 3840 x 2160 - 150 %" | all checked | Per-display on/off, remembered per monitor device id; a newly connected display is on. The last checked display cannot be unchecked. With one display the diagram is replaced by "One display: 3840 x 2160 at 150 %." |
| | "&Identify" | | Shows a large number on each display for 3 s (drawn by the lights layer, click-through). |
| | "Main Display Only (Like 2003)" link | | Unchecks every display but the main one (one undo step). |
| | Caption | | "Standard Bulbs are <n> px tall on Display 1 and Display 2." (or per display when they differ). |
| | [MUST] "Frame:" radios "Each Display" / "All Displays Together" | Each Display | 5.2.4 (PO-1). |
| **Bulb Look** | "Loo&k" segmented: "Modern Glow", "Bright Glow", "Classic 2003", "Custom" | Modern Glow | Modern Glow = Pixels Smooth, Glow Soft, Smooth Fading on, Smooth Screen Saver Motion on. Bright Glow = Smooth, Bright, on, on. **Classic 2003 = Crisp, Off, off, off** ("Crisp pixels, no glow, no fading and 16-steps-per-second screen saver motion - exactly like 2003."). "Custom" is selected automatically when the four values match no preset; choosing it does nothing. |
| | "Bulb Si&ze" radios "Small", "Standard", "Large", "Extra Large" | Standard | 5.3.2. Same setting as Home. |
| | "&Pixels:" radios "Smooth" ("Clean, smooth edges at any size.") / "Crisp" ("Square pixels, like 2003.") | Smooth | 5.3.3. |
| | "&Glow:" combo "Off", "Soft", "Bright" | Soft | 5.9. "Lit bulbs give off a soft light that brightens the wallpaper around them." |
| | "S&mooth Fading", "Smooth Screen Saver Mo&tion" | on, on | Same settings as Flash Settings and Screen Saver. |
| **Startup Settings** (5.4) | "Automatically &Start Holiday Lights" + "If this box is checked, Holiday Lights will run every time you sign in to Windows. Uncheck the box to use Holiday Lights only as a screen saver. You can still start it at any time from the Start menu." (5.4 text updated) | newcomers and factory-default 5.4 imports: on (shown on the Welcome card); customized imports: the 5.4 state (a `*Holiday Lights*.lnk` in the Startup folder) | Applied at once (5.4 waited for OK). Writes or removes the per-user Run value (6.6.1). If Windows disabled it (`StartupApproved\Run` first byte odd): InfoBar "Startup is turned off for Holiday Lights in Windows Settings." [Open Startup Apps] (`ms-settings:startupapps`); never written. |
| **Hot Keys** | Row "Switch Between On Desktop and On Top": check box, key caps, "Change..." | on, Ctrl+Alt+Shift+B (imported: the 5.4 letter and on/off) | The 5.4 hot key's job (6.6.4). Check box registers or unregisters at once. If registration fails: the row shows (warning) "Another program is using this key combination. Choose another one." and the check box stays checked but inactive. |
| | Row "Turn the Lights On or Off" | off, Ctrl+Alt+Shift+L | Toggles Show Lights. |
| | Text | | "You can press a hot key at any time, in any program. Use the first one to move the bulbs on top of your windows when you need to see something underneath them, and back again." (5.4 text updated) |
| | One-time InfoBar (imported users whose 5.4 hot key was on) | | "Holiday Lights 5.4 used Ctrl+Shift+B, which web browsers use for the bookmarks bar, so your hot key is now Ctrl+Alt+Shift+B." [Use Ctrl+Shift+B Anyway] [x]. |
| **When the Lights Rest** | "Hide the Lights on a Display While a Full-Screen App or Game Is Running There" | on | 5.12.2. |
| | "Hide the Lights During Presentations" | on | |
| | "When &Energy Saver Is On:" combo "Use Less Power", "Stop Flashing", "Turn Off the Lights", "Change Nothing" | Use Less Power | 5.12.3. |
| | "Pause Music While a Full-Screen App, Game or Presentation Is Running" | on | |
| | "Pause Music During Focus Sessions" | on | |
| | "Pause Music When I Lock My PC" | on | |
| | Text | | "The lights always rest while your PC is locked, while the screen is off, while another screen saver runs, and during Remote Desktop sessions." |
| **Accessibility** | "&Limit Flashing to 3 Flashes per Second" + "Recommended if flashing lights bother you." | on if Windows "Animation effects" is off at first run, else off | 5.5.3. |
| | "&Decorate the Settings Window with Lights" | on | The string of lights (4.3.1). |
| | Text | | "Animations in Holiday Lights windows follow Windows' "Animation effects" setting. The lights on your desktop keep flashing." |
| **Files and Folders** | Rows "My Bulbs", "My Music", "My Pictures" with the path and "Open" | `Documents\Holiday Lights\Bulbs`, `\Music`, `\Pictures` | Opens the folder in Explorer (created if missing). |
| | "Open &Bulb Files (.bul) with Holiday Lights" | on | Registers or removes the per-user association (6.9). |
| **Holiday Lights 5.4** (only when the 5.4 key exists) | Status line | | "Imported on Oct 8, 2026: your settings, 11 themes, 0 bulbs, 0 songs and 0 pictures." + "Import Details..." (3.8.5) + "Import Again..." (3.8.1). |
| | Leftover fixes (each shown only when it applies) | | "Holiday Lights 5.4 is running too. It doesn't work properly on this version of Windows." [Close Holiday Lights 5.4]; "Holiday Lights 5.4 also starts with Windows." [Turn Off]; "Your screen saver is still set to Holiday Lights 5.4." [Fix It] (6.8.3). |
| **Reset and Uninstall** | "&Reset All Settings...", "Restore Built-In Themes...", "&Uninstall Holiday Lights..." | | 3.8.1, 3.6.5. |

### 3.8 Dialogs

#### 3.8.1 Confirmations [MUST]

The only confirmations in the program (everything else is undoable). ContentDialogs in the owning window; the
destructive button is on the left and never the default; Esc = Cancel.

| Title | Text | Buttons |
|---|---|---|
| "Reset Holiday Lights?" | "This puts every setting back the way it was when Holiday Lights was installed. Your themes, bulbs, songs and pictures are kept, and Recent Settings keeps your current settings." | [Reset] [Cancel] |
| "Uninstall Holiday Lights?" | "Uninstalling removes Holiday Lights from your PC. Your bulbs, songs and pictures in Documents\Holiday Lights are kept unless you choose to remove them." | [Uninstall] [Cancel] (runs the per-user uninstaller, 6.10, and exits) |
| "Import Holiday Lights 5.4 Settings Again?" | "Your current bulb, music, screen saver and hot key settings will be replaced by the ones from Holiday Lights 5.4. Your themes are kept; 5.4 themes you don't have yet are added." | [Import] [Cancel] |
| "Replace Theme?" | "A theme named "<name>" already exists.\n\nDo you want to replace it?" (5.4) | [Yes] [No] |
| "Discard Changes?" | "Your changes to <name> will be lost." | [Discard] [Keep Editing] |
| "Reset Holiday Lights?" (command line `--reset` / `reset`) | as the first row | [Reset] [Cancel] |

#### 3.8.2 Change Hot Key dialog [MUST]

```
+ Change Hot Key ------------------------------------------ [X] +
| Press the keys you want to use for:                          |
| Switch Between On Desktop and On Top                         |
| +----------------------------------------------------------+ |
| |               Ctrl + Alt + Shift + B                     | |
| +----------------------------------------------------------+ |
| (message line)                                               |
| [Use Default]                              [ Save ] [ Cancel ]|
+--------------------------------------------------------------+
```

* Records the next key combination. Accepted keys: A-Z, 0-9 (top row and numeric keypad; 5.4 refused the keypad),
  F1-F24, Insert, Home, End, Page Up, Page Down, Pause. Esc cancels (it cannot be a hot key); Tab leaves the field.
* A combination must contain Ctrl, Alt or Windows, unless the key is F1-F24 or Pause. Shift + a letter alone is
  refused: "That would stop you from typing capital letters."
* Message line (first that applies; Save disabled for the first two):
  * "Another program is using this combination." (a trial `RegisterHotKey` failed);
  * "This combination types "Ł" on your Polish (Programmers) keyboard. Choose another." (AltGr check: a combination
    containing Ctrl+Alt is refused when `ToUnicodeEx` with that modifier state produces a character on any installed
    layout from `GetKeyboardLayoutList`; re-checked when keyboard layouts change);
  * "Ctrl+Shift+B is the bookmarks-bar shortcut in web browsers. Holiday Lights would take it from them." (also for
    Ctrl+Shift+T, Ctrl+Shift+N, Ctrl+Shift+Delete; Save stays enabled);
  * "Windows uses many Windows-key shortcuts; this one might stop working." (any Windows-key combination).
* "Use Default" restores the default combination. The recorded combination is announced ("Ctrl Alt Shift B").

#### 3.8.3 Color dialog [MUST]

Caption "Color". 48 basic colors (the classic ChooseColor set), 16 "Custom Colors" (persisted; imported from 5.4
"Custom Color 0-15"), a hue/saturation field with a brightness slider, a "#RRGGBB" box, old and new color preview,
"Add to Custom Colors", [OK] [Cancel]. Fully keyboard operable (arrows move in the swatch grids and the field). The
custom colors are part of the Cancel snapshot.

#### 3.8.4 Choose a Bulb dialog [MUST]

Caption "Choose a Bulb", 900 x 640. The Bulb List (search, Show, Sort, Tiles/Details, selected-bulb details) limited
to add-on bulbs (5.4 allowed add-on bulbs as animations); [Choose] [Cancel]; double-click or Enter chooses.

#### 3.8.5 Import Details dialog [MUST]

Caption "Imported from Holiday Lights 5.4". A read-only list of every 5.4 item with "Imported", "Already included"
or "Not imported - <reason>" (6.8.2), and the date. [Copy] [Close].

### 3.9 About Holiday Lights window [MUST]

```
+ About Holiday Lights ------------------------------------------------------- [X] +
| ~o~o~o~o~o~o~o~o~o~o~o~o~o~o (ABOUTFLASH strip, alternating every 500 ms)        |
|            HOLIDAY LIGHTS   (the 5.4 ABOUT banner, 480 x 94 DIP)                 |
|                                                     Modern Edition 6.0          |
| Holiday Lights - Modern Edition, version 6.0.0                                  |
| Holiday Lights 6 — the modern edition — was created by StarrLord.                |
| Visit the project at github.com/starrlord/holidaylights.   (link)                |
| Free for everyone: no trial, no registration, no internet connection.            |
| Open source under the MIT License.                                               |
| Based on Holiday Lights 5.4 for Windows. Copyright 1993-2003 Tiger Technologies.|
| Holiday Lights is a registered trademark of Tiger Technologies.                 |
| > Built-In Bulb Artists                                                          |
| > Add-On Bulb Artists (about 410)                                                |
| > Music                                                                          |
| > Screen Saver Art                                                               |
| > Software                                                                       |
| All artwork and music are copyrighted by their authors and may not be used for  |
| other purposes without their permission.                                        |
| [Copy Version Info]                                                    [ OK ]   |
+---------------------------------------------------------------------------------+
```

* 640 x 720, fixed size, modeless, single instance (5.4 About was modeless).
* Banner: RT_BITMAP `ABOUT` (480 x 94) scaled with MMPX + area averaging; its pale-blue background (#DDEEFF) is kept in
  light and dark mode (it is artwork, framed by an 8 DIP rounded border). Above it the `ABOUTFLASH1`/`ABOUTFLASH2`
  strip alternates every 500 ms (5.4), with the glow and fading of the current Look. Reduced motion: `ABOUTFLASH1`
  only. "Modern Edition 6.0" is drawn over the banner where 5.4 printed "version 5.4".
* The Tiger Technologies `LOGO` bitmap is not shown (it could imply endorsement); Tiger Technologies is credited by
  name.
* Creator line: "Holiday Lights 6 — the modern edition — was created by StarrLord." The next line links to the
  project's home page (https://github.com/starrlord/holidaylights, shown as "github.com/starrlord/holidaylights"); the
  link opens in the default browser only when the user follows it (mouse, or Tab and Enter) and is the only web link
  in the product. The licence line says "Open source under the MIT License."
* Expanders (content in 6.5; keyboard: Tab reaches them, Enter or Space opens). The add-on artist list has its own
  search box; selecting an artist [NICE] filters the Bulb List to their bulbs.
* "&Copy Version Info" copies: version, Windows build, the display list (size, scale, position, work area), the
  effective layer mode per display and the requested one, the MIDI device - for bug reports.

### 3.10 Holiday Lights Help window [MUST]

* 960 x 720, resizable, modeless, single instance. Left: search box and the Contents tree; right: the topic rendered
  from bundled Markdown, with the 5.4 help banner art `bm0` at the top of every topic (5.4 look). Back / Forward
  buttons (Alt+Left / Alt+Right). Topics end with "Open the <page> page" buttons that open Settings there.
* Contents: the 5.4 books, with the payment book removed and new books added (topic list in 6.7).
* F1 in Settings and the "Help" button open the topic for the current page or the focused section; F1 in Bulb
  Editing opens "Making Your Own Bulbs". Ctrl+F searches titles and text.
* Topic texts are the 5.4 texts updated: every "Start > Programs", "Display Properties", "Sndvol32", "Internet
  Explorer 5", payment and tigertech.com reference is rewritten; "up to five different kinds of bulbs on each side"
  becomes "up to six"; the "click once (not twice)" advice for the tray icon stays true and stays in.

### 3.11 Welcome card [MUST]

A small non-modal window (caption "Welcome to Holiday Lights", 560 x 640 DIP, not resizable, Mica, centered on the
main display so the screen edges and their lights stay visible). Shown once, after the first-run power-up. Never
blocks the lights; closing it with X, Esc, "Done" or "Open Settings" keeps everything as shown.

```
+ Welcome to Holiday Lights --------------------------------------------- [X] +
| ~o~o~o~o~o~o~o (ABOUTFLASH strip)                                           |
|                   HOLIDAY LIGHTS  (5.4 banner)                              |
| Your desktop is decorated!                                                  |
| Holiday Lights lives in the notification area next to the clock: look for  |
| the red bulb. Click it any time to change your lights.                      |
| Don't see it? Select the ^ next to the clock. You can drag the bulb onto   |
| the taskbar to keep it in view.                                             |
|   [picture of the taskbar corner: ^ flyout with the red bulb]               |
| Choose a Theme                                                              |
| [Automatic   ] [Christmas 1 ] [Thanksgiving] [New Year    ]  More Themes... |
| [Halloween   ]                                                              |
| [today       ]                                                              |
| Play Holiday Music                                              [ Off ( )]  |
| Automatically Start Holiday Lights When I Sign In              [ On  (o)]  |
| [Use Holiday Lights as My Screen Saver]                                     |
|                                              [Open Settings]  [  Done  ]    |
+----------------------------------------------------------------------------+
```

| Element | Behavior |
|---|---|
| Heading | Newcomer: "Your desktop is decorated!". 5.4 imports: "Welcome back!". |
| Tray text and picture | As shown; the picture is a vector drawing of the Windows 11 taskbar corner with the "^" flyout open and the red bulb highlighted, in the current light/dark mode. |
| Theme cards (about 120 x 96 DIP so four fit the 496 DIP content row; static previews of the main display's top-left corner at a legible size, animate on hover/focus; PO amendment, review r1 #15/#23) | "Automatic" (selected for newcomers; shows today's theme, "Halloween today"), "Christmas 1" (the 2003 look, always offered), then the next two different calendar themes after today's, skipping Christmas 1 (on Oct 8: Thanksgiving, New Year). Clicking applies at once (the lights change behind the card; Recent Settings records it); choosing a theme turns Automatic themes off and the "Automatic" card turns them back on. Arrow keys move, Enter applies. "More Themes..." opens Settings on Themes. |
| "Play Holiday Music" toggle | The global music switch. Newcomers and factory-default imports: off. Customized imports: on unless 5.4 was "Never"; while the card is open no song starts, and the first song starts when the card closes if the switch is on. |
| "Automatically Start Holiday Lights When I Sign In" toggle | Newcomers and factory-default imports: on. Customized imports: the 5.4 state. |
| "Use Holiday Lights as My Screen Saver" | Newcomers only (when Holiday Lights is not already the saver): same as the Screen Saver page button; becomes "Holiday Lights is your screen saver." with a check glyph. |
| "Open Settings" / "Done" | Close the card; "Open Settings" opens Settings on Home. |

Layout (PO amendment, review r1 #23): the banner is drawn at 70 %, the taskbar picture sits beside the tray text,
"More Themes..." shares the "Choose a Theme" row, the 5.4 lines are one InfoBar-style panel after the choices, and
"Prefer the exact 2003 look?" shares a line with "What's New in 6.0", so the card fits 560 x 640 without scrolling in
every variant but the rarest (factory defaults with 5.4 running, at startup and as the saver: 22 DIP).

**5.4 variants** (InfoBar-style lines inside the card, each with a one-click fix that turns into a check-glyph line
"Done." when it succeeds):

| Line | Shown when | Action |
|---|---|---|
| Factory defaults (2.5.2): "Holiday Lights 5.4 was using its standard settings, so your lights now follow the holidays. Its 11 themes are included." | 5.4 at factory defaults | "Use My 2003 Lights" (2.5.2). |
| Customized (2.5.3): "Your lights, themes and music choices from Holiday Lights 5.4 are here." | customized 5.4 | "Show Import Details" (3.8.5), "Start Fresh Instead" (2.5.3). |
| "Your old screen saver can't start on this version of Windows." | `SCRNSAVE.EXE` points at a 5.4 `.scr` (3.5.2 rule) | "Fix It" = "Use Holiday Lights as My Screen Saver" keeping the user's timeout. |
| "Holiday Lights 5.4 is still running." | `FindWindow("Holiday Lights", "TigerTechHolidayLights")` succeeds | "Close It" sends it `WM_COMMAND 106` (5.4 Exit, as its installer did). |
| "Holiday Lights 5.4 also starts with Windows." | a `*Holiday Lights*.lnk` in the Startup folder targets the 5.4 exe | "Turn Off" moves that shortcut to the Recycle Bin. |
| "Prefer the exact 2003 look?" | always in the 5.4 variants | "Use Classic 2003 Look" sets Look = Classic 2003 (crisp pixels, no glow, no fading). |
| "What's New in 6.0" link | always in the 5.4 variants | Opens Help > What's New in 6.0. |

Accessibility: the heading is announced when the card opens ("Welcome to Holiday Lights. Your desktop is
decorated!"); the card takes focus only because the user just started the program (first run after install).

### 3.12 Notifications, snackbars, InfoBars, on-screen pill [MUST]

**Notifications** (tray balloons via H.NotifyIcon `ShowNotification(..., respectQuietTime: true)`; Windows 11 shows
them as toasts; they have no buttons; at most one of each kind per day unless noted; never during a full-screen app or
a Focus session):

| When | Title | Text | Clicking it |
|---|---|---|---|
| The Settings window is closed for the first time ever | "Your lights stay on" | "Holiday Lights keeps running in the notification area. Click the red bulb to come back." | opens Settings on Home |
| Automatic themes switched the theme (and "Tell Me When the Theme Changes" is on) | the new theme's screen saver message, else "Holiday Lights" ("Happy Halloween!") | "Your lights switched to the Halloween theme. Click to undo or choose another." | opens Themes with the InfoBar "Holiday Lights switched to Halloween on Oct 1." [Undo] [Turn Off Automatic Themes] |
| A hot key could not be registered (once per change) | "Hot key not available" | "Ctrl+Alt+Shift+B is used by another program. Choose another one in General." | opens General |
| The MIDI synthesizer is busy (once per session, only while Music Box is not open) | "Music is waiting" | "Another app is using the MIDI synthesizer. Holiday Lights will try again." | opens Music Box |
| The settings file was unreadable and was replaced | "Settings were reset" | "Your settings file couldn't be read, so Holiday Lights started with defaults. Your themes and bulbs are safe." | opens General |
| The lights renderer recovered from an error (once per session) | "Lights restarted" | "Something went wrong drawing your lights, so they were restarted." | - |

**Snackbars** (3.0.1): after using a bulb for Whole Frame / All Edges / All Corners, Clear All Bulbs, removing a
bulb, song or picture, deleting a theme, loading a theme, adding files, and Reset. Texts in Appendix D.

**InfoBars**: listed with each page; texts in Appendix D.

**On-screen pill** (hot key feedback) [MUST]: drawn by the lights layer as a DirectComposition visual in a topmost,
click-through, non-activating window on the display under the mouse pointer: top center, 24 DIP below the work-area
top, 36 DIP high, dark rounded rectangle (#202020 at 92 %, 18 DIP radius, drawn into its own surface - no DWM corner
preference involved), icon + white text. Fades in 150 ms, holds 1.6 s, fades out 150 ms; never takes focus; not shown
while a full-screen app is in front. Texts: "Bulbs on top of all windows", "Bulbs on the desktop", "Lights on",
"Lights off". The first three times the location hot key is used, the pill adds a second line for 4 s: "Press
Ctrl+Alt+Shift+B again to put them back." (the actual combination). Reduced motion: no fades.

---------------------------------------------------------------------------------------------------------------------

## 4. Visual and motion design

### 4.1 Character: festive, not cheesy

The festive color comes from the 2003 pixel art and from real light. The window chrome is plain Windows 11 Fluent
(Mica, system accent, Segoe UI Variable), so it looks native in light and dark.

| Do | Don't |
|---|---|
| Let the bulbs carry color and motion: animated previews wherever a bulb is shown, the live stage, the string of lights. | Recolor the UI red and green, use novelty fonts in the UI, add snowfall over windows. |
| Use the real 1994-2006 art everywhere a picture is needed (previews, tiles, chips, tray header, theme cards, the About and Welcome banner). | Draw new clip-art mascots or stock illustrations. |
| One celebratory moment per occasion: the first lights-on wave, a theme change, the About banner blinking. | Confetti, pop-ups during work, celebrations on every click. |
| Warm, short copy; the original bulb descriptions verbatim (their 2003 humor is the personality). | Jokes in error messages, exclamation marks everywhere (at most one per screen), emoji. |
| Glow that really adds light to the wallpaper (additive), subtle by default. | Bloom so strong it blurs the art; dark halos to fake glow on light wallpapers. |
| Sound only when the user turned music on. | Click sounds, jingles at start-up. |

### 4.2 Tokens

| Token | Value | Use |
|---|---|---|
| Night well | vertical gradient #14203A to #1F3157, in light and dark mode; High Contrast: system Window color | Behind every bulb picture in the UI (tiles, chips, corner boxes, selected-bulb bar, editor preview, theme cards, tray header). Lit colors pop and black cords stay visible. |
| Night gradient | #0B1530 to #1D3466 | Stage backdrop when no wallpaper is available. |
| Taskbar band | #202020 at 85 % | Taskbar area in stages. |
| Stage bezel | 1 DIP #3A3A3A, 6 DIP radius | Display outlines in stages. |
| Heritage sky | #DDEEFF | Background of the 5.4 ABOUT banner panel (sampled from the bitmap) in About and Welcome. |
| Card | Fluent `CardBackgroundFillColorDefault`, 8 DIP radius, 16 DIP padding | Groups (the 5.4 group boxes). |
| Chip and tile radius | 4 DIP | |
| Spacing | 4 / 8 / 12 / 16 / 24 grid; page padding 24; group gap 16 | |
| Type | Segoe UI Variable: Title 28 (page titles and the Home greeting), Subtitle 20, BodyStrong 14 (group headers), Body 14, Caption 12 (tile names, counts) | The only decorative typefaces are the screen saver message fonts. |
| Selection, focus, drop targets | system accent: 2 DIP accent outline for selection; 10 % accent fill for the hovered drop target; 2 DIP dashed accent outline for valid drop targets; 2 DIP focus visual | |
| Status | Fluent InfoBar severities and glyphs | |
| Illustration palette | bulb red #E3342F, holly green #1F8B4C, warm gold #F4B942, snow white #F5F8FF, ice blue #CFE8FF | App and tray icons, layer-mode illustrations, the taskbar-corner picture. |
| Art scaling in the UI | MMPX + area averaging, or Crisp (the current Pixels setting); never bilinear | 3.0.3. |

### 4.3 Signature elements

#### 4.3.1 The string of lights (Settings window)

A 24 DIP band directly under the title bar, across the whole window: the current arrangement's **top edge** with its
corners, laid out by the real layout engine across the window width at the window's DPI with the bulbs scaled so the
tallest top-edge cell is 20 DIP, animated with the current pattern and fading, with glow only in dark mode (additive
light is invisible on light backgrounds). If the top edge is empty the first non-empty edge is used; with no bulbs
the band collapses. Off when "Decorate the Settings Window with Lights" is unchecked; hidden in High Contrast; frame 0
under reduced motion; not exposed to UI Automation. It is the window's festive signature and a preview of the top
edge.

#### 4.3.2 The power-up wave

Rendered by the lights engine with DirectComposition opacity animations (begin-time offsets per bulb); it never
blocks input and is skipped on resting displays.

| Variant | When | Timeline |
|---|---|---|
| First run | first launch after install (2.5) | 0 ms: every bulb appears dark (light bulbs show their unlit frame, other bulbs at 40 % opacity), fading in over 200 ms. 300 ms: a wave runs clockwise along each display's ring (5.7) from the top-left corner: bulb k reaches full brightness at 300 + 1600 x k / R ms, rising over 120 ms (ease-out), its glow rising over 300 ms. 1900 ms: every light bulb lit with full glow for 300 ms. 2200 ms: the flash pattern starts at step 0. |
| Short | sign-in autostart (1.0 s ring), "Show Lights" turned on (0.8 s), theme transition (0.8 s) | The same wave without the 300 ms hold. |
| Reduced motion | any of the above | All bulbs fade in together over 300 ms. |

#### 4.3.3 Theme transition

When a theme is loaded (by hand or automatically): the old lights fade out over 250 ms, then the new lights do the
short power-up. Reduced motion: a 300 ms crossfade.

#### 4.3.4 Tray menu header

120 x 20 DIP strip of the active top edge in the tray menu (2.2), animated while the menu is open (frame 0 under
reduced motion), with the theme name beside it.

#### 4.3.5 Heritage art

| Art | Where |
|---|---|
| `ABOUT` banner (480 x 94) + `ABOUTFLASH1/2` (296 x 15, alternating every 500 ms) | About (3.9) and the Welcome card (3.11), as in the 5.4 About box. |
| `bm0` help banner ("Holiday Lights" logo with lights) | Top of every Help topic. |
| `WARNING` (32 x 32) | Damaged or undecodable bulb art (5.4). |
| `DITHER` (8 x 8) | The dimmed outside of the Bulb Editing white square (5.4). |
| `FLAKE`, `BALLOON`, `BALLOONSMALL`, saver GIFs 3100-3105, the 11 pictures (second embedded BMP) | Screen saver, unchanged. |
| All bulb art | Unchanged pixels (faithful decoding, PO decision 3); only scaled. |

### 4.4 Motion catalogue

| Motion | Normal | Reduced motion (Windows "Animation effects" off) |
|---|---|---|
| The lights on the desktop | per pattern, speed and Look | **unchanged** - they keep flashing; the separate flash limit (5.5.3) is on by default in this case |
| Power-up wave, theme transition | 4.3.2, 4.3.3 | 300 ms fade / crossfade |
| Lights off, exit | all bulbs fade out over 300 ms, then the layers hide | instant |
| Layer move (Bulb Drawing change, hot key) | fade out 150 ms, move, fade in 150 ms | instant |
| Resting and resuming | fade out / in over 300 ms | instant |
| Arrangement edit | only the affected edges re-lay out; new bulbs fade in 150 ms, removed ones fade out 150 ms; the chip settles (scale 0.8 to 1.0, 150 ms) | instant |
| Page change, flyouts, InfoBars | Fluent defaults (167 ms) | none |
| Snackbar | slide 12 DIP + fade, 200 ms | fade 150 ms |
| Tile hover | lift (scale 1.03 + shadow), 120 ms | none |
| Bulb previews (tiles, chips, edge samples, theme cards, tray header, string of lights) | step with the pattern (3.0.3) | frame 0; tiles and cards animate only while hovered or focused |
| Home and Bulb Factory stages, screen saver preview | step with the pattern; fades at up to 30 fps | stepping continues (they are the requested preview); no try-on animation |
| About / Welcome light strip | alternates every 500 ms | ABOUTFLASH1 still |
| On-screen pill | 150 ms fades | none |
| Peek | instant cloak | instant cloak |
| Screen saver | 60 ms simulation, drawn at the refresh rate with interpolation (Smooth Motion) | unchanged (the saver is opt-in) |

### 4.5 Light, dark and High Contrast

* Window chrome, menus and flyouts follow Windows light/dark and the accent color live (`ThemeMode=System`).
* Night wells, stages, the About banner panel and screen saver previews keep their own colors in both modes (they are
  content).
* Tray icons follow the taskbar's light/dark setting.
* **High Contrast**: chrome uses system colors; night wells become the system Window color; glow is not drawn in UI
  previews; the string of lights is hidden; focus, selection and drop outlines use `SystemColors.HighlightBrush`;
  bulb art itself is unchanged (it is content).

### 4.6 Sound

None besides the music the user turned on. Rationale: Holiday Lights already plays music, and interface sounds in an
office are unwelcome.

### 4.7 Icons

* **App icon**: the 5.4 red C7 bulb (`ICON`) redrawn: red glass, white highlight, gold screw base, green cord stub;
  16, 20, 24, 32, 40, 48, 64, 256 px; 48 px and up add a soft warm glow ring.
* **Tray icons**: lit and unlit, each for light and dark taskbars, 16/20/24/32 px.
* **`.bul` document icon**: the bulb on a page (5.4 icon group 2), redrawn.
* **Layer-mode illustrations**: three 160 x 96 vector drawings (3.7).
* **Glyphs** (Segoe Fluent Icons): Home U+E80F, Lightbulb U+E82F, MusicNote U+EC4F, TVMonitor U+E7F4, Color U+E790,
  Settings U+E713, Help U+E897, Info U+E946, Undo U+E7A7, Redo U+E7A6, Search U+E721, Add U+E710, Delete U+E74D,
  FavoriteStar U+E734 / FavoriteStarFill U+E735, View U+E890, Folder U+E8B7, Edit U+E70F, Play U+E768, Pause U+E769,
  Next U+E893, Volume U+E767, Mute U+E74F, Warning U+E7BA. Code points are verified against the installed font during
  implementation; the names are normative.

### 4.8 Voice and copy

* Second person, short, warm. Title Case for labels (house style), sentence case for everything else.
* Words: "lights" for everything drawn on the desktop, "bulb" for one kind of decoration, "edge" and "corner" for the
  boxes, "flash pattern", "theme". "Flavor" only inside Bulb Editing and Help, always explained.
* Never shown: "strip", "slot" (except "Bulb List Preview" texts kept from 5.4), "phase", "tick", "DComp", "layer
  mode", "fallback".
* Units: "0.30 s", "48 px", "60 %"; dates and numbers per the Windows locale.
* Bulb names and descriptions, song file names and the 2003 credit lines are shown verbatim.

---------------------------------------------------------------------------------------------------------------------

## 5. Lights behavior

### 5.1 Where the lights are drawn (layer modes) [MUST]

One layer per enabled display, covering that display; every bulb is a DirectComposition visual with exact per-pixel
alpha; premultiplied pixels with rgb > alpha add light (glow) in every mode; clicks always pass through
(`ARCHITECTURE.md` §5).

| Bulb Drawing setting | Tried first | Fallback order |
|---|---|---|
| On Desktop + Behind the Desktop Icons (default; PO decision 6) | (a) **behind the icons**: a `WS_EX_NOREDIRECTIONBITMAP` child of the desktop layer parent (Progman on the raised 24H2+ desktop, the wallpaper WorkerW on the classic layout) z-ordered directly below `SHELLDLL_DefView`, DirectComposition content | (b), then (c) |
| On Desktop, box unchecked | (b) **in front of the icons**: a top-level layered, transparent, tool, no-activate window **owned by Progman** at `HWND_BOTTOM`, `DWMWA_EXCLUDED_FROM_PEEK` | (c) |
| On Top | (c) **on top of all windows**: the same window, topmost, not owned, inserted **directly below `Shell_TrayWnd` and every `Shell_SecondaryTrayWnd`** in the topmost band so the taskbar (including an auto-hide taskbar) always stays above the bulbs | - |

* **Host discovery.** Detect the desktop structure (not the Windows version). When no
  wallpaper layer exists yet, send `SendMessageTimeout(progman, 0x052C, 0xD, 0x1, SMTO_NORMAL, 1000)` once per
  attempt - **never with lParam 0**.
* **Fallback and return.** The fallback is used when the host cannot be found or the layer cannot be created or kept.
  The chosen mode is retried on `TaskbarCreated`, when the 2 s maintenance check finds new Progman/DefView handles,
  after unlock, after resume, after display changes, every 30 s while degraded, and at once on "Try Again". When it
  works again the lights move back silently. The tray radio stays on "Bulbs On Desktop" while (b) or (c) stands in
  for (a); Home, Bulb Drawing and General show the status line (3.1) and the tray tooltip says it; never a dialog.
* **Show desktop (Win+D), Peek, virtual desktops.** (a) is part of the desktop host, which Windows brings forward on
  Win+D, so the bulbs stay visible. (b) follows its owner; if after Win+D the layer is found below the desktop host
  (checked on `EVENT_SYSTEM_FOREGROUND` for the desktop), it is moved to the bottom of the topmost band, still below
  the taskbar, until another application window is activated (Rainmeter's technique).
  Top-level layers are excluded from taskbar Peek. Virtual desktops: (a) is shared by all desktops; for (b) and (c)
  the 2 s maintenance check calls `IsWindowOnCurrentVirtualDesktop` and moves a layer to the current desktop when
  needed. These behaviors are verified on the reference PC (acceptance 7.5 #12).
* All layers never take focus, never appear in Alt+Tab or the taskbar, and never receive clicks.
* The location hot key and the tray radios switch between On Desktop and On Top only; "Behind the Desktop Icons" is
  kept.

### 5.2 Displays, multi-monitor and the taskbar

#### 5.2.1 Policy [MUST]

* **Each Display**: every enabled display gets the complete arrangement around its own work area, laid out
  independently in that display's physical pixels and scale (`GetDpiForMonitor(MDT_EFFECTIVE_DPI)`). Any layout
  works: different sizes, mixed DPI, displays left of or above the main display, negative coordinates. On the
  reference PC: two complete frames, one per 4K monitor, both at scale 1.5.
* Per-display on/off (General > Displays), remembered by monitor device id; a newly connected display is on; at least
  one display stays on.
* One arrangement for all displays (5.4 had one arrangement). Per-display arrangements are out of scope.
* All displays share one step counter, so all frames flash in step.

#### 5.2.2 Taskbar [MUST]

Each display's **work area** (`MONITORINFO.rcWork`; 5.4 used `SPI_GETWORKAREA` of the main display) is framed: the
bottom strip sits directly above a bottom taskbar, a top or side taskbar moves the matching strip. With an auto-hide
taskbar the work area is the full display; the taskbar then slides over the bulbs in every mode (5.1 z-order rules).

#### 5.2.3 Rebuilds [MUST]

Triggers: `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, `WM_SETTINGCHANGE(SPI_SETWORKAREA)`, `TaskbarCreated`, the 2 s
maintenance poll (a diff of `{device, rcMonitor, rcWork, dpi}` per display), and changes of arrangement, pattern,
size, Pixels or Look. They are debounced 300 ms; only the affected displays rebuild layout and sprites; the result is
visible within 500 ms.

#### 5.2.4 One frame around all displays [MUST] (PO-1)

"Frame: All Displays Together" treats the enabled displays as one outline (a wreath):

1. Take the enabled displays' work areas in physical pixels.
2. Group them into connected components (rectangles that touch or overlap; gaps of 2 px or less count as touching).
   Each component gets one wreath; a lone display is simply framed.
3. Compute the outline of each component's union (rectilinear polygon; holes ignored).
4. Every outline edge becomes a strip by its outward direction (facing up = Top, down = Bottom, left = Left, right =
   Right). Each edge is cut where it passes from one display to another; each piece uses its own display's scale, so
   **nothing straddles two layer windows**.
5. Convex outline corners receive the corner bulbs, drawn by the horizontal piece as in 5.4; concave corners get none,
   and the vertical piece starts below (or ends above) the horizontal strip's thickness so strips never overlap.
6. Horizontal pieces are laid out first, then vertical pieces between them (5.4 order Top, Bottom, Right, Left).
7. The type/flavor index and the 5.4 chase counter continue across the pieces of one outline edge, so neither the
   colors nor Alternating and Bulb Chase restart at a seam; both restart on every outline edge (as 5.4 restarted per
   strip). Otherwise each piece behaves as a 5.4 strip (own fit, gap and frame count). (Review r1 #46.)
8. Reference PC: one top edge across both displays (two pieces split at x = 0), one bottom edge likewise, the left
   edge on Display 2, the right edge on Display 1, four corners.

### 5.3 Layout and scaling

#### 5.3.1 Classic layout, exactly [MUST]

Per display, the 5.4 algorithm is reproduced, with the golden layouts (`tests/HolidayLights.Tests/Golden/layout`) as
the reference:

* Strips are built in the order Top, Bottom, Right, Left; Top and Bottom span the full width and own the four
  corners; Left and Right fit between them; each strip's thickness is its largest cell; strips touch the work-area
  edges.
* Corners reserve `cellWidth + hSpacing`; side bulbs take `spacing + cellLength + spacing`; leftover pixels spread as
  a fractional gap after every bulb (double precision, truncated per bulb); bulb i uses type `ids[i mod n]` and flavor
  `i div n` (modulo the bulb's flavor count); top cells top-aligned, bottom cells bottom-aligned, right cells
  right-aligned; built-in spacings from the table (0, 2 or 8), add-on spacing 0.
* Up to 6 types on every edge (PO decision 4).
* The only change: the inputs are scaled (5.3.2).

#### 5.3.2 Scale [MUST]

`S = display scale (effective DPI / 96) x Size factor` (Small 0.75, Standard 1.0, Large 1.5, Extra Large 2.0). Every
cell becomes `round(w x S) x round(h x S)` and every spacing `round(sp x S)`; the classic algorithm then runs on these
integers in physical pixels. Reference PC at Standard: S = 1.5; Standard Bulbs are 48 x 48 px; with Standard Bulbs
on every edge and corner, the top strip holds 78 bulbs between two 48 px corners with gap 0 and each side strip holds
41 bulbs with gap 24/41 (work area 3840 x 2088).

#### 5.3.3 Pixel-art scaling [MUST]

* **Smooth** (default): transparent pixels canonicalized to 0; MMPX 2x (twice for 4x, nearest-neighbor doublings
  beyond) to the smallest power of two >= S, clamping at the frame edge so cords continue into neighbors; then
  area-average down to the exact size in premultiplied alpha.
* **Crisp**: nearest neighbor to ceil(S), then area-average down to S (blocky pixels with at most one soft seam pixel
  at fractional scales; pure nearest neighbor at integer scales). The layout is identical to Smooth.
* At S = 1 both use the original pixels. Results are cached on disk per (bulb, animation, frame, S, style) in
  `%LOCALAPPDATA%\Holiday Lights\Cache` and uploaded once as DirectComposition surfaces.

#### 5.3.4 Art fidelity [MUST]

Built-in art comes from the 49 sheets with the exact 5.4 cell slicing (golden `builtin-cells.json`); add-on art through the
faithful 5.4 GIF decoder (holes, persistent local color tables, GDI nearest-color translation, ignored frame delays) -
PO decision 3. Masks become alpha 0/255; the 5.4 "OR fringe" and the On-Top clipping of Party Hats and Pastel Easter
Eggs are artifacts, not art, and are not reproduced.

### 5.4 Bulb kinds [MUST]

Each animation of a bulb (per edge and flavor, and per corner) is one of:

| Kind | Rule | Behavior |
|---|---|---|
| **Light bulb** | Built-in: the 15 lit/unlit bulbs (Standard, Indoor, Mini, Heavy Duty, Gifts, Sweet Hearts, Old Glory, Ghosts, Autumn Leaves, Cornucopias, Dead Turkeys, Chili Peppers, Paper Lanterns, Laundry, Religious Icons); frame 0 = lit. Add-on: exactly 2 frames with identical masks, where the brighter frame's mean luma exceeds the other's by at least 12 % **and** at least 5 % of opaque pixels differ in luma by 32 or more; the brighter frame is lit. | glows, fades, takes brightness in the new patterns |
| **Animation** | 2 or more frames, not a light bulb | frames switch instantly, as in 2003 |
| **Static** | 1 frame | never changes (Jolly Holly, Snow Family, Spring Flowers, spacers) |

Emissive pixels of a light bulb (the glow source): pixels where luma(lit) - luma(unlit) >= 32, or
max(R,G,B)(lit) - max(R,G,B)(unlit) >= 32, so saturated reds and blues count (luma = 0.299 R + 0.587 G + 0.114 B,
0-255; review r1 #13). The add-on light-bulb classifier (the 5 % / 32-luma rule) is unchanged.

### 5.5 Timing [MUST]

#### 5.5.1 The step clock

* One global step counter s, advanced every `P = interval x 60 ms` (interval 1-9; slider position p = 1..9 from Slow
  to Fast gives interval 10 - p; default interval 5 = 300 ms). Exact 60 ms multiples (PO decision 5), paced by a
  high-resolution waitable timer on the lights thread; never `WM_TIMER`.
* "Don't Flash" shows a static picture (5.4 redrew an unchanged frame every 600 ms).
* Every display, strip and preview shares s; a strip shows its frame `s mod frames(strip)` (5.4).
* A speed change takes effect at the next step boundary without resetting s, so the lights never jump.
* The animation never stops while a mouse button is held (5.4 bug fixed).
* Continuous time t (ms since the pattern started) drives fades and continuous patterns.

#### 5.5.2 Previews

Every UI preview uses the same clock and rules as the desktop (3.0.2).

#### 5.5.3 Flash limit (photosensitivity)

When "Limit Flashing to 3 Flashes per Second" is on: interval >= 3 (180 ms; slider positions 8-9 disabled), so a
light bulb completes at most 2.8 on/off cycles per second; Twinkle, Chase Around the Screen and Dance to the Music
retrigger a bulb at most every 333 ms; Smooth Fading is forced on. The lights keep flashing.

### 5.6 Classic flash patterns - exact [MUST]

Reproduced exactly as 5.4 does it, including the quirks the product owner keeps:

```
frames(strip) = DontFlash      -> 1
                RandomFlashing -> 8
                otherwise      -> max phase count over the strip's usable side bulbs
                                  (Top/Bottom: also their two corners' phase count for that side)
k = 1 at the start of every strip; after each side bulb whose phase count > 1: k = k + 1
side bulb phase in strip frame f:
   DontFlash      0
   FlashTogether  f
   Alternating    (k odd) ? f + frames/2 : f        // 2-phase bulbs stop alternating next to 4/8-phase bulbs (kept)
   BulbChase      f + k                             // runs toward the strip start (kept)
   RandomFlashing rand()                            // MSVC LCG seed*214013+2531011, (seed>>16)&0x7FFF, one draw per
                                                    // (bulb, f) at build time, build order Top, Bottom, Right, Left,
                                                    // seeded with the millisecond; repeats every 8 steps (kept)
corner phase in frame f: f (DontFlash: 0)           // corners ignore the pattern (kept)
shown frame = phase mod (frame count of that animation)
strip frame at step s = s mod frames(strip)
```

Random Flashing re-rolls when the lights are rebuilt (5.4). Light bulbs show lit when the shown frame is 0. With
Smooth Fading on, light-bulb changes in the classic patterns are drawn as fades (5.8) **without moving any step**.
Music never changes the timing of a classic pattern.

### 5.7 New flash patterns - exact

Definitions use: s the step, P the step period, t time; **ring order** per display: top-left corner, top strip left
to right, top-right corner, right strip top to bottom, bottom-right corner, bottom strip right to left, bottom-left
corner, left strip bottom to top (missing corners skipped). q = index of a bulb among light bulbs in ring order; a =
index among animation bulbs; R = number of bulbs in the ring; b in [0,1] = brightness of a light bulb (0 = unlit
frame, 1 = lit frame; rendered per 5.8). `u(i, s)` = a uniform number in [0,1) from SplitMix64(seed XOR (ring index
<< 32) XOR s), where seed is the ring's seed: the pattern seed on ring 0, pattern seed XOR SplitMix64(r) on ring r > 0
(PO decision 5: identical displays never twinkle in lockstep; the screen saver seeds display i the same way), the
pattern seed taken from the clock when the pattern starts (fixed in tests), so every pattern is deterministic and
unit-testable. Corners take part like any other bulb. Static bulbs never change. Animation bulbs switch frames
instantly.

| # | Pattern | Light bulbs | Animation bulbs | Fade (5.8) | Scope |
|---|---|---|---|---|---|
| 5 | **Twinkle** | Start lit with probability 0.8. At each step a lit bulb goes dark when `u < 0.12`; a dark bulb lights when `u < 0.5` (about 80 % lit, never repeating). | Advance one frame at a step when `u < 0.5`. | on 0.6 d, off d, d = 0.8 P | MUST |
| 6 | **Slow Glow** | `b = 0.5 - 0.5 cos(2 pi t / (16 P))` for every bulb (4.8 s per breath at the default speed). | Frame `s mod N` (like Flash Together). | continuous | MUST |
| 7 | **Chase Around the Screen** | Lit when `(q - s) mod 3 = 0`: one in three lit, moving one bulb clockwise per step around the whole frame. | Frame `(s - a) mod N`: the motion travels clockwise. | d = 0.5 P | MUST |
| 8 | **Dance to the Music** | Driven by music events (5.10). Without music playing (music off, between songs, an "Intermittently" gap, paused): exactly Slow Glow. | One frame per beat (at most every 120 ms); if no beat for 2 s, every P. | attack 30 ms, decay tau 250 ms | MUST |
| 9 | **Waves** | `b = 0.5 - 0.5 cos(2 pi (t / (16 P) - q / 16))`: brightness waves 16 bulbs long travel clockwise. | Frame `s mod N`. | continuous | NICE |
| 10 | **Combination** | Plays Flash Together, Alternating, Bulb Chase, Random Flashing, Twinkle, Slow Glow, Chase Around the Screen (and Waves when built) in that order, 40 steps each (12 s at the default speed), each restarting from s = 0; repeats. | per the active pattern | per the active pattern | NICE |

Patterns 5-10 are stored by name; the classic values 0-4 keep their 5.4 numbers in settings, themes and the import.

### 5.8 Brightness and smooth fading [MUST]

* Applies to **light bulbs only**; animation bulbs always switch frames instantly via `SetContent` at step
  boundaries, as in 2003.
* Per light bulb: an unlit visual, the lit visual above it at opacity b, and the glow visual (5.9) beneath them at
  opacity `b x glow intensity`.
* All brightness changes are **DirectComposition opacity animations**; the CPU only creates them at step boundaries
  and music events; nothing is drawn per frame on the CPU.
  * Discrete patterns (the classic five, Twinkle, Chase Around the Screen): turning on ramps 0 to 1 in 0.6 d, turning
    off ramps 1 to 0 in d (an incandescent afterglow). Classic patterns: `d = min(0.4 P, 150 ms)` (120 ms at the
    default speed, 24 ms at the fastest - the 5.4 rhythm is kept).
  * Slow Glow (and Waves): one repeating animation per bulb (a raised cosine approximated by four cubic segments per
    period; Waves uses a per-bulb begin offset), created when the pattern starts and whenever P changes.
  * Dance to the Music: 5.10.
* With Smooth Fading off: discrete patterns switch instantly; continuous patterns show lit when b >= 0.5 (step
  animations).
* "Use Less Power" (5.12.3) turns fading off.

### 5.9 Glow [MUST]

* Only light bulbs glow, only from their emissive pixels (5.4), only while lit (scaled by b).
* Glow sprite per (bulb, edge, flavor, scale): the emissive pixels in their lit colors, Gaussian blur with
  sigma = 0.22 x min(cell width, cell height) at scale S, capped at 24 art px (PO decision 1), margin ceil(3 sigma), stored **premultiplied with alpha 0**
  so DWM adds it to whatever lies behind (additive light). Intensity: Soft 0.55, Bright
  1.0. Drawn beneath the bulbs; overlapping glows add up; clipped at the display edge; never affects layout.
* Behind the icons, the glow lights up the wallpaper under the icons; over a white wallpaper it is invisible
  (additive light).
* Off in Classic 2003, under "Use Less Power" and "Stop Flashing", and in High Contrast UI previews. The screen saver
  uses the same glow.
* HDR displays: if testing shows clamping artifacts in HDR (unverified), glow intensity is halved on displays in
  Advanced Color mode (risk R2).

### 5.10 Music sync ("Dance to the Music") [MUST]

* **Groups.** Light bulbs are grouped by pitch class: bulb q belongs to group `q mod 12`; corners also form group C.
  Each group has one brightness B(t) that all its bulbs share, so one event re-targets one animation per visual in
  that group.
* **MIDI** (our sequencer publishes every note-on when it is sent):
  * Channel 10, note 35 or 36 (bass drum), velocity v: every group `B = max(B, v / 127)`.
  * Other channel-10 notes: group C `B = max(B, 0.4 + 0.6 v / 127)`.
  * Any other note n: group `n mod 12` gets `B = max(B, 0.4 + 0.6 v / 127)` (melodies run around the screen).
  * Beats for animation bulbs: quarter notes from the song's tempo map (every shipped song has one tempo).
* **Audio files**: the NAudio beat detector (energy over 1,024-frame windows, 43-window history, variance-adapted
  threshold, 5-window refractory period) emits beats; on a beat every group B = 1.
* **Decay** `B = B x exp(-dt / 250 ms)`; attack 30 ms. The CPU tracks B analytically and, per event, gives each visual
  of an affected group a new opacity animation (attack segment, then a cubic approximation of the decay). Events are
  coalesced in 30 ms batches.
* **Latency.** Visual events are scheduled at the moment the sound is heard: MIDI events + the device's own latency
  (GS Wavetable Synth 190 ms, measured: it sounds 216-220 ms after `midiOutShortMsg`) + `music.syncOffsetMs` (default
  40 ms, the way from the device to the listener), published 40 ms ahead; audio beats + the output latency (review r1
  #8).
* All displays react to the same events. CPU budget while Dance plays music: <= 2 % of one core (5.14).
* In the screen saver process, the running app streams these events over the pipe (6.2.1); without a running app the
  saver process plays the music and generates the events itself.

### 5.11 Automatic themes (Theme Calendar) [MUST]

* **On/off.** On for newcomers and factory-default 5.4 imports; off for customized 5.4 imports (2.5). Loading a theme
  by hand (Home, Themes, tray, Welcome) turns it off.
* **Dates** are local; ranges are inclusive whole days and may cross the new year. A switch is evaluated at program
  start, at local midnight, at resume from sleep, on time-zone or clock changes, and when the calendar is edited.
* **Default entries** (check state as in 3.6.4):

| Entry | Range | Theme | Checked by default |
|---|---|---|---|
| New Year | Dec 31 - Jan 1 | New Year | yes |
| Valentine's Day | Feb 1 - Feb 14 | Valentine's Day | yes |
| St. Patrick's Day | Mar 10 - Mar 17 | St. Patrick's Day | yes |
| Easter | Easter Sunday - 14 days to Easter Sunday + 1 day (Gregorian computus; Julian computus converted to Gregorian for GR, CY, RU, UA, RS, BG, RO, GE, MK, ME, BY, MD) | Easter Eggs | yes |
| July 4th | Jul 1 - Jul 5 | July 4th | region US only |
| Halloween | Oct 1 - Oct 31 | Halloween | yes |
| Thanksgiving | US: Nov 1 - the fourth Thursday of November; CA: the second Monday of October - 6 days to that Monday | Thanksgiving | regions US and CA only |
| Christmas | US: the day after US Thanksgiving - Dec 30; elsewhere Nov 25 - Dec 30 | Christmas 1 | yes |
| Chanukah | the day before 25 Kislev through 25 Kislev + 7 days (.NET `HebrewCalendar`) | Chanukah | no (one click; it would otherwise replace Christmas lights for 9 days for people who don't celebrate it) |
| Winter | Jan 2 - Feb 28 (southern hemisphere: Jun 1 - Aug 31) | Winter Wonderland | no |
| Spring | Mar 1 - May 31 (southern: Sep 1 - Nov 24) | Spring Garden | no |
| Summer | Jun 1 - Aug 31 (southern: Jan 2 - Feb 28) | Summer Nights | no |
| Autumn | Sep 1 - Nov 24 (southern: Mar 1 - May 31) | Autumn Harvest | no |

  Southern hemisphere regions: AR, AU, BO, BR, CL, LS, MG, MZ, NA, NZ, PE, PY, SZ, UY, ZA, ZM, ZW.
* **Overlaps** between checked entries: the **shorter** range wins; equal length: the later start wins (Thanksgiving
  wins inside Autumn; Chanukah, when checked, wins inside Christmas; Valentine's Day inside Winter).
* **Between holidays** (no checked entry contains today): the "Between holidays" theme, default **Classic Lights**,
  so the lights never vanish.
* **Boundaries only.** The calendar applies a theme only when the active entry **changes** (or when Automatic themes
  are turned on); manual edits in between are left alone until the next boundary. The first time the user changes
  the arrangement by hand while Automatic themes are on, an InfoBar on Bulb Factory says "Automatic themes are on: on
  Nov 1 your lights change to Thanksgiving." [Save as Theme...] [Turn Off Automatic Themes].
* **Settings open.** If the Settings window is open at a boundary, the switch waits until it closes.
* **Each switch** uses the theme transition, records Recent Settings, and (setting "Tell Me When the Theme Changes")
  shows the notification of 3.12; clicking it opens Themes with "Undo".
* The music switch is global (D8): a theme switch can change which songs play and when, but never turns music on.

### 5.12 When the lights rest [MUST]

#### 5.12.1 Lights off (user)

"Show Lights" off (tray, Home, hot key): every layer fades out and hides, timers stop, the tray icon is unlit; it
persists across restarts; music and the screen saver are unaffected.

#### 5.12.2 Automatic pausing

| Condition | Detection | Lights | Music | Setting |
|---|---|---|---|---|
| Session locked | `SessionSwitch` + initial `WTSSessionInfoEx` state (SessionFlags at offset 16) | hidden, clock stopped | paused (unless the Holiday Lights screen saver plays it) | always; music: "Pause Music When I Lock My PC" |
| Display off | `GUID_SESSION_DISPLAY_STATUS` = 0 | clock stopped, no commits | continues | always |
| Another screen saver running, session not present | `QUNS_NOT_PRESENT` | stopped | per mode | always |
| Holiday Lights screen saver running (also "Preview Screen Saver") | pipe message from the saver (6.2.1) | desktop layers stop committing | per "Play the Chosen Songs" | always |
| Full-screen app or game on a display | the foreground window (skipping Progman, WorkerW, the taskbars, cloaked and minimized windows and our own) covers that display's `rcMonitor` (`DWMWA_EXTENDED_FRAME_BOUNDS`); the display stays full screen after that window loses the focus to a window on another display while it still covers the display, until the user activates another window mostly on that display (windows that never had the focus, such as overlays, never count); polled every 1 s, every 2 s while the lights rest on every display (locked, display off, `QUNS_NOT_PRESENT`, Remote Desktop); plus `QUNS_BUSY` | hidden on **that display only** | paused | "Hide the Lights on a Display While a Full-Screen App or Game Is Running There"; "Pause Music While ..." |
| Exclusive full-screen Direct3D, Game Mode | `QUNS_RUNNING_D3D_FULL_SCREEN`; effective power mode 5 | hidden on all displays | paused | same settings |
| Presentation | `QUNS_PRESENTATION_MODE` | hidden on all displays | paused | "Hide the Lights During Presentations"; "Pause Music While ..." |
| Focus session | `FocusSessionManager.IsFocusActive` | unaffected | paused | "Pause Music During Focus Sessions" |
| Remote Desktop session | `GetSystemMetrics(SM_REMOTESESSION)` | hidden | stopped | always |
| No graphics device | DirectComposition device cannot be created | hidden; retried every 10 s | unaffected | always (Home status) |

Resuming is immediate when the condition ends (300 ms fade); music resumes where it paused. Hidden or stopped layers
commit no frames. While everything rests, only a 2 s state poll runs.

#### 5.12.3 Power

"When Energy Saver Is On" applies while `GUID_ENERGY_SAVER_STATUS` >= 1 (or `GUID_POWER_SAVING_STATUS` = 1 on older
Windows):

| Choice | Effect |
|---|---|
| "Use Less Power" (default) | Glow off, fading off, step period at least 300 ms (interval >= 5). The lights keep flashing. |
| "Stop Flashing" | Every bulb shows frame 0 (lit), no glow, no fading; no clock. |
| "Turn Off the Lights" | As Lights Off, until Energy Saver ends. |
| "Change Nothing" | - |

### 5.13 Robustness [MUST]

| Event | Behavior |
|---|---|
| Explorer restart (`TaskbarCreated`, layer windows destroyed, or the 2 s check finds new Progman/DefView handles) | Re-create the layers with back-off (500 ms, 1 s, 2 s, 4 s ...), the chosen mode first; re-add the tray icon. Lights back within 3 s. |
| Display added or removed, resolution, scale, orientation, taskbar moved or resized | Debounced rebuild of the affected displays (5.2.3); previews follow. |
| Graphics device lost (driver update, TDR) | Detect with `CheckDeviceState`; recreate the D3D11/DComp device and all surfaces within 1 s; after 2 failures in a minute use WARP and show the Bulb Factory InfoBar "Holiday Lights had trouble drawing the lights and switched to software drawing." |
| Sleep and resume | Revalidate the layers and the host, re-evaluate Automatic themes, restart the clock. |
| Unlock | Revalidate the layers (Lively issue: the wallpaper host can change). |
| Crash in the lights thread | Restart the lights thread, at most once per minute; notification "Lights restarted" once per session. |
| Settings file damaged (content cannot be parsed) | Rename it (or copy it byte-exact when it cannot be renamed) to `settings.damaged-<date>.json`, start with defaults, keep themes and files, notification "Settings were reset". |
| Settings file cannot be opened (held by another program, being replaced, access denied) | Retry for about 3 s, start with defaults, never rename or overwrite the file, notification "Settings couldn't be read"; use it (with the changes made meanwhile applied again) as soon as it can be read (review r1 #6). |
| Theme file unreadable | Skipped and listed in an InfoBar on Themes ("1 theme couldn't be read: <file>."), never deleted. |
| Disk full or file write failure | InfoBar on the page that wrote ("Couldn't save your settings: <Windows reason>."); the change stays applied in memory and is retried on the next change. |

### 5.14 Performance budget [MUST]

| Situation (reference PC, two 4K displays) | Budget |
|---|---|
| Start to first committed frame | <= 2 s with a warm cache, <= 6 s cold (first run, nothing cached) |
| Lights animating, default theme and settings, Settings closed | <= 0.5 % of one CPU core average (ETW), < 200 MB working set |
| Dance to the Music while music plays | <= 2 % of one core |
| Settings open on Bulb Factory with the gallery animating | <= 3 % of one core |
| Everything paused or hidden | 0 % (no commits; one 2 s poll) |
| Gallery: first tiles after opening Bulb Factory | <= 300 ms warm; search results <= 100 ms after typing stops |

Only bulbs in use are decoded at desktop scale; Holiday Lights never enables EcoQoS ("efficiency mode") while lights
or music run (H.NotifyIcon `ForceCreate(enablesEfficiencyMode: false)`).

---------------------------------------------------------------------------------------------------------------------

## 6. Subsystems

### 6.1 Music Box behavior [MUST]

#### 6.1.1 The music switch and the play modes

* **"Play Holiday Music"** (`music.enabled`) is a global device preference, not part of themes. Off means no music
  at all. It is off for newcomers and for factory-default 5.4 imports, on for customized imports unless their mode
  was "Never" (6.8). Turning it on while "Play the Chosen Songs" is "Never" sets the mode to "Always" (one undo step).
* **"Play the Chosen Songs"** (part of themes) keeps the 5.4 modes 0-4 exactly: 0 Never; 1 Only When
  the Screen Saver is On (the Holiday Lights screen saver is showing, including "Preview Screen Saver"); 2 Only When
  the Screen Saver is Off; 3 Always (1 s between songs); 4 Intermittently (60,000 + random(0..119,999) ms between
  songs).
* Music plays when the switch is on, the mode allows it, at least one checked song is playable, and no pause rule
  (5.12.2) applies.

#### 6.1.2 Classic rules, kept and fixed

* **Shuffle bag** (5.4): every checked, playable song plays once per round in random order; a new round contains
  every checked song except the one that just played (unless it is the only one), so no song plays twice in a row. A
  rescan starts a new bag.
* No delay before the first song (5.4), except: for a 5.4 import the first song waits until the Welcome card closes;
  the first song after program start fades in over 3 s.
* Unchecking the playing song stops it and starts another (5.4).
* "Play Now" ignores the mode and the check boxes and does not touch the bag (5.4).
* Loading a theme re-evaluates music (5.4): if the playing song is still checked and the mode still allows it, it
  keeps playing; otherwise it stops and the next song follows the new mode.
* The settings store the **unchecked** songs (5.4 "Disabled Music"), so new songs start checked; a theme stores its
  **checked** songs (5.4 "Enabled Music"); loading a theme unchecks every song not named in it.
* Song list order: upper-cased file name (5.4 `toupper` key).
* **Fixed:** an "Intermittently" pause is honored by every re-evaluation (5.4 started the next song when Settings
  tabs changed); a song that fails mid-way skips to the next after 1 s (5.4 stopped all music); a `.lnk` shortcut
  plays with the engine of its target (5.4 sent every shortcut to DirectShow); no 2 s `reset.mid` freeze.

#### 6.1.3 Engines

* **MIDI** (`.mid`, `.rmi`): our sequencer over WinMM: SMF 0/1, tempo map, paced by a
  high-resolution waitable timer on the music thread. Device: the "MIDI Output" setting, else the first device whose
  name contains "GS Wavetable", else device 0; the MIDI mapper only if no device exists. GM System On
  (`F0 7E 7F 09 01 F7`) before each song; CC 120, 123 and 121 on all 16 channels at stop; loops on the End-of-Track
  time (the trailing rest is kept); volume = CC7 scaling on every channel; pause/resume re-sends program and
  controller state; publishes note-on and beat events to the lights (5.10) through a lock-free ring buffer.
* **Audio files** (`.mp3`, `.wav`, `.wma`, `.aif`, `.aifc`, `.aiff`, `.au`, `.snd`, `.mpeg`, `.m4a`): NAudio 3.1
  `AudioFileReader` (Media Foundation for WMA/M4A/MPEG) -> beat-detector tap -> `WaveOut`; volume on the reader.
* The music thread runs at above-normal priority; EcoQoS is never enabled while music plays.

#### 6.1.4 Song credits and categories (Arranger column, chips, About)

From the 5.4 music credits (help topic 31):

| Songs (file names) | Chip | Arranged by |
|---|---|---|
| Deck the Halls (Reggae), Jingle Bells (Reggae), O Christmas Tree (Swing), We Wish You a Merry Christmas (Reggae) | Christmas | Dean Burris, 1999 |
| Auld Lang Syne (Swing) | New Year | Dean Burris, 1999 |
| Hava Nagilah | Chanukah | Dean Burris, 1999 |
| Auld Lang Syne | New Year | Michael Kosacki, 1994 |
| Chanukah Song, Dreidle | Chanukah | Michael Kosacki, 1994 |
| Greensleeves | Christmas, Folk & Classics | Michael Kosacki, 1994 |
| Joy of Man's Desire | Christmas | Michael Kosacki, 1994 |
| A Very Merry Christmas, Almost Time for Christmas, Angels We Have Heard On High, Deck the Halls, First Noel, God Rest Ye Merry Gentlemen, God Rest Ye Merry Gentlemen (Reggae), Good Christian Men, Good King Wenceslaus, Hark the Herald Angels Sing, Here We Come A-Caroling, In a Manger, It Came Upon A Midnight, Jingle Bells, Jolly Old St. Nick, Joy to the World, O Christmas Tree, O Come, All Ye Faithful, O Little Town of Bethlehem, Silent Night, Twelve Days of Christmas, We Three Kings, We Wish You a Merry Christmas, What Child Is This | Christmas | George Larson, 1993 ("all other traditional Christmas song arrangements") |
| Joy of Man's Desire (Reggae) | Christmas | (no credit in 5.4; the column stays empty) |
| Bicycle Built for Two, Clementine | Folk & Classics | Bill Basham and Diversified Software Research, 1996 |
| Yankee Doodle Dandy | Patriotic | Bill Basham and Diversified Software Research, 1996 |
| Danny Boy | Folk & Classics | Thomas Thurston, 1999 |
| Halloween - Eerie, Halloween - Funeral March, Halloween - Haunted House, Halloween - Marionette, Halloween - Scary | Halloween | Night Gallery Halloween Web site, 1995 |
| Star Spangled Banner | Patriotic | (no credit in 5.4) |

Chip counts: Christmas 31, Chanukah 3, Halloween 5, New Year 2, Patriotic 2, Folk & Classics 4 (Greensleeves is in two
chips). "The 31 Christmas songs" = exactly the songs of the 5.4 "Christmas 1" theme.

### 6.2 Screen saver [MUST]

#### 6.2.1 Program modes

The installed `Holiday Lights.scr` is the same program as `HolidayLights.exe` (a copy with the `.scr` extension;
string resource 1 "Holiday Lights" so Windows lists it under that name). Screen saver processes **never take the
single-instance mutex**.

| Invocation | Behavior |
|---|---|
| `/s` (`/S`, `-s`) | One topmost, full-screen, cursor-less window per display chosen in "Show On" (others get a black window), each with its own DirectComposition tree. If Holiday Lights is running, the saver connects to its pipe and sends `saver-started`: the app rests its desktop layers, keeps playing (or starts) the music per "Play the Chosen Songs" without restarting the current song, and streams note and beat events back for "Dance to the Music"; on exit the saver sends `saver-stopped`. If Holiday Lights is not running, the saver process reads the settings and plays the music itself (switch and mode permitting), exiting with the saver (5.4). |
| `/p <hwnd>`, `/p:<hwnd>` | A child of Windows' preview window, created on a thread whose DPI awareness equals the parent's; the same saver simulated at the main display's size and drawn scaled to the small rectangle (normal art area-averaged down; 5.4's quarter-size cells are not used); no music. |
| `/c`, `/c:<hwnd>`, no argument | Forwards `show-settings saver` to a running Holiday Lights; otherwise a settings-only session (no lights, tray, hot keys or music) opens Settings on Screen Saver and exits when the window closes (5.4 `settings` mode). |
| `/a` | Ignored (Win9x passwords, retired). |

Exit rules (5.4): any key, any mouse button, pointer movement of more than 4 DIP (Manhattan), power broadcasts; focus
and activation changes are ignored; the cursor is hidden. Windows handles "On resume, display sign-in screen".

#### 6.2.2 What is drawn (5.4, per display)

Per display, every 60 ms simulation step, in 5.4 order: background color -> picture (main display only; Center with
y at one third of the free height when the picture is shorter, Tile, Stretch; bundled pictures use their second
embedded BMP and are scaled with MMPX at the display scale, photos with high-quality resampling) -> the animation
module -> the current arrangement around the **full display** (the saver covers the taskbar, as 5.4) with the current
pattern, Look and glow -> the message (main display only) with its 2 px black shadow, bouncing 1 DIP per step between
H/8 margins, or below a centered picture when it fits (5.4 rule). Snow sticks to the bulbs during the first 2,000
steps; snow and leaf piles grow to 24 DIP; Gravity Well stamps settled sprites; all module physics, counts and
constants exactly as in 5.4. "Main Display Only": the other displays stay black (5.4).

#### 6.2.3 What changed

* The simulation runs in DIPs (a 4K display at 150 % simulates as 2560 x 1440) with the 5.4 per-step constants at a
  fixed 60 ms step; drawing is scaled to physical pixels, so sizes and speeds look like 2003 on any display.
* "Smooth Motion": positions are interpolated between simulation steps at the display refresh rate; off (Classic
  2003) presents every 60 ms.
* Sprite frames advance every Flash Interval x 60 ms, **also with "Don't Flash"** (5.4 froze them; the stored
  interval is used).
* Fonts: a theme font that is not installed uses the first installed look-alike: Creepy -> Chiller -> Ink Free;
  Lucida Handwriting -> Segoe Script; Mistral -> Segoe Script; Footlight MT Light, Georgia Ref, Figaro MT -> Georgia;
  any other missing font -> Arial Bold 36 pt (the 5.4 fallback). Sizes: points = |lfHeight| x 72 / 96, rendered at
  the display scale.
* No trial banner.

#### 6.2.4 Installing as the Windows screen saver

Only the Screen Saver page buttons (3.5.2) and the Welcome card "Fix It" / "Use Holiday Lights as My Screen Saver"
change Windows' screen saver settings; "Preview Screen Saver" never does. The previous `SCRNSAVE.EXE`,
`ScreenSaveActive` and `ScreenSaveTimeOut` are remembered in settings for "Stop Using It" and for the uninstaller. The
broken 5.4 entry is detected as in 3.5.2 and replaced only on request.

### 6.3 Bulb library and Bulb Editing files [MUST]

| Source | Id | Location | Written by Holiday Lights |
|---|---|---|---|
| 49 built-in bulbs | `builtin:<slug>` (`builtin:standard-bulbs`) | embedded (original BMPs + table) | never |
| 1,501 bundled add-on bulbs (header `locked` = 1) | `addon:<file stem>` | `<install>\Content\Bulbs` (read-only) | never |
| My Bulbs (added files, imported 5.4 bulbs, bulbs made from GIFs) | `user:<file stem>` | `Documents\Holiday Lights\Bulbs` (watched) | only Bulb Editing, and only for files with `locked` = 0 |

* **Bulb content identity** (duplicate detection, the 5.4 import): two `.bul` files are the same bulb when their name,
  description, author, copyright, preview window, slot table and the size and CRC of every referenced GIF entry are
  equal. Region caches, the sender record, the `categ:` record, the `locked`, `unsent` and copyright-status flags and
  the bulb id are ignored (5.4 wrote some of these into files).
* Favorites, removed (hidden) bundled bulbs and category overrides are stored in settings by id; they never change a
  file. Hiding a bulb removes it from the Bulb List; arrangements and themes that use it keep showing it.
* Every `.bul` is decoded with the faithful 5.4 decoder; damaged files (5.4 loader rules) are reported, never deleted.
* **Bulb Editing writes** a standard `.bul` version 4 exactly as 5.4 writes it: 0x57C-byte header, up to 37 GIF
  entries stored verbatim and de-duplicated by CRC, the `categ:` record, `fileSize`, `locked` = 0, `unsent` = 0,
  copyright status 0, a fresh bulb id not used by any loaded bulb, no region caches, no sender record; strings in
  Windows-1252 (a character outside it is replaced by "?" and the user is told "Some characters can't be saved in a
  bulb file and were replaced."). Save writes `<name>.bul.tmp` then replaces the file. Holiday Lights 5.4 can read
  the result (tested with the 5.4 load rules).
* The index cache (`Cache\index.json`: names, categories, authors, sizes, frame counts, kinds, content hashes) and the
  per-DPI thumbnails are invalidated by file size and date.

### 6.4 Themes [MUST]

#### 6.4.1 What a theme holds

Exactly the 13 values of 5.4: arrangement (8 boxes), flash pattern, flash interval,
checked songs, Play the Chosen Songs, screen saver animation, style, message, font (family, size, bold, italic,
underline, strikeout), message color, background color, picture, placement. Not in themes (device preferences, as
5.4): Show Lights, Bulb Drawing and Behind the Desktop Icons, displays, size, Look (pixels, glow, Smooth Fading,
Smooth Motion), Play Holiday Music, volume, MIDI output, Show On, hot keys, startup, rest rules, accessibility,
Automatic themes and the calendar, favorites.

#### 6.4.2 Loading

Copies the 13 values. A value the theme lacks gets its **default** (5.4 deleted it, with the same visible result).
Songs not named in the theme are unchecked; songs named but missing are ignored. Missing bulbs are skipped and the
InfoBar "This theme uses 2 bulbs that aren't installed: <names>." appears on Themes. Recent Settings records the
previous values; the Save Theme name becomes the theme's name (5.4); music is re-evaluated (6.1.2). A theme
"matches" the current settings (Current badge, tray radio) when all 13 values are equal (songs compared as sets, font
family case-insensitively).

#### 6.4.3 Shipped themes

19 themes: the 11 classic installer themes exactly as stored by the 5.4 installer and 8 new ones (7.2). They are
seeded into the themes folder on first run and behave like 5.4's registry themes (replaceable, renamable,
deletable); the originals are embedded for "Restore Built-In Themes", "Restore Original" and the "Changed" badge.

#### 6.4.4 Storage

`%APPDATA%\Holiday Lights\Themes\<name>.json` (Appendix C), atomic writes. Theme export and import as a `.hltheme`
file (a zip with the theme and any My Bulbs and My Pictures it uses) is [NICE].

### 6.5 About and credits content [MUST]

The About expanders (and the Help "Credits" chapter) contain:

1. **Original program**: "Holiday Lights 5.4 for Windows, Copyright 1993-2003 Tiger Technologies. Holiday Lights was
   created by Tiger Technologies. Holiday Lights is a registered trademark of Tiger Technologies."
2. **Built-In Bulb Artists** (from the 49 records' author and copyright fields): Joe Lachoff (most built-in bulbs);
   Robert L. Mathews (Heavy Duty Bulbs, Sweet Hearts; with Joe Lachoff: Old Glory Bulbs); Mark Pettus, Mighty Toad
   Software (Jack-O-Lanterns, Zombie Tombstones, The Grim Reaper, Autumn Leaves, Cornucopias, Live Turkeys, Dead
   Turkeys); Mary Lee Seward (Spring Flowers, Ghosts); Gail Allinson (North Pole Express, Flight Lights); Party Hats
   "based on art included with Mac OS, Copyright 1983-1997 Apple Computer, Inc." Each artist with the bulbs they drew.
3. **Add-On Bulb Artists**: every distinct author of the 1,501 bundled bulbs (about 410 names), A-Z with their bulb
   counts, generated at build time from the first line of each author field: trimmed; "Created by " / "By "
   removed; trailing periods removed; merged case-, space- and punctuation-insensitively, showing the most frequent
   spelling; an e-mail-only entry shows the part before "@". Searchable. [NICE] selecting a name filters the Bulb
   List to that artist.
4. **Music**: the arrangers of 6.1.4 with their songs.
5. **Screen Saver Art**: "The background pictures are licensed from ArtToday. The Dancing Demon, Gingerbread Man and
   Singing Tree animations are licensed from web-ready.com; the Angel, Santa and Skeleton animations from XOOM, Inc."
   (5.4 help topic 30, as text, no links).
6. **Software**: MMPX pixel-art magnification (Morgan McGuire and Mara Gagiu, MIT), Vortice.Windows (MIT), NAudio
   (MIT), H.NotifyIcon (MIT), VirtualizingWrapPanel (MIT), .NET and WPF (MIT), each with its license text ("View
   License").
7. "All artwork and music are copyrighted by their authors and may not be used for other purposes without their
   permission." (5.4 wording) and "Holiday Lights works completely offline and never collects information about you."

### 6.6 Startup, single instance, command line, hot keys, errors [MUST]

#### 6.6.1 Startup

Per-user Run value `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Holiday Lights` =
`"<install>\HolidayLights.exe" --autostart`. `--autostart` starts silently (no Welcome, no Settings) with the short
power-up. `StartupApproved\Run` is read to show the "turned off in Windows Settings" InfoBar and is never written.

#### 6.6.2 Single instance

Mutex `Local\HolidayLights6.Instance`; per-user pipe `HolidayLights6.<user SID>` (`PipeOptions.CurrentUserOnly`).
Later launches forward their command and exit. Commands: `show-settings [page]`, `open <files>`, `toggle-lights`,
`lights on|off`, `toggle-layer`, `theme <name>`, `exit`, `saver-started`, `saver-stopped`, `subscribe-music-events`.

#### 6.6.3 Command line

| Command | Effect |
|---|---|
| (none) | Start (lights and tray, no window - 5.4), or, if running, open Settings on its last page. |
| `--autostart` | Start without any window (Run value). |
| `--settings [home\|bulbs\|music\|saver\|themes\|general]`; legacy `settings` | Open Settings on that page; legacy `settings` = Screen Saver, as a settings-only session when not running (5.4). |
| `--open "<file>" ["<file>" ...]`; legacy `open <file>` (everything after "open " is the path) | Add the files (3.2.10). |
| `--toggle-layer` | Same as the location hot key. |
| `--lights on`, `--lights off`, `--lights toggle` | Show Lights. |
| `--theme "<name>"` | Load a theme. |
| `--exit` | Exit the running instance. |
| `--reset`; legacy `reset` | Asks "Reset Holiday Lights?" (3.8.1), then resets 6.0's settings (never the 5.4 registry); Recent Settings keeps the old values. |
| `--render-test <dir>` | Writes one PNG per display of the current scene (CPU compositor) without creating windows (ARCHITECTURE §8). |
| `/s`, `/p <hwnd>`, `/p:<hwnd>`, `/c`, `/c:<hwnd>`, `/a` | Screen saver modes (6.2.1). |

#### 6.6.4 Hot keys

`RegisterHotKey` with `MOD_NOREPEAT` on a hidden top-level message window of the UI thread; registered in the normal
mode only (not in screen saver or settings-only sessions). The location hot key toggles On Desktop / On Top (5.4 job)
and persists the new location (5.4); pressed while the lights are off, it turns them on, on top. The lights hot key
toggles Show Lights. Each use shows the on-screen pill (3.12). Failures are shown in General (3.7) and once as a
notification. The AltGr check (3.8.2) also runs at start-up and when keyboard layouts change; a combination that has
become unsafe is not registered and General explains why.

#### 6.6.5 Errors and logs

Unexpected errors are logged to `%LOCALAPPDATA%\Holiday Lights\Logs\` (3 files of 1 MB, rotating). The lights restart
themselves after a rendering failure (5.13). The user sees inline InfoBars and at most one notification per session
per kind (3.12); never a modal error box.

### 6.7 Help content [MUST]

Markdown topics bundled in the app, rewritten for 6.0 from the 5.4 help file. Tooltips and visible descriptions come
from the 57 "What's This?" texts (topics 37-93), updated (Appendix A). Contents (5.4 books kept, the payment book
removed, the contact book renamed "Credits"):

| Book | Topics |
|---|---|
| Getting Started | Welcome to Holiday Lights; About Holiday Lights; Starting Holiday Lights; The Taskbar Icon (click once, not twice, for the menu; double-click for Settings; finding the icon behind the ^); Turning the Lights On and Off; Exiting Holiday Lights; Permanently Removing Holiday Lights; Coming from Holiday Lights 5.4; What's New in 6.0 |
| Arranging the Bulbs | About Holiday Lights Bulbs; Changing Bulbs in the Settings Window (boxes, targets, try-on, dragging); Finding Bulbs (search, Show, favorites); Viewing Bulbs by Category; Displaying Bulbs On the Desktop or Above All Windows; Using a Hot Key to Choose Where Bulbs Are Drawn; Using the Taskbar Icon Menu to Choose Where Bulbs Are Drawn; Bulbs on Several Displays; Bulb Size, Glow and the Classic 2003 Look; Changing the Flash Pattern (every shipped pattern, with the 5.4 note that Bulb Chase and Alternating look the same with simple on/off bulbs but differ with bulbs such as Stockings and Menorahs); Changing the Flash Speed; Adding Your Own Bulbs; Making Your Own Bulbs (Bulb Editing, flavors, the white square); Sharing Your Bulbs (Export Bulb File) |
| Playing Background Music | About Music; Turning Music On and Off; Turning Songs On and Off; Choosing When Music is Played; Adding New Songs; Volume; Making the Lights Dance; Solving Music Problems |
| Using the Screen Saver | About the Screen Saver; Turning the Screen Saver On or Off; Animations and Styles; Background Pictures; Text Message; Several Displays |
| Holiday Lights Themes | About Holiday Lights Themes; Changing Themes Automatically on Holidays; Recent Settings |
| When the Lights Rest | Full-screen apps and games, presentations, Energy Saver, locking, Remote Desktop |
| Keyboard Shortcuts | every shortcut in Appendix B |
| Troubleshooting | I Can't See My Lights; The Taskbar Icon Is Missing; There Is No Music; The Screen Saver Doesn't Start; My Hot Key Doesn't Work; The Bulbs Are in Front of My Icons |
| Credits | Art Copyright Information; Music Copyright Information; Software Credits |

"What's New in 6.0" (also linked from the welcome-back card): "Your lights fit every display again, sharp at any
Windows scaling." / "They can sit behind your desktop icons (like before), in front of them, or on top of
everything." / "A gentle glow and soft fading, or the Classic 2003 look with one click." / "Four new patterns:
Twinkle, Slow Glow, Chase Around the Screen and Dance to the Music." / "All 1,501 add-on bulbs are included, with
search, categories and favorites." / "Six kinds of bulbs on every edge, and you can drag them into a new order." /
"Themes can change automatically on holidays." / "Real music volume and a choice of MIDI synthesizer." / "The screen
saver runs on every display, and previewing it never changes your Windows settings." / "The hot key is now
Ctrl+Alt+Shift+B, so web browsers keep Ctrl+Shift+B." / "Everything is unlocked and free, and Holiday Lights never
goes online."

### 6.8 Import from Holiday Lights 5.4 [MUST]

#### 6.8.1 When, and read-only

Automatically on the first start when `HKCU\Software\Tiger Technologies\Holiday Lights` exists, before the first
frame; again on demand (General > "Import Again..."). **Read-only**: nothing in the 5.4 key, its folders or its
files is changed, except, on explicit request, closing a running 5.4 and moving its Startup shortcut to the Recycle
Bin. The 5.4 folder is the directory of HKCU `Path`, else HKLM (WOW6432Node) `Path`, expanded with
`GetLongPathName` (for example `C:\Program Files\Holiday Lights`).

#### 6.8.2 Mapping

| 5.4 value or item | 6.0 result |
|---|---|
| `Bulb Settings` (128 bytes = 32 int32, or a legacy value of up to 32 signed bytes) | Arrangement: ints 0-5 top (left to right), 6/7 top-left/top-right, 8-13 right, 16-21 bottom, 22/23 bottom-left/bottom-right, 24-29 left; -1 = empty. Built-in ids 0-48 map to `builtin:` **by id**, not table index. Add-on ids are re-created exactly as 5.4 assigned them: enumerate `*.bul` in `<5.4 folder>\Holiday Lights Bulbs` in NTFS name order, read each header id (offset 0x0C), and when it is -1 or taken (built-ins hold 0-48) increment until free (5.4 rule). Each file maps to the bundled bulb with the same bulb content (6.3) -> `addon:`; otherwise the file is copied into My Bulbs -> `user:`. Unresolvable ids are dropped and listed. |
| `Flash Interval` | interval, clamped to 1-9 (out of range -> 5) |
| `Flash Pattern` | 0-4 (anything else -> Flash Together) |
| `Bulb Location` | 0 -> On Desktop with Behind the Desktop Icons; non-zero -> On Top |
| `Disabled Music` | the listed songs are unchecked (exact name), all others checked |
| `Music Play` | Play the Chosen Songs 0-4; "Play Holiday Music" on unless 0 (customized path, 6.8.4) |
| `Screen Saver Module` | the animation of that name; an add-on bulb's name -> that bulb (bundled or copied); unknown -> Snow |
| `Screen Saver Movement Type` | style 0-3; missing -> derived from the animation (5.4 rule) |
| `Screen Saver Message` | message |
| `Screen Saver Message Font` (LOGFONTA) | family = `lfFaceName`; size = round(\|lfHeight\| x 72 / 96) pt, clamped to 18-100; bold = `lfWeight` >= 600; italic, underline and strikeout from `lfItalic`, `lfUnderline`, `lfStrikeOut` |
| `Screen Saver Message Color`, `Screen Saver Background Color` | COLORREF -> #RRGGBB |
| `Screen Saver Picture Name` | one of the 11 bundled pictures (by name), "(None)", or the file (after `.lnk` resolution) copied from `<5.4 folder>\Holiday Lights Pictures` into My Pictures |
| `Screen Saver Picture Display Type` | 0 Center, 1 Tile, 2 Stretch |
| `Custom Color  0` ... `Custom Color  15` | the Color dialog's custom colors |
| `Bulb Location Hot Key On or Off`, `Bulb Location Hot Key Char` | location hot key on/off and its key; modifiers become Ctrl+Alt+Shift (General explains once and offers Ctrl+Shift+B) |
| `Included Bulb Categories\<name>` | category overrides of that built-in bulb |
| `Themes\<name>` (13 values each) | Equal to the shipped classic theme of that name -> nothing to do ("Already included"). Same name, different values -> replaces the seeded theme of that name (it is the user's). Other names -> added under the same name (characters not allowed in file names become "-"). Missing values get the 6.0 defaults; add-on ids resolved as above. |
| A `*Holiday Lights*.lnk` in the Startup folder | customized imports: "Automatically Start Holiday Lights" on (else off); factory-default imports use the newcomer default (on) |
| Songs in `<5.4 folder>\Holiday Lights Music` that are not bundled (by name and content) | copied into My Music (`.lnk` copied as shortcuts) |
| Pictures in `<5.4 folder>\Holiday Lights Pictures` that are not bundled | copied into My Pictures |
| `Current Version`, `User`, `Serial Number`, `Path`, `Last Converted Picture Name/Time`, `Prevent Slow Bulb Warning` | not imported ("obsolete") |

#### 6.8.3 5.4 leftovers

Detected at import and shown on the Welcome card and in General > Holiday Lights 5.4, each with a one-click fix:

| Leftover | Detection | Fix |
|---|---|---|
| Broken 5.4 screen saver | 3.5.2 rule (this PC: `SCRNSAVE.EXE = C:\WINDOWS\system32\HOLIDA~1.SCR`, file only in SysWOW64) | "Fix It" = "Use Holiday Lights as My Screen Saver", keeping the timeout (this PC: 1200 s). |
| 5.4 running | `FindWindow("Holiday Lights", "TigerTechHolidayLights")` | "Close It" sends `WM_COMMAND 106` (5.4 Exit). |
| 5.4 starts with Windows | a `*Holiday Lights*.lnk` in the Startup folder targeting the 5.4 exe | "Turn Off" moves it to the Recycle Bin. |

#### 6.8.4 Which look applies

* **Factory defaults** (definition in 2.5.2): everything is imported, the 5.4 current settings are kept in Recent
  Settings as "Holiday Lights 5.4 Settings", and the newcomer look applies (Automatic themes on, "Play Holiday Music"
  off, "Automatically Start Holiday Lights" on). "Use My 2003 Lights" applies those 5.4 settings, turns Automatic
  themes off and music on.
* **Customized**: the 5.4 current settings become the current settings (shown as "Custom Settings" unless they equal
  a theme); Automatic themes off; "Play Holiday Music" on unless the mode was Never; the first song waits for the
  Welcome card to close. "Start Fresh Instead" switches to the newcomer look (imported themes stay; Recent Settings
  keeps the replaced settings).

#### 6.8.5 Result and report

The import report lists every item as "Imported", "Already included" or "Not imported - <reason>" (shown in Import
Details, 3.8.5); General shows the summary line. "Import Again..." (after its confirmation) replaces the current
settings with the 5.4 ones and adds 5.4 themes that are missing; existing 6.0 themes are kept.

### 6.9 `.bul` files [MUST]

* **Association** (per user, no elevation): `HKCU\Software\Classes\.bul` -> ProgID `TigerTech.HolidayLights.Bulb`
  ("Holiday Lights Bulb", the redrawn 5.4 document icon, `open` = `"<install>\HolidayLights.exe" --open "%1"`), then
  `SHChangeNotify(SHCNE_ASSOCCHANGED)`. Written by the installer and by the General check box; repaired at start only
  when missing or pointing at a missing exe; never takes the extension from another program the user chose.
* **Double-click a `.bul`**: the program starts if needed and adds the file (3.2.10); Settings opens on Bulb Factory
  with the bulb selected (5.4: "just double-click on it to install it").
* Files copied into the My Bulbs folder by hand appear live. Damaged files are listed under "Show Damaged Bulbs" and
  never deleted automatically.

### 6.10 Install, data and uninstall [MUST]

* **Installer**: per user, no administrator rights (Inno Setup `PrivilegesRequired=lowest` or equivalent), into
  `%LOCALAPPDATA%\Programs\HolidayLights\`: `HolidayLights.exe`, `Holiday Lights.scr`, `Content\Bulbs` (1,501 `.bul`),
  `Content\Music` (46 songs; `reset.mid` is not shipped), `Content\Pictures` (the 11 pictures). One Start menu entry
  "Holiday Lights"; the `.bul` association; an uninstall entry under `HKCU\...\Uninstall\HolidayLights`. If 5.4 is
  running, the installer offers to close it (its Exit command). Last page: "Start Holiday Lights" (checked).
* **Data**:

| What | Where |
|---|---|
| Settings | `%APPDATA%\Holiday Lights\settings.json` (atomic writes, debounced 500 ms; `version` field for migrations) |
| Themes | `%APPDATA%\Holiday Lights\Themes\*.json` |
| Caches, logs, holding folder | `%LOCALAPPDATA%\Holiday Lights\Cache\`, `\Logs\`, `\Removed\` |
| Your bulbs, songs, pictures | `Documents\Holiday Lights\Bulbs`, `\Music`, `\Pictures` (created on first use) |

* **Uninstall**: removes the program, the `.scr`, the Run value and the association; if Holiday Lights is the screen
  saver, restores the remembered previous one (or none); asks "Also remove your bulbs, songs and pictures in
  Documents\Holiday Lights?" (default No) and removes settings and themes only if "Also remove my settings and themes"
  is checked. It never touches the 5.4 registry or folders.

### 6.11 Privacy and offline [MUST]

Holiday Lights never connects to the internet: no update check, no telemetry, and no web links except the project's
home page (https://github.com/starrlord/holidaylights): About links to it and opens it in the browser only when the
user follows it, and Help names it in "About Holiday Lights" and "Software Credits". Logs stay local. The About
window and Help > Credits say: "Holiday Lights works completely offline and never collects information about you."

---------------------------------------------------------------------------------------------------------------------

## 7. Defaults, scope and acceptance

### 7.1 Defaults (every setting)

Keys refer to `settings.json` (Appendix C). "Theme value" = one of the 13 values; for a fresh install it comes from
today's theme (Automatic themes); when a theme lacks it, the 5.4 default in this table applies.

| Setting (key) | Default | Rationale |
|---|---|---|
| `lights.on` (Show Lights) | true | Lights first. |
| `lights.drawing` (Bulb Drawing) | "desktop" (On Desktop); imported: 5.4 "Bulb Location" | 5.4 default; never in the way. |
| `lights.behindIcons` | true | PO decision 6; where 5.4 drew. |
| `lights.displays.disabled` | [] (every display on) | Every monitor decorated; fixes 5.4's main-only frame. |
| `lights.frameMode` | "eachDisplay" | Correct with any layout, mixed DPI and per-display pausing. |
| `lights.size` | "standard" (100 % of each display's scale) | The 2003 physical size next to everything else on screen (48 px at 150 %). |
| `look.pixels` | "smooth" (MMPX) | Keeps the hand-drawn look at fractional scales such as 150 %. |
| `look.glow` | "soft" | Festive real light without washing out the art. |
| `look.smoothFading` | true | Closer to real bulbs; at 120 ms it keeps the 5.4 rhythm. |
| `look.smoothSaverMotion` | true | Same physics, smoother picture. |
| Look preset (derived) | Modern Glow | The four values above. |
| Arrangement (`current.arrangement`, theme value) | newcomers and factory-default imports: today's Automatic theme (Halloween on Oct 8; Classic Lights between holidays); customized imports: their 5.4 arrangement; a theme lacking it: the 5.4 default = Christmas 1 (top Standard Bulbs; right and left Standard Bulbs + Snow Family; bottom Snow Family + Jolly Holly; Jolly Holly in every corner) | Seasonal for newcomers; a fan's own lights for fans. |
| `current.flash.pattern` (theme value) | from the theme; 5.4 default Flash Together | 5.4. |
| `current.flash.interval` (theme value) | from the theme; 5.4 default 5 (300 ms) | 5.4. |
| Checked songs (`current.music.disabledSongs`, theme value) | from the theme; 5.4 default all checked | 5.4 ("Disabled Music" empty). |
| `current.music.mode` Play the Chosen Songs (theme value) | from the theme; 5.4 default Always | 5.4. |
| `music.enabled` Play Holiday Music | false for newcomers and factory-default imports; customized imports: true unless their mode was Never | No surprise sound; fans keep their music. |
| `music.volume` | 60 % | The GS synth at full CC7 is loud. |
| `music.midiDevice` | "" = automatic (first "GS Wavetable" device, else device 0) | Present on every Windows 11 PC; never the mapper. |
| `music.syncOffsetMs` | 40 | The way from the MIDI device to the listener, added to the device's own latency (GS Wavetable Synth 190 ms, measured; R3 resolved). |
| Screen saver animation (theme value) | 5.4 default Snow | 5.4. |
| Style (theme value) | derived from the animation (Snow: Bounce Off Sides) | 5.4 rule. |
| Message (theme value) | 5.4 default "Happy Holidays!" | 5.4. |
| Font (theme value) | 5.4 default Arial, 36 pt, Bold | 5.4 (-48 px at 96 DPI). |
| Message color (theme value) | 5.4 default #FF0000 | 5.4. |
| Background color (theme value) | 5.4 default #000000 | 5.4. |
| Picture (theme value) | 5.4 default Santa Candle | 5.4. |
| Placement (theme value) | 5.4 default Center | 5.4. |
| `saver.showOn` | "all" | Fixes black secondary monitors; Main Display Only reproduces 5.4. |
| `saver.previous` | null | Recorded only when Holiday Lights becomes the screen saver. |
| `colors.custom` | 16 x #000000, or the imported 5.4 values | 5.4. |
| `calendar.enabled` (Automatic themes) | true for newcomers and factory-default imports; false for customized imports | Seasonal delight; fans' settings never replaced unasked. |
| `calendar.entries` | 5.11 table | Classic themes on their holidays; Chanukah and the seasons opt-in. |
| `calendar.region` | the Windows region (GeoInfo) | Correct holidays and hemisphere. |
| `calendar.between` | "Classic Lights" | The lights never vanish between holidays. |
| `calendar.notify` | true | One friendly notification per switch. |
| `hotkeys.location` | on, Ctrl+Alt+Shift+B; imported: the 5.4 letter and on/off with Ctrl+Alt+Shift | Keeps the 5.4 job and letter without taking the browsers' Ctrl+Shift+B. |
| `hotkeys.lights` | off, Ctrl+Alt+Shift+L | Available, not registered unless wanted. |
| `startup.auto` | newcomers and factory-default imports: true (visible on the Welcome card); customized imports: the 5.4 state | A holiday decoration should come back after a restart; the user sees and controls it on day one. |
| `rest.fullScreen` | true | Never over games and videos; no Fullscreen Optimizations penalty. |
| `rest.presentation` | true | |
| `rest.energySaver` | "useLessPower" | Festive and frugal. |
| `rest.musicFullScreen`, `rest.musicFocus`, `rest.musicLock` | true, true, true | Courtesy; no music to an empty room. |
| `accessibility.limitFlashing` | true if Windows "Animation effects" is off at first run, else false | Safe for people who asked Windows for less motion, without stopping the lights. |
| `ui.decorateWindow` | true | Festive signature. |
| `ui.gallery.view` / `ui.gallery.sort` | "tiles" / "original" | The 49 originals first, as in 5.4. |
| Show filter | "All Bulbs", reset every time Settings opens (not stored) | 5.4. |
| `ui.settings.lastPage` | "home" | |
| `bulbs.favorites`, `bulbs.hidden`, `songs.hidden`, `pictures.hidden` | empty | |
| `bulbs.categoryOverrides` | empty; imported 5.4 "Included Bulb Categories" | |
| `files.associateBul` | true | 5.4 registered it on every start. |
| `themes.lastName` | "" (then the last loaded or saved theme) | Save Theme pre-fill (5.4). |
| Window size | 1240 x 800 DIP clamped to 92 % of the work area; minimum 760 x 560 | Fits a 1366 x 768 laptop and shows the frame and the Bulb List side by side. |

### 7.2 Shipped themes (exact)

**The 11 classic themes** (the 5.4 installer themes, golden `default-themes.json`, values exactly as
stored) plus the two values the installer did not store: background black; style derived from the animation. Font
sizes are |lfHeight| x 72 / 96. Bulb names are built-in bulbs ("16"/"32" = the 16/32 Pixel Spacer).

| Theme | Top / Right / Bottom / Left | Corners | Pattern, interval | Songs; play mode | Screen saver: animation, style, picture, message, font, color |
|---|---|---|---|---|---|
| Blank Slate | - / - / - / - | none | Flash Together, 5 | none; Always | (None), -, (None), "", Arial 36 Bold, #FF0000 |
| Chanukah | Dreidels, 16, Menorahs, 16 / 16, Dreidels, 16, Menorahs / Dreidels, 16, Menorahs, 16 / 16, Menorahs, 16, Dreidels | none | Bulb Chase, 5 | Chanukah Song, Dreidle, Hava Nagilah; Intermittently | Dreidels, Bounce Off Sides, (None), "Happy Chanukah!", Footlight MT Light 48 Bold, #00FF00 |
| Christmas 1 | Standard Bulbs / Standard Bulbs, Snow Family / Snow Family, Jolly Holly / Standard Bulbs, Snow Family | Jolly Holly x4 | Flash Together, 5 | the 31 Christmas songs; Always | Snow, -, Santa Candle Center, "Merry Christmas!", Lucida Handwriting 48 Bold, #FF0000 |
| Christmas 2 | Standard Bulbs, Shiny Baubles / Standard Bulbs, Shiny Baubles / Standard Bulbs, Snow Family / Standard Bulbs, Shiny Baubles | Jolly Holly x4 | Flash Together, 5 | the 31 Christmas songs; Always | Santa, Bounce Off Sides, Snowman Center, "Merry Christmas!", Lucida Handwriting 48 Bold, #FF0000 |
| Easter Eggs | Pastel Easter Eggs, Striped Easter Eggs / same / Pastel Easter Eggs, Spring Flowers, Striped Easter Eggs / Striped Easter Eggs, Pastel Easter Eggs | none | Alternating, 7 | none; Always | Easter Eggs, Gravity Well, Easter Bunny Center, "Happy Easter!", Comic Sans MS 42 Bold, #FFFF00 |
| Halloween | Jack-O-Lanterns, Ghosts / Jack-O-Lanterns, The Grim Reaper / Jack-O-Lanterns, Zombie Tombstones / Jack-O-Lanterns, The Grim Reaper | Autumn Leaves top-left and top-right | Flash Together, 5 | the 5 Halloween songs; Intermittently | Halloween, Attraction, Pumpkin Center, "Happy Halloween!", Creepy 60 Bold, #FF0000 |
| July 4th | 32, Sun, 32, Old Glory Bulbs / 32, Old Glory Bulbs, 32, Chili Peppers / 32, Old Glory Bulbs / 32, Old Glory Bulbs, 32, Chili Peppers | Chili Peppers x4 | Flash Together, 5 | Star Spangled Banner, Yankee Doodle Dandy; Intermittently | Happy Faces, Gravity Well, Flag Center, "Happy July 4th!", Arial Black 48 Bold, #FF0000 |
| New Year | Mini Bulbs / Mini Bulbs / Mini Bulbs, Party Hats / Mini Bulbs | Jolly Holly top-left and top-right | Bulb Chase, 1 | Auld Lang Syne (Swing), Auld Lang Syne; Always | Balloons, -, New Year Clock Center, "Happy New Year!", Impact 48 Regular, #FF00FF |
| St. Patrick's Day | Shamrocks / Shamrocks / Shamrocks, Party Hats / Shamrocks | none | Alternating, 5 | Danny Boy; Intermittently | Shamrocks, Bounce Off Sides, Pot of Gold Center, "Happy St. Patrick's Day!", Georgia Ref 48 Bold, #00FF00 |
| Thanksgiving | Cornucopias, Autumn Leaves, Live Turkeys / Dead Turkeys, Autumn Leaves / (= top) / (= right) | Autumn Leaves x4 | Bulb Chase, 5 | none; Always | Leaves, Falling Leaves, Turkey Center, "Happy Thanksgiving!", Figaro MT 60 Bold, #808000 |
| Valentine's Day | Mezmerized, Candy Hearts, Sweet Hearts / Candy Hearts, Sweet Hearts, Mezmerized / (= top) / (= right) | none | Flash Together, 5 | Bicycle Built for Two, Clementine; Intermittently | Valentine's Hearts, Bounce Off Sides, Hearts Center, "Happy Valentine's Day!", Mistral 48 Bold, #FF00FF |

Fonts not installed on most PCs (Footlight MT Light, Lucida Handwriting, Creepy, Georgia Ref, Figaro MT, Mistral) use
the look-alikes of 6.2.3 and are labeled in the Font picker.

**The 8 new themes** (built only from bulbs that exist - built-in bulbs and verified bundled add-on file stems,
`addon:` - and fonts that ship with Windows 11; background black unless stated):

| Theme | Top / Right / Bottom / Left | Corners | Pattern, interval | Songs; play mode | Screen saver: animation, style, picture, message, font, color |
|---|---|---|---|---|---|
| Autumn Harvest | Autumn Leaves / Autumn Leaves / Cornucopias, Autumn Leaves / Autumn Leaves | Cornucopias x4 | Alternating, 6 | Greensleeves, Joy of Man's Desire, Danny Boy; Intermittently | Leaves, Falling Leaves, Pumpkin Center, "Happy Autumn!", Georgia 42 Bold, #F28C28 |
| Bubble Lights | `addon:MulticolorBubbleLights` on all four edges | Shiny Baubles x4 | Flash Together, 4 | the 31 Christmas songs; Always | Baubles, Bounce Off Sides, Snowman Center, "Happy Holidays!", Georgia 42 Bold, #F4B942 |
| Christmas Twinkle | Standard Bulbs on all four edges | Jolly Holly x4 | Twinkle, 5 | the 31 Christmas songs; Always | Snow, -, Santa Candle Center, "Merry Christmas!", Segoe Script 48 Bold, #FF0000 |
| Classic Lights (the default between holidays) | Standard Bulbs on all four edges | Standard Bulbs x4 | Flash Together, 5 | Bicycle Built for Two, Clementine, Danny Boy, Greensleeves, Yankee Doodle Dandy; Intermittently | Balloons, -, (None), "", Arial 36 Bold, #FF0000 |
| Holiday Party | `addon:StarlightBulbs` / `addon:ShinyBulbs` / `addon:SparklieBulbs2` / `addon:ShinyBulbs` | Party Hats x4 | Dance to the Music, 3 | Deck the Halls (Reggae), Jingle Bells (Reggae), We Wish You a Merry Christmas (Reggae), God Rest Ye Merry Gentlemen (Reggae), Joy of Man's Desire (Reggae), O Christmas Tree (Swing), Auld Lang Syne (Swing), Hava Nagilah; Always | Balloons, -, (None), "Let's Celebrate!", Arial Black 48, #F4B942; background #0B1530 |
| Spring Garden | `addon:SpringFlowerLights` / `addon:SpringFlowerLights` / Spring Flowers / `addon:SpringFlowerLights` | Spring Flowers x4 | Slow Glow, 5 | Bicycle Built for Two, Clementine, Greensleeves; Intermittently | Balloons, -, (None), "Happy Spring!", Comic Sans MS 42 Bold, #8BC34A; background #1D3466 |
| Summer Nights | Paper Lanterns on all four edges | Moon x4 | Twinkle, 7 | Bicycle Built for Two, Clementine, Danny Boy; Intermittently | Heavens Above, Attraction, (None), "Enjoy the Summer Nights!", Georgia 42 Italic, #F4B942; background #0B1530 |
| Winter Wonderland | `addon:IcicleLights` / `addon:Snowflakes4` / Snow Family / `addon:Snowflakes4` | `addon:PastelSnowflakes` x4 | Slow Glow, 6 | Greensleeves, Joy of Man's Desire, Danny Boy; Intermittently | Snow Flakes, -, Snowman Center, "Let It Snow!", Georgia 42 Italic, #CFE8FF |

A build-time test verifies that every shipped theme's bulbs exist and have art for every slot they use, that its
songs and pictures exist, and that its fonts ship with Windows 11 (new themes) or have a listed look-alike (classic
themes).

### 7.3 MUST for 6.0, in build priority order

P0 = the first usable build (lights on every display, controllable, 5.4 users imported); P1 = the complete everyday
experience; P2 = completes 6.0. **Every row is required for the 6.0 release.**

| Priority | Item | Sections |
|---|---|---|
| P0 | Lights engine: three layer modes with fallback, retries and status; Each Display; per-display on/off; work areas; rebuilds; exact 5.4 layout with scaling; Smooth and Crisp pixel art; faithful decoding; the step clock; the five classic patterns exactly; Show Lights; pausing, power, lock and Remote Desktop rules; robustness; performance budget | 5.1-5.6, 5.12-5.14 |
| P0 | Tray icon and menu exactly as 2.2 (incl. double-click and the header strip) | 2.2 |
| P0 | Settings storage, single instance, command line, startup (Run value), per-user `.bul` association, offline-only | 6.6, 6.9, 6.10, 6.11, Appendix C |
| P0 | 5.4 import incl. the factory-default rule, theme import, add-on id re-creation, content matching, leftovers and the report | 6.8 |
| P0 | Bulb Factory core: frame editor with 6 types per edge, live exact preview, targets and try-on, Bulb List (search, Show, Sort, Tiles, Details, favorites, In Use, My Bulbs, Removed Bulbs), selected-bulb bar, Add To flyout, drag-and-drop rules, context menus, file import, keyboard and screen-reader model, Flash Settings and Bulb Drawing groups | 3.2 |
| P1 | Control kit; Settings window shell (navigation, OK/Cancel/Help with the full snapshot, Undo/Redo, holding folder, string of lights, Peek) | 3.0, 2.3, 4.3.1 |
| P1 | Home page | 3.1 |
| P1 | Look presets, glow, smooth fading, flash limit, reduced-motion and High Contrast rules; power-up wave and theme transition | 4.3-4.5, 5.5.3, 5.8, 5.9 |
| P1 | New patterns Twinkle and Chase Around the Screen (discrete; on the opacity-animation model) | 5.7 |
| P1 | First run: Welcome card in all variants, notifications, snackbars, InfoBars, on-screen pill | 2.5, 3.11, 3.12 |
| P1 | Themes page: cards, Save Theme, Restore Built-In Themes, Recent Settings, 19 shipped themes, Automatic themes with the region rules, Theme Calendar dialog | 3.6, 5.11, 6.4, 7.2 |
| P1 | Music Box page and engine: own MIDI sequencer, NAudio, Play Holiday Music, modes, shuffle bag, volume, MIDI output, song list with credits and chips, problems | 3.4, 6.1 |
| P1 | Screen Saver page and `/s /p /c`: all modules and styles, every display, Smooth Motion, live preview, status card and the only-on-request install, Color dialog, font picker | 3.5, 3.8.3, 6.2 |
| P1 | General page; hot keys with the recorder, AltGr check and pill | 3.7, 3.8.2, 6.6.4 |
| P1 | "One frame around all displays" (PO-1): outline algorithm, seam-aware pieces, corners, Frame radios | 5.2.4, 3.7 |
| P1 | Per-user installer and uninstaller | 6.10 |
| P2 | Slow Glow and Dance to the Music (MIDI note and audio beat events, latency offset, event stream to the saver), "Lights and Music" card, Holiday Party behavior | 5.7, 5.10, 3.4.5 |
| P2 | Bulb Editing, Edit Categories, New Category, Bulb Credits, Export Bulb File, 5.4-compatible `.bul` writer | 3.3, 6.3 |
| P2 | About with the 2003 banner and complete credits; Help window with every topic, search and F1; descriptions and tooltips (Appendix A) | 3.9, 3.10, 6.5, 6.7 |
| P2 | Accessibility pass (keyboard map, Narrator and NVDA, High Contrast) and the acceptance scenarios of 7.5 | Appendix B, 7.5 |

### 7.4 NICE-TO-HAVE (after 6.0, in this order)

| # | Item | Why it waits |
|---|---|---|
| N1 | (promoted to MUST P1 by PO-1: "One frame around all displays", 5.2.4) | |
| N2 | Waves pattern (5.7) | Small once the opacity-animation model exists. |
| N3 | Combination pattern (5.7) | Small; depends on N2 for its full list. |
| N4 | Theme export and import (`.hltheme`) | Sharing with family without a server. |
| N5 | "Lights Keep Time with the Music" for Twinkle and Slow Glow (beat-locked step period) | Needs measured latencies; Dance covers music in 6.0. |
| N6 | Desktop snow: light snowfall behind the icons piling on the bottom bulbs, reusing the Snow module | Very festive; needs a CPU/GPU budget check. |
| N7 | "Fit" picture placement | 11 pictures are small; Center is faithful. |
| N8 | Snooze: "Turn Off the Lights for 1 Hour" / "Until Tomorrow" | Show Lights covers the need. |
| N9 | "Add Holiday..." custom calendar rows (birthdays, anniversaries) | The fixed list covers every shipped theme. |
| N10 | Message placeholders in the screen saver text ("{days until Christmas}") | Charming, not essential. |
| N11 | "Hide the Lights from Screen Sharing" (capture exclusion for in-front and on-top layers) | Presentations already hide the lights. |
| N12 | Built-in bulbs as screen saver animations; a static PNG as a new bulb | 5.4 allowed add-ons and GIFs only. |
| N13 | Artist links in About (filter the Bulb List by artist) | |
| N14 | Picture and message on every display in the screen saver | Main display matches 5.4. |
| N15 | Windows text-size scaling up to 225 % beyond Fluent defaults | WPF does not honor TextScaleFactor; needs manual scaling everywhere. |
| N16 | "Roll the Credits" animation in About; a countdown chip on Home ("12 days until Christmas") | Delight, not core. |
| N17 | "Recently Used" Show filter; compact icon-only gallery view | |
| N18 | Pause music during calls (microphone in use) | |
| N19 | Per-display arrangements | 5.4 had one arrangement; adds UI weight. Not planned. |

**Out of scope:** online bulb downloads or sharing; any trial, licensing or payment; languages other than English (all
strings are externalized); macOS and Linux; an MSIX package (the screen saver path and registry needs fit a per-user
installer better); per-bulb "standard GIF" decoding (PO decision 3).

### 7.5 Acceptance scenarios (definition of done)

1. **Commissioning PC, first launch** (5.4 at factory defaults; two 3840 x 2160 at 150 %, second at x = -3840; broken
   5.4 screen saver; October 8): within 2 s warm (6 s cold) both monitors show complete Halloween frames at scale
   1.5, behind the desktop icons, each framed to its work area above its 72 px taskbar; no sound; the Welcome card
   says "Welcome back!" with "Use My 2003 Lights" and the screen saver "Fix It"; Themes lists the 11 classic themes
   once each and the 8 new ones.
2. **"Use My 2003 Lights"** on that PC: Christmas 1 bulbs (Standard Bulbs; Standard Bulbs + Snow Family; Snow
   Family + Jolly Holly; Jolly Holly corners) at 48 px, Flash Together every 300 ms; screen saver Snow, Santa Candle,
   "Happy Holidays!"; Automatic themes off; the first song starts when the card closes.
3. **Customized 5.4 user** (registry fixture: Halloween arrangement, Bulb Chase interval 3, Intermittently, hot key
   letter H turned off, a user theme "Grandma's Lights", one add-on bulb in the old Bulbs folder that equals a bundled
   bulb except for a region cache, one that is not bundled): everything imported as-is; Automatic themes off; music on,
   starting after the card closes; hot key off with letter H; "Grandma's Lights" under My Themes; the first add-on
   maps to its `addon:` id, the second is copied into My Bulbs; the report lists every item.
4. **Newcomer, fresh PC, October 8**: Halloween within 2 s; no sound; the Welcome card offers music; tray > Themes >
   Christmas 1 switches in two clicks (G2) and turns Automatic themes off.
5. **One bulb on the whole frame** (G3): double-click the tray icon, Bulb Factory, double-click Candy Canes: every edge
   and corner shows Candy Canes; the snackbar's Undo restores the previous arrangement.
6. **Keyboard only** (G4): Tab to the frame editor, arrows to the Left Edge "+", Enter, type "snow", arrow to Snow
   Family, Enter: it is added to the left edge; Ctrl+Z removes it; Narrator announces both.
7. **Layout fidelity** (G8): on a 1920 x 1080 display at 100 % with a 48 px bottom taskbar (work area 1920 x 1032),
   every bulb position equals the golden layout (default theme: bottom 54 bulbs with gap 20/54, sides 28 with gap
   0.5714; Standard Bulbs everywhere: top 58, sides 30 with gap 8/30); the five classic patterns produce its film
   strips; on the reference PC with Standard Bulbs everywhere: top 78 bulbs, sides 41 with gap 24/41.
8. **Cancel** (5.4 bugs): change only the pattern and the speed, press Cancel, restart: the old pattern and speed are
   back. Change the screen saver background color, press Cancel: it is restored. Remove a song, press Cancel: it is
   back. Add a bulb, press Cancel: the bulb is still there.
9. **Remove Bulb** (5.4 bug): remove a My Bulbs bulb used on two edges: the edges update at once and stay updated
   after a restart; Undo brings back file and arrangement; after closing Settings the file is in the Recycle Bin.
10. **Screen saver**: "Preview Screen Saver" never changes `SCRNSAVE.EXE`; "Use Holiday Lights as My Screen Saver"
    writes the full long path; Windows starts it after the timeout on both monitors; "Stop Using It" restores the
    previous value; `/p` draws inside Windows' dialog; `/c` with the app closed opens a settings-only session.
11. **Tray clicks** on the reference PC: a single left or right click opens the menu at once; a double-click closes
    it and opens Settings; Enter on the icon opens Settings; the menu matches 2.2 exactly.
12. **Resting and robustness**: a full-screen game on monitor 2 hides only monitor 2's lights within 1 s and pauses
    music; both return when it exits. In each layer mode Win+D keeps the lights visible and switching virtual
    desktops keeps them on every desktop. After an Explorer restart lights and tray icon return within 3 s in the
    chosen mode. On Top lights stay below an auto-hide taskbar.
13. **Mouse held**: dragging a window for 10 s never freezes the lights (5.4 bug).
14. **Performance** (G5): the 5.14 budgets on the reference PC.
15. **Reduced motion**: with Windows animation effects off before the first run, the lights flash; "Limit Flashing"
    is on; speeds 8-9 are disabled; UI animations are off; nothing flashes faster than 3 times per second.
16. **Music and lights**: Holiday Party with music on reacts to notes; with music off it glows slowly; while music
    plays with Flash Together, consecutive steps stay 300 ms +- 2 ms apart (classic timing untouched).
17. **Hot keys**: Ctrl+Alt+Shift+B toggles On Desktop / On Top with the pill; a combination that types a character
    with AltGr on an installed layout is refused; Ctrl+Shift+B can be recorded after the warning.
18. **Bulb Editing round trip**: a GIF becomes a bulb, gets a second flavor and a category, is saved, loads under
    the 5.4 rules, and exports with "Export Bulb File...".
19. **Automatic themes** (simulated dates, US region): Oct 31 Halloween; Nov 1 Thanksgiving; Nov 27, 2026 Christmas 1;
    Dec 31 New Year; Jul 15 Classic Lights; no switch while Settings is open; one notification per switch; its
    InfoBar's Undo restores the previous settings.
20. **High Contrast and screen readers**: every page usable in High Contrast; Narrator and NVDA read every control
    (G4).

### 7.6 Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | "Behind the icons" relies on undocumented shell structure that changed in 24H2 | Structure detection, fallback chain, retries, status line, "Try Again" (5.1). |
| R2 | Additive glow under Windows HDR / Advanced Color is unverified | Test; halve glow intensity on HDR displays if clamping looks wrong (5.9). |
| R3 | GS synth audio latency (40 ms) is an estimate | Resolved (review r1): the GS synth sounds 216-220 ms after the send; events are stamped send + 190 ms (device) + `music.syncOffsetMs`. |
| R4 | Ctrl+Alt hot keys collide with AltGr characters | AltGr check (3.8.2). |
| R5 | Win+D and virtual-desktop behavior of owned and topmost windows is unverified | Rainmeter fallback, virtual-desktop safety net, acceptance 7.5 #12. |
| R6 | The tray double-click may be swallowed by the open menu on some configurations | Acceptance 7.5 #11; Enter and the bold "Holiday Lights Settings..." remain. |
| R7 | WPF's Fluent theme is still "in progress" in .NET 10 | In-house control kit with explicit templates; High Contrast tests. |
| R8 | The high-resolution waitable timer in an occluded window-owning process is unverified | Measure step jitter in the real app (acceptance 7.5 #16). |
| R9 | Some add-on bulbs are very large (215 of 1,501 above 128 px) or have opaque backgrounds | Faithful drawing; "Big" tag and size in the details. |
| R10 | Fast classic speeds flash up to 8 times per second | Faithful by default; "Limit Flashing" (5.5.3), on by default with reduced motion. |
| R11 | Holiday dates are culture-specific | Region rules; every entry switchable and editable; Chanukah and seasons opt-in. |
| R12 | Classic theme fonts are rarely installed | Look-alike substitutes, labeled. |
| R13 | `Documents` may be redirected to OneDrive | Works; files sync; the holding folder is local. |
| R14 | Whether Windows' Screen Saver Settings lists a per-user `.scr` outside System32 is unverified | Holiday Lights sets and shows its own status (3.5.2); Windows' list is not needed. |

---------------------------------------------------------------------------------------------------------------------

## Appendix A - Descriptions and tooltips (from the 5.4 "What's This?" popups, updated)

| Control | Text (description or tooltip) |
|---|---|
| Bulb List | "Shows the bulbs you can use. Double-click a bulb to use it, or drag it into the boxes around your screen. Right-click a bulb to edit it, see its credits, or remove a bulb you've added. (Bulbs that came with Holiday Lights 5.4 can't be removed.)" |
| Top-Left / Top-Right / Bottom-Left / Bottom-Right box | "Represents the <top-left> corner of your screen. Drag a bulb here, or select this box and double-click a bulb." |
| Top / Right / Bottom / Left Edge box | "Represents the <top> edge of your screen. Up to six kinds of bulbs repeat along the edge in this order." |
| Whole Frame / All Edges / All Corners | "Double-clicking a bulb puts it on every edge and in every corner." / "... on all four edges; the corners stay." / "... in all four corners; the edges stay." |
| Peek | "Hide this window for a moment so you can see your desktop. Hold to keep it hidden." |
| Clear All Bulbs | "Takes every bulb off the screen. You can undo it." |
| Flash pattern | "Changes the pattern in which the bulbs flash." |
| Speed slider | "Changes the speed at which the bulbs flash. Slower speeds use less of your computer's power." |
| Smooth Fading | "Light bulbs fade on and off like real bulbs instead of switching instantly." |
| Add Bulb... | "Adds a bulb file (.bul) to the list, or turns an animated GIF picture into a new bulb." |
| Edit Bulb... / Edit Categories... | "If you made the bulb yourself, you can edit its name, description, pictures and more. If someone else made it, you can edit its categories." |
| Bulb Credits | "Shows who made this bulb and its copyright." |
| On Desktop | "Shows the bulbs on your desktop, below all your windows, so they never get in your way." |
| Behind the Desktop Icons | "Your desktop icons stay in front of the bulbs and keep working. Uncheck it to draw the bulbs in front of the icons, still behind every window." |
| On Top | "Shows the bulbs above all windows so they're always visible. Clicks go through them, and they hide while a full-screen app is open." |
| Load Theme... / Save Settings As Theme... | "Replaces your bulb, music and screen saver settings with a saved theme." / "Saves your current bulb, music and screen saver settings as a theme." |
| Bulb Size | "How big the bulbs are, compared with everything else on each display." |
| Look | "Modern Glow: smooth pixels, a soft glow and fading. Bright Glow: a stronger glow. Classic 2003: exactly like Holiday Lights 5.4." |
| Pixels | "Smooth keeps the hand-drawn look at any size. Crisp shows square pixels, like 2003." |
| Glow | "Lit bulbs give off a soft light that brightens the wallpaper around them." |
| Show On (displays) | "Choose which displays get lights." |
| Identify | "Shows each display's number on the display." |
| Play Holiday Music | "Turns all music on or off. Themes can't turn music on while this is off." |
| Song check box | "Controls whether Holiday Lights ever picks this song when randomly picking music." |
| Play button | "Plays this song now." |
| Add Song... | "Adds songs to the list. The files are copied to your My Music folder so they're always available." |
| Never | "Prevents Holiday Lights from playing music at any time." |
| Only When the Screen Saver is On | "Plays music only when the Holiday Lights screen saver is showing." |
| Only When the Screen Saver is Off | "Plays music only when the Holiday Lights screen saver is not showing." |
| Always | "Plays music in the background at all times." |
| Intermittently | "Plays a song in the background at one to three minute random intervals." |
| Volume | "Changes how loud Holiday Lights plays music. Other apps aren't affected." |
| Make the Lights Dance to the Music | "The lights play along with the music. It changes the flash pattern in the Bulb Factory." |
| Text box (screen saver) | "Type any text you want to appear on the screen saver." |
| Clear | "Removes the text message from the screen saver." |
| Font, Size, Bold, Italic, Underline, Strikeout, Text Color... | "Change the font, size, style and color in which the text message appears." |
| Animation tiles | "Animations appear in addition to the bulbs around the edge of the screen. Click an animation to choose it." |
| Use a Bulb as the Animation... | "Lets one of your add-on bulbs float around the screen saver." |
| Style | "Chooses a different movement style for the animation. If this box is dimmed, the animation has its own movement that can't be changed." |
| Picture tiles | "Chooses the background picture. Right-click a picture to remove it." |
| Placement | "Center puts the picture in the middle; Tile repeats it; Stretch makes it the same size as your screen, even if that distorts it." |
| Add... (picture) | "Adds a picture. It's copied to your My Pictures folder." |
| Background Color... | "The color around the picture, or of the whole background if there's no picture. A picture that covers the screen hides it." |
| Preview Screen Saver | "Shows the screen saver now, with the current settings. It doesn't change your Windows screen saver." |
| Use Holiday Lights as My Screen Saver | "Makes Holiday Lights your Windows screen saver. Stop Using It brings back the one you had." |
| Show On (screen saver) | "All Displays shows the screen saver on every display. Main Display Only leaves the others black, like Holiday Lights 5.4." |
| Smooth Motion | "Moves snow, balloons and text smoothly. Turn it off for the 2003 look." |
| Change Themes Automatically on Holidays | "On a holiday, its theme replaces your bulb, music and screen saver settings. Your previous settings are kept in Recent Settings." |
| Automatically Start Holiday Lights | "Controls whether Holiday Lights runs automatically each time you sign in to Windows." |
| Hot key rows | "This is the key combination Holiday Lights uses. To change it, click Change... and press the new keys." |
| Limit Flashing to 3 Flashes per Second | "Keeps the lights flashing slowly enough to be comfortable for people who are sensitive to flashing lights." |
| OK / Cancel / Help | "Closes this window and keeps your changes." / "Closes this window and undoes every change made since you opened it. Things you added or saved are kept." / "Shows help for this page." |
| Undo / Redo | "Undo: <action>" / "Redo: <action>" |
| Bulb Editing: Bulb Name / Description | "Type the name of the bulb. It's shown in the bulb list." / "Type the description of the bulb. It's shown in the bulb list." |
| Author's Name and E-Mail Address / Copyright Message | "Your name and e-mail address. They're shown when someone views the bulb credits." / "A copyright notice. It's shown when someone views the bulb credits." |
| Categories / New Category... | "Check the categories this bulb belongs to. You can then show a single category in the bulb list." / "Adds a new category to the list." |
| Slot map / slot combo | "You can use different GIF animations for different sides or corners. Choose the one you want to preview or change." |
| Flavor combo | "A side can have up to eight different GIF animations. Each animation is called a flavor; they repeat along the edge. If this list is dimmed, you're viewing a corner or the bulb list preview, which have one animation each." |
| Preview (editor) | "Shows the selected side or corner. With Bulb List Preview selected, drag the white square (or use the arrow keys) to choose the 32 x 32 picture shown in the bulb list." |
| Change... / Remove Flavor | "Chooses a new GIF animation for the selected side and flavor." / "Removes the animation for the selected flavor. If this button is dimmed, the selected flavor can't be removed." |

## Appendix B - Keyboard map

**Global (system-wide)**

| Keys | Action |
|---|---|
| Ctrl+Alt+Shift+B (configurable; on) | Switch Between On Desktop and On Top |
| Ctrl+Alt+Shift+L (configurable; off) | Turn the Lights On or Off |
| Win+B, arrows, Enter or Space | Open Holiday Lights Settings from the tray icon |
| Win+B, arrows, Shift+F10 or the Menu key | Open the tray menu |

**Settings window**

| Keys | Action |
|---|---|
| Ctrl+1 ... Ctrl+6 | Home, Bulb Factory, Music Box, Screen Saver, Themes, General |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous page |
| F6 | Move between the navigation, the page and the bottom bar |
| F1 | Help for the current page or section |
| Ctrl+Z / Ctrl+Y (Ctrl+Shift+Z) | Undo / Redo (not inside text boxes) |
| Ctrl+F | Search (Bulb Factory, Music Box) |
| Esc | Close a flyout, menu or dialog; clear a search box; cancel a drag (never closes the window) |

**Bulb Factory**

| Keys | Where | Action |
|---|---|---|
| Arrows | frame editor | Move between boxes, chips and "+" (focus = target) |
| Ctrl+arrows | chip | Move the chip within its edge |
| Delete, Backspace | chip, corner | Remove |
| Enter | chip, "+", corner | Go to the Bulb List search to pick a bulb for it |
| Ctrl+V | edge box | Add the bulbs selected in the Bulb List |
| Shift+F10, Menu key | chip, box, tile | Context menu |
| Arrows, Home, End, PgUp, PgDn | Bulb List | Move between bulbs |
| Enter | Bulb List | Use the focused bulb for the target |
| Shift+Enter | Bulb List | Add it to every edge |
| Space | Bulb List | Select (never applies) |
| Ctrl+D | Bulb List | Add to / remove from Favorites |
| any letter | Bulb List | Start a search |

**Other places**: Music Box - Space toggles the focused song, Enter plays it, Delete removes it. Themes - Enter
loads, F2 renames, Delete deletes. Screen Saver - Ctrl+Enter previews. Bulb Editing - Ctrl+S saves, Esc cancels,
arrows (Shift = 8 px) move the white square. Running screen saver - any key, mouse button or mouse movement ends it.

## Appendix C - Data model

`%APPDATA%\Holiday Lights\settings.json` (System.Text.Json source-generated; atomic write via temp file + replace,
debounced 500 ms; `version` drives migrations; comments are explanations, not part of the file):

```jsonc
{
  "version": 1,
  "lights": { "on": true, "drawing": "desktop", "behindIcons": true,          // drawing: "desktop" | "onTop"
              "displays": { "disabled": [] }, "frameMode": "eachDisplay",      // monitor device ids
              "size": "standard" },                                             // small|standard|large|extraLarge
  "look": { "pixels": "smooth", "glow": "soft", "smoothFading": true, "smoothSaverMotion": true },
  "current": {                                                                  // the 13 theme values
    "arrangement": { "top": ["builtin:standard-bulbs"], "right": ["builtin:standard-bulbs", "builtin:snow-family"],
                     "bottom": ["builtin:snow-family", "builtin:jolly-holly"],
                     "left": ["builtin:standard-bulbs", "builtin:snow-family"],
                     "topLeft": "builtin:jolly-holly", "topRight": "builtin:jolly-holly",
                     "bottomLeft": "builtin:jolly-holly", "bottomRight": "builtin:jolly-holly" },
    "flash": { "pattern": "flashTogether", "interval": 5 },
    "music": { "disabledSongs": [], "mode": "always" },   // settings store UNCHECKED songs (5.4 "Disabled Music")
    "saver": { "animation": "Snow", "style": "bounceOffSides", "message": "Happy Holidays!",
               "font": { "family": "Arial", "sizePt": 36, "bold": true, "italic": false,
                         "underline": false, "strikeout": false },
               "color": "#FF0000", "background": "#000000",
               "picture": "bundled:Santa Candle.BMP", "placement": "center" }
  },
  "music": { "enabled": false, "volume": 60, "midiDevice": "", "syncOffsetMs": 40 },
  "saver": { "showOn": "all", "previous": null },        // previous: { scrnsaveExe, active, timeoutSeconds }
  "colors": { "custom": ["#000000", "..."] },            // 16 entries
  "calendar": { "enabled": true, "region": "US", "between": "Classic Lights", "notify": true,
                "entries": [ { "id": "halloween", "use": true, "rule": { "type": "fixed", "from": "10-01", "to": "10-31" },
                               "theme": "Halloween" } ] },
  "hotkeys": { "location": { "enabled": true, "modifiers": ["Ctrl", "Alt", "Shift"], "key": "B" },
               "lights": { "enabled": false, "modifiers": ["Ctrl", "Alt", "Shift"], "key": "L" } },
  "startup": { "auto": true },
  "rest": { "fullScreen": true, "presentation": true, "energySaver": "useLessPower",
            "musicFullScreen": true, "musicFocus": true, "musicLock": true },
  "accessibility": { "limitFlashing": false },
  "ui": { "decorateWindow": true, "settings": { "lastPage": "home", "window": null },
          "gallery": { "view": "tiles", "sort": "original" } },
  "bulbs": { "favorites": [], "hidden": [], "categoryOverrides": { "builtin:jack-o-lanterns": ["Halloween", "Autumn"] } },
  "songs": { "hidden": [] }, "pictures": { "hidden": [] },
  "files": { "associateBul": true },
  "themes": { "lastName": "" },
  "recentSettings": [ { "label": "Before Halloween (automatic)", "date": "2026-10-01T00:00:00", "values": { } } ],
  "import54": { "date": "2026-10-08", "factoryDefaults": true, "report": [ ] },
  "onboarding": { "welcomeShown": true, "closeNotified": false, "locationHotKeyHints": 0, "automaticEditHintShown": false }
}
```

Ids: bulbs `builtin:<slug>`, `addon:<file stem>`, `user:<file stem>` (never 5.4's colliding numeric ids); songs and
pictures `bundled:<file name>` / `user:<file name>`. Pattern values: `dontFlash`, `flashTogether`, `alternating`,
`bulbChase`, `randomFlashing` (5.4 values 0-4), `twinkle`, `slowGlow`, `chaseAround`, `danceToMusic`, `waves`,
`combination`. Play modes: `never`, `saverOn`, `saverOff`, `always`, `intermittently` (5.4 values 0-4). Calendar rule
types: `fixed` (from/to month-day), `easter` (offsets from Easter Sunday; Western or Orthodox by region),
`usThanksgiving` (from fixed to the fourth Thursday of November), `caThanksgiving`, `afterUsThanksgiving` (to fixed),
`chanukah`.

Theme file `%APPDATA%\Holiday Lights\Themes\<name>.json`:

```json
{
  "schema": "holidaylights.theme/1",
  "name": "Halloween",
  "shipped": "classic",
  "arrangement": {
    "top": ["builtin:jack-o-lanterns", "builtin:ghosts"],
    "right": ["builtin:jack-o-lanterns", "builtin:the-grim-reaper"],
    "bottom": ["builtin:jack-o-lanterns", "builtin:zombie-tombstones"],
    "left": ["builtin:jack-o-lanterns", "builtin:the-grim-reaper"],
    "topLeft": "builtin:autumn-leaves", "topRight": "builtin:autumn-leaves", "bottomLeft": null, "bottomRight": null
  },
  "flash": { "pattern": "flashTogether", "interval": 5 },
  "music": { "enabledSongs": ["bundled:Halloween - Eerie.mid", "bundled:Halloween - Funeral March.mid",
                              "bundled:Halloween - Haunted House.mid", "bundled:Halloween - Marionette.mid",
                              "bundled:Halloween - Scary.mid"], "mode": "intermittently" },
  "saver": { "animation": "Halloween", "style": "attraction", "message": "Happy Halloween!",
             "font": { "family": "Creepy", "sizePt": 60, "bold": true, "italic": false, "underline": false, "strikeout": false },
             "color": "#FF0000", "background": "#000000", "picture": "bundled:Pumpkin.BMP", "placement": "center" }
}
```

`shipped` is `classic`, `new` or absent (your themes); it drives the groups on the Themes page, "Restore Original" and
the "Changed" badge. A theme stores **checked** songs (5.4 "Enabled Music").

## Appendix D - Copy deck (strings not given above)

**Snackbars:** "Using <bulb> on the whole frame." / "... on all four edges." / "... in all four corners." [Undo] -
"Cleared all edges and corners." [Undo] - "Removed <bulb>." [Undo] - "Added <bulb>." [Undo] - "Added <n> bulbs."
[Show] - "Added 3 bulbs. 1 file was already in your bulbs." [Show] - "<theme> theme loaded." [Undo] - "<theme> theme
loaded. Automatic themes are off." [Undo] - "Deleted <theme>." [Undo] - "Removed <song>." [Undo] - "Added <n>
songs." [Show] - "Removed <picture>." [Undo] - "Settings reset." [Undo] - "Restored <n> themes." [Undo].

**InfoBars:** Bulbs - "<name> is already in your bulbs." / "Problem Importing File: Sorry, that bulb file is damaged
and can't be used." / "1 bulb file couldn't be read: <file>. It may be damaged." [Show in Folder] / "2 bulbs in your
arrangement are missing." [Remove Missing Bulbs] / "The bottom edge is full, so the bulb was not added there." /
"Saved <name>. To share it, right-click it and choose Export Bulb File..." / "Automatic themes are on: on <date> your
lights change to <theme>." [Save as Theme...] [Turn Off Automatic Themes] / "Holiday Lights had trouble drawing the
lights and switched to software drawing." Themes - "This theme uses 2 bulbs that aren't installed: <names>." /
"Automatic themes are off because you chose a theme." [Turn Back On] / "Holiday Lights switched to <theme> on
<date>." [Undo] [Turn Off Automatic Themes] / "1 theme couldn't be read: <file>." Music, Screen Saver, General -
the tables in 3.4.6, 3.5.2 and 3.7. Files - "Couldn't save your settings: <Windows reason>." / "Couldn't copy
"<file>": <Windows reason>." / "Some characters can't be saved in a bulb file and were replaced."

## Appendix E - Notes for the product owner (no action needed to build this spec)

* 269 of the 1,501 bundled bulb files carry copyright status 3, the value the 5.4 Sharing dialog wrote for "I do not
  know who created this artwork" (how Tiger Technologies' server treated it is unknown), and 113 carry status 0;
  file names such as 2pac, AlGore or CronoTrigger suggest real people and game characters. Credits show each bulb's
  fields verbatim. A content review before distribution is advisable.
* The screen saver pictures were "licensed from ArtToday" for use in the Holiday Lights screen saver; the music
  arrangements and bulb art "may not be used for other purposes without the author's permission". 6.0 uses them only
  inside Holiday Lights, as 5.4 did.
* The Tiger Technologies logo is deliberately not shown; the product name, the 5.4 banner art and the trademark line
  are used as decided by the product owner.
