# assets/heritage - original Holiday Lights 5.4 art and data used by 6.0

Owner: contracts (read-only for every builder). Embedded into `HolidayLights.Core` as manifest resources named
`holidaylights/heritage/<path>`; open them with `EmbeddedAssets.Open(HeritageAssets.<Name>)`
(`src/HolidayLights.Core/Abstractions/Runtime/EmbeddedAssets.cs`).

Every file is a verbatim copy of a resource of Holiday Lights 5.4 (`Holiday Lights.exe`, PE timestamp 2003-11-12) or
of its help file. Nothing here was edited.

| File | Source | Content | Used by |
|---|---|---|---|
| `bitmaps/ABOUT.bmp` | RT_BITMAP `ABOUT` | 480 x 94, 8 bpp: the About banner | About window, Welcome card (app-shell) |
| `bitmaps/ABOUTFLASH1.bmp`, `ABOUTFLASH2.bmp` | RT_BITMAP | 296 x 15, 8 bpp: the light strip that alternates every 500 ms | About window, Welcome card |
| `bitmaps/WARNING.bmp` + `WARNINGMASK.bmp` | RT_BITMAP | 32 x 32, 4 bpp art + 1 bpp mask (black = opaque) | damaged bulb art (core-bulbs) |
| `bitmaps/DITHER.bmp` | RT_BITMAP | 8 x 8, 1 bpp checkerboard | dimmed outside of the Bulb Editing white square (bulb-factory) |
| `bitmaps/FLAKE.bmp` + `FLAKEMASK.bmp` | RT_BITMAP | 45 x 9, 1 bpp: five 9 x 9 flakes | Snow Flakes module (screensaver) |
| `bitmaps/BALLOON.bmp` + `BALLOONMASK.bmp` | RT_BITMAP | 126 x 50: six 21 x 50 balloons | Balloons module (screensaver) |
| `bitmaps/BALLOONSMALL.bmp` + `BALLOONMASKSMALL.bmp` | RT_BITMAP | 30 x 7: six 5 x 7 balloons (5.4 preview art) | optional, `/p` preview (screensaver) |
| `saver/3100.gif` ... `3105.gif` | RT_RCDATA 3100-3105 | Singing Tree, Dancing Demon, Skeleton, Gingerbread Man, Angel, Santa floaters | GenericModule GIF floaters (screensaver) |
| `saver/anim_table.json` | the program's data | the 21-entry table of the screen saver's picture animations, as JSON | GenericModule (screensaver) |
| `help/bm0.png` | the help file (`hlights.hlp`), picture `bm0` | the 5.4 help banner ("Holiday Lights" logo with lights) | top of every Help topic (app-shell, branding-docs) |
| `icons/ICON.ico`, `icons/2.ico` | icon groups `ICON` and 2 | the 5.4 application icon and `.bul` document icon | reference art for the redrawn icons (branding-docs) |

All bitmaps are Windows BMP files (14-byte BITMAPFILEHEADER + the resource bytes), uncompressed and bottom-up,
exactly like `assets/builtin/bmp/`. Masks use the colour table {0: white, 1: black}; **black = opaque**.

The 11 screen saver pictures are not here: they ship as content files (`Content/Pictures/*.BMP`, from the repository's
`content/Pictures/`); each is two concatenated BMPs and the **second** one is the picture.
