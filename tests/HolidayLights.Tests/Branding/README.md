# tests/HolidayLights.Tests/Branding

Owner: branding-docs. Namespace `HolidayLights.Tests.Branding`; run with

```
dotnet test tests/HolidayLights.Tests -c Debug --filter "FullyQualifiedName~HolidayLights.Tests.Branding"
```

| Tests | What they check |
|---|---|
| `HelpTopicTests` | One topic per `HelpTopics` id, titled as in `contents.json`; links only to topics, Settings pages and embedded pictures; the Markdown subset of `HelpContent/README.md`; no web links, payment or retired Windows words; the ellipsis character in labels; the spec texts of What's New and the flash patterns; the "click once (not twice)" advice. |
| `CreditsTests` | `credits.json` against PRODUCT-SPEC 6.5 and 6.1.4: the fixed texts, every built-in bulb credited, the add-on artists recomputed from the 1,501 bundled `.bul` files, the music credits against the bundled songs, the MIT licences; the Credits topics match the data. |
| `IconTests` | The `.ico` files named by `AppAssets`: every required size, 32-bit frames (PNG only at 256 px), lit brighter than unlit, the rim of the unlit icon on dark taskbars, the glow ring from 48 px, the document icon copied next to the program. |
| `IllustrationTests`, `TooltipTests` | `Assets/Illustrations.xaml` loaded through its pack URI as App.xaml does: the `AppResourceKeys.Illustration*` drawings and their stacking order, the light and dark taskbar corner, and every PRODUCT-SPEC Appendix A text in the merged `Tooltips.xaml`. |
| `PictureTests` | The MMPX-enlarged 2003 banners (4x, no colour the 2003 art lacks, the "Modern Edition 6.0" line) and the 2x, 192 DPI help pictures. |
| `UserGuideTests` | `docs/USER-GUIDE.md` equals the Help composed as one document, and all its links land. |

`AddOnArtistCredits.cs` and `UserGuideComposer.cs` hold the rules for the generated content; the branding generator
(`assets/icons/generator`) compiles the same files to write `credits.json`, the end of the Art Copyright topic and the
user guide. When a test says a generated file is out of date, run the generator (`assets/icons/README.md`).

Helpers (owner: contracts, `../Shared`): `TestPaths` (repository, golden and content paths), `StaThread` (WPF code on an
STA thread).
