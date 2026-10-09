# assets/icons - the Holiday Lights 6 visual identity

Owner: branding-docs. Everything here, and the art it produces, follows PRODUCT-SPEC 4.1 (festive, not cheesy), 4.2
(palette) and 4.7 (icons).

| Folder | Content |
|---|---|
| `generator/` | **BrandingGenerator**, a small WPF tool (RenderTargetBitmap) that draws every icon, illustration, banner and help picture from vector drawings and the 2003 art. A developer tool: not part of `HolidayLights.sln`, nothing ships from it. |
| `generator/cells/` | The 20 built-in bulb cells (PNG, cut from the 5.4 sheets in `assets/builtin`) that the help pictures draw. |
| `png/` | Masters for documents and installer art: the application and `.bul` document icons at 256 px, the four tray icons at 32 px. |

## What the generator writes

| File (under `src/HolidayLights.App`) | Content | Drawn by |
|---|---|---|
| `Assets/HolidayLights.ico` | The application icon, 16, 20, 24, 32, 40, 48, 64 and 256 px. A red C9 bulb with a white highlight, gold screw base and green socket; from 48 px it hangs on a green light-string cord in a soft warm glow ring. | `IconSet.App`, `BulbArt` |
| `Assets/Tray/TrayLitLight.ico`, `TrayLitDark.ico` | Tray icon "lights on" for light and dark taskbars, 16, 20, 24 and 32 px (100 % to 200 % scaling). | `IconSet.Tray` |
| `Assets/Tray/TrayUnlitLight.ico`, `TrayUnlitDark.ico` | Tray icon "lights off": dark red glass, grey highlight; on dark taskbars a faint light rim keeps it visible. | `IconSet.Tray` |
| `Assets/BulbDocument.ico` | The `.bul` document icon (the 5.4 icon group 2 redrawn): a Windows 11 page with a folded corner and the lit bulb, 16 to 256 px. | `DocumentArt` |
| `Assets/Illustrations.xaml` | The `AppResourceKeys.Illustration*` drawings: the three "Where Bulbs Are Drawn" cards (160 x 96) and the Welcome card's taskbar corner (320 x 120, light and dark). Generated XAML; the dictionary also merges `Tooltips.xaml`. | `IllustrationArt`, `XamlEmitter` |
| `Assets/Banner/AboutBanner.png` | The 2003 ABOUT banner with "Modern Edition 6.0" where 5.4 printed "version 5.4". | `BannerGenerator` |
| `Assets/Banner/AboutFlash1.png`, `AboutFlash2.png` | The two frames of the light strip above the banner (alternate every 500 ms). | `BannerGenerator` |
| `Assets/Banner/HelpBanner.png` | The 2003 help banner (`bm0`) for the top of every Help topic. | `BannerGenerator` |
| `HelpContent/images/*.png` | The pictures of the Help topics. | `HelpImageGenerator` |
| `HelpContent/credits.json` (`addOnArtists`) and the last section of `HelpContent/topics/art-copyright.md` | The add-on bulb artists, computed from the 1,501 bundled `.bul` files (PRODUCT-SPEC 6.5 item 3). | `ContentGenerator`, rules in `tests/HolidayLights.Tests/Branding/AddOnArtistCredits.cs` |
| `../../docs/USER-GUIDE.md` | The Help topics as one document. | `ContentGenerator`, rules in `tests/HolidayLights.Tests/Branding/UserGuideComposer.cs` |

`Assets/Tooltips.xaml`, the help topics, `contents.json` and the rest of `credits.json` are written by hand. The
add-on artist and user-guide rules live with the Branding tests, which check the committed files against them; the
generator compiles the same two files (linked in `BrandingGenerator.csproj`).

## Running it

From the repository root (set `HL_BUILD_ROOT` first, as for every build):

```
dotnet build assets/icons/generator -c Debug
<HL_BUILD_ROOT>/bin/BrandingGenerator/Debug/net10.0-windows/BrandingGenerator.exe all
```

Commands: `icons`, `illustrations`, `banners`, `help-images`, `credits`, `user-guide`, `all` (all of these), and
`review <folder>`, which writes review sheets (every icon size at 100 % and 400 % on light and dark taskbars, white and
a blue wallpaper; the illustrations at 100 % and 300 %; the banners as WPF shows them at 100 %, 150 % and 200 %). Run
`review` and look at the sheets after every change to the art. After changing a help topic, run `user-guide` (the
Branding tests fail while `docs/USER-GUIDE.md` is out of date).

## Design notes

- **Palette** (PRODUCT-SPEC 4.2): bulb red #E3342F, holly green #1F8B4C, warm gold #F4B942, snow white #F5F8FF, ice blue
  #CFE8FF, the night sky #0B1530 to #1D3466 behind bulb pictures, the heritage sky #DDEEFF behind the 2003 banners.
- **The bulb** is one parametric drawing (`BulbArt`). Below 40 px its 1 px contours sit on pixel centres and one row of
  gold stays visible between socket and glass, so the 16 px tray icon is as crisp as the 2003 one; from 40 px the
  socket gets its ribs and the base its threads.
- **Lit and unlit** differ in brightness and highlight (white or grey), never in shape, so the tray icon reads the same
  in both states.
- **Light and dark**: tray icons follow the taskbar (`SystemUsesLightTheme`); the taskbar-corner picture has a light
  and a dark version. The 2003 banners keep their sky in both modes because they are artwork (PRODUCT-SPEC 3.9); frame
  them with the 8 DIP rounded border the spec asks for.
- **Banners at any scale**: the PNG files are 4x the 2003 size (MMPX). Show them at their DIP size (ABOUT 480 x 94,
  strip 296 x 15, help banner 216 x 53) with `RenderOptions.BitmapScalingMode="Fant"`: MMPX plus area averaging, as
  PRODUCT-SPEC 3.9 asks.
- **Help pictures** are drawn at 2x and saved at 192 DPI, so WPF's natural size of each picture is its intended DIP
  size.
- **Character** (PRODUCT-SPEC 4.1): the colour and motion come from the 2003 bulb art and from light; the chrome stays
  plain Windows 11. No new mascots or clip art, no recoloured UI, no emoji.
