# assets/themes - the 19 shipped themes

Owner: core-settings. Embedded into `HolidayLights.Core` as `holidaylights/themes/<file>` (every file here except this
README; list them with `EmbeddedAssets.List("themes/")`). `ShippedThemes` (Core/Themes) reads them; they are the
originals behind "Restore Built-In Themes", "Restore Original" and the "Changed" badge, and the seed of a new themes
folder.

One `<theme name>.json` per shipped theme in the theme file format of PRODUCT-SPEC Appendix C (`ThemeDefinition`,
schema `holidaylights.theme/1`, `"shipped": "classic"` or `"new"`):

* the 11 classic installer themes, translated exactly from the 5.4 installer's registry values (golden
  `default-themes.json`): "Bulb Settings" ids to `builtin:<slug>` by id (`assets/builtin/table.json`), "Enabled Music"
  to `bundled:<file name>`, the LOGFONT to family / round(|lfHeight| x 72 / 96) pt / bold = weight >= 600, COLORREFs
  to `#RRGGBB`, pictures to `bundled:<file name>` or `(None)`. The installer stored no background colour and no
  style, so these files have neither: they load as black and as derived from the animation, exactly like 5.4;
* the 8 new themes of PRODUCT-SPEC 7.2, with every value stated (style and background included).

`tests/HolidayLights.Tests/Settings/ShippedThemesTests.cs` is the build-time check of PRODUCT-SPEC 7.2: the classic
files equal the 5.4 mapping of the golden installer themes, every theme's bulbs exist and have art for every slot they
use (golden `builtin-cells.json`, `bul-frames.json.gz`, the bundled `.bul` files), its songs and pictures are bundled,
and its fonts ship with Windows 11 (new themes) or have a listed look-alike (classic themes).
