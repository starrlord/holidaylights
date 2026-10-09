Holiday Lights {{VERSION}} puts animated holiday lights around every monitor of your Windows PC: 1,550 bulbs, flash patterns that can dance to the Music Box, seasonal themes that change by themselves, and a screen saver.

## Download and install

1. Download **[{{SETUP_EXE}}]({{REPOSITORY_URL}}/releases/download/{{TAG}}/{{SETUP_EXE}})**.
2. Double-click it. Holiday Lights installs for you only, in `%LOCALAPPDATA%\Programs\HolidayLights`, so no administrator rights are needed. Everything it needs is inside, so it works on any Windows 11 PC, even offline.

Already have Holiday Lights 6? Run the new setup the same way: it updates your installation and keeps your settings, themes, bulbs, songs and pictures. To remove Holiday Lights later, use **Settings > Apps > Installed apps**.

Holiday Lights is not code-signed, so the first time you run the setup, Windows SmartScreen may say "Windows protected your PC". Choose **More info**, then **Run anyway**.

Holiday Lights runs on 64-bit Windows 11. It works completely offline and never collects information about you.

## Checksums (SHA-256)

```text
{{CHECKSUMS}}
```

To check the download in PowerShell, run `Get-FileHash .\{{SETUP_EXE}}` and compare the hash with the line above (case does not matter). `SHA256SUMS.txt` holds the same line for `sha256sum -c`.

---

The source code is open source under the MIT License. The bulbs, music and pictures remain the copyright of their authors (see [content/README.md]({{REPOSITORY_URL}}/blob/{{TAG}}/content/README.md)). Built by GitHub Actions from {{COMMIT}}.
