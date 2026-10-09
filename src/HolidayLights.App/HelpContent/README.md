# HelpContent - Help topics, contents and credits

Owner: branding-docs. Every file here except this README is embedded in `HolidayLights.exe` as `help/<path>` and read
through `HelpContentStore` (`src/HolidayLights.App/Contracts/HelpContentModel.cs`).

| File | Content | Read by |
|---|---|---|
| `contents.json` | Books and topics (schema `holidaylights.help/1`); topic ids are the constants of `HelpTopics` | Help window (app-shell) |
| `topics/<id>.md` | One Markdown topic per id (PRODUCT-SPEC 6.7, rewritten from the 5.4 help file) | Help window |
| `images/<name>.png` | Pictures referenced from topics as `![...](images/<name>.png)` | Help window |
| `credits.json` | Credits (schema `holidaylights.credits/1`, `CreditsContent`; PRODUCT-SPEC 6.5) | About window, Help "Credits" |

Markdown subset the Help window renders: `#`/`##`/`###` headings, paragraphs, `-`/`*` and numbered lists, `**bold**`,
`*italic*`, `` `code` ``, tables, images (`images/...`), links to other topics (`topic:<id>`) and page buttons
(`settings:<page>` with home, bulbs, music, saver, themes or general). No web links (PRODUCT-SPEC 6.11). The 5.4 help
banner (`HeritageAssets.HelpBanner`, or its MMPX enlargement `Assets/Banner/HelpBanner.png`) is drawn above every topic
by the Help window, not referenced by topics.

Conventions the topics follow (checked by `tests/HolidayLights.Tests/Branding`):

- The first line is `# <title>`, the title of the topic in `contents.json`; it is the only `#` heading.
- No nested lists, block quotes, code blocks, rules or HTML; tables have a header row and a `|---|` delimiter row.
- Page buttons are links whose text is "Open the <page> page", on a line of their own at the end of the topic.
- UI labels are **bold** and spelled exactly as in the program; labels that open a window end with "…" (U+2026).
- Pictures are drawn at twice their display size and saved at 192 DPI: show each at its natural WPF size (its
  `Width` and `Height` in DIP), not stretched to the page width.
- The last section of `topics/art-copyright.md` ("Add-On Bulb Artists") and the `addOnArtists` of `credits.json` are
  generated from the bundled bulb files, and `docs/USER-GUIDE.md` is generated from these topics: after a change, run
  the branding generator (`assets/icons/README.md`).
