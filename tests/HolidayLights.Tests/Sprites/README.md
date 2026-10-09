# tests/HolidayLights.Tests/Sprites

Owner: core-sprites. Namespace `HolidayLights.Tests.Sprites`; run with

```
dotnet test tests/HolidayLights.Tests -c Debug --filter "FullyQualifiedName~HolidayLights.Tests.Sprites"
```

| Tests | What they prove |
|---|---|
| `MmpxTests` | MMPX 2x and 4x are bit-exact against the upscaling prototype's references in `Golden/upscale` (transparent edge) and against the clamp-to-edge output of the reference Python port; the edge modes differ only near the border. |
| `PixelArtScalerTests` | Sizes are `ArtScale.Scale` for both styles; scale 1 is the original; Crisp at integer scales is nearest neighbour; Smooth at 2/4/8 is MMPX (+ nearest doublings); Smooth 1.25/1.5/1.75/2.5, Crisp 1.5 and the 1-bit threshold are bit-exact with the prototype's `BoxResample` outputs (`Golden/upscale`); output is premultiplied, without dark fringes. |
| `GlowBakerTests` | Sigma 0.22 x the shorter side capped at 24 art pixels (product-owner decision 1), margin ceil(3 sigma), alpha 0, the emissive rule (32 brighter in luma or in the brightest channel, so every colour of Standard Bulbs glows alike), lit colours, energy conservation, the normalized Gaussian. |
| `CpuCompositorTests` | Exact rounding, source-over (including colour above alpha, matching what DWM composes on Windows 11), additive light, clipping, vector path = scalar path. |
| `DrawLightsTests` | `DrawLights` at scale 1 is bit-exact with reference renders of three layout goldens; brightness, glow beneath the bulbs and clipped to the display, zoom/offset rounding, display filter, validation. |
| `SpriteProviderTests`, `SpriteCacheTests` | Key normalization (5.4 flavor/frame rules), one computation per sprite under concurrency, halos (never baked by the prefetch; stored on first use only for prefetched sprites; compact for long strips), prefetch persistence read back by a new provider without decoding, eviction, damaged files, LRU budget, disk trimming (deterministic, and repeated during a long session), concurrent writes of one key. |
| `ScenePreviewRendererTests` | A scene rendered at 150 % with Smooth art and Soft glow equals its golden; one display = backdrop + band + `DrawLights`; the desktop = its displays side by side; lights off, overrides, static states. |
| `PerformanceTests` | Scaling the 890 distinct cells of the 49 built-in bulbs at 150 % takes under 300 ms (Smooth and Crisp). |

Test doubles: `BuiltInTestBulbs` (the 49 built-in bulbs sliced from the embedded RT_BITMAP sheets with the rectangles of
`Golden/builtin-cells.json`, each cell checked against its golden hash), `FakeBulb`/`FakeResolver` (synthetic bulbs),
`GoldenLayout` (a `Golden/layout/*.json.gz` case as a `LightsLayout` with its per-frame phases).

## Golden images (`Golden/`)

| File | Made by |
|---|---|
| `upscale/*.png` | The upscaling prototype's references: `1x.png` (the bulb sheet 1001/1002) and its MMPX 2x/4x, nearest-neighbour and area-averaging (`BoxResample`) results, samples outside the image read as transparent. |
| `mmpx-2x-clamp.png` | The reference Python port of MMPX (unmodified, clamp-to-edge) applied to `upscale/1x.png`. |
| `layoutsim-default-1920x1080-p1-f0.png` | The reference model of the 5.4 layout: 1920 x 1080 screen, 48 px taskbar, default theme, pattern 1, frame 0. |
| `layoutsim-mixed-1280x1024-p3-f1.png` | The same model: 1280 x 1024, 40 px taskbar, the mixed example theme (golden `layout/mixed-example.json.gz`), pattern 3, frame 1. |
| `layoutsim-halloween-1280x1024-p4-f5.png` | The same model: 1280 x 1024, 40 px taskbar, the Halloween installer theme (`Golden/layout/themes/installer-halloween.json`), pattern 4 with seed 1, frame 5. |
| `scene-default-1024x768-zoom150.png` | `ScenePreviewRenderer` itself (default theme, layout golden 0,0,1024,738, pattern 1, frame 0, zoom 1.5), inspected visually before it was adopted; a regression guard for Smooth scaling + glow. Regenerated in the review round when red Standard Bulbs started to glow like the other colours (the only change: halos around the red bulbs). |

When a golden image differs or is missing, the test writes the actual image to
`%TEMP%\HolidayLightsTests\Sprites\<name>.actual.png` for inspection.
