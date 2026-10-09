Holiday Lights {{VERSION}} puts animated holiday lights around every monitor of your Windows PC: 1,550 bulbs, flash patterns that can dance to the Music Box, seasonal themes that change by themselves, and a screen saver.

## Download

| File | Choose it when |
|---|---|
| **[{{SELF_CONTAINED_ZIP}}]({{REPOSITORY_URL}}/releases/download/{{TAG}}/{{SELF_CONTAINED_ZIP}})** | You want the usual download. Everything Holiday Lights needs is inside, so it works on any Windows 11 PC, even offline. |
| [{{FRAMEWORK_DEPENDENT_ZIP}}]({{REPOSITORY_URL}}/releases/download/{{TAG}}/{{FRAMEWORK_DEPENDENT_ZIP}}) | Your PC already has the [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0). This download is smaller. |

## Install

1. Download the zip and extract it (right-click it, then **Extract All**).
2. In the extracted folder, double-click **Setup.exe**. Holiday Lights installs for you only, in `%LOCALAPPDATA%\Programs\HolidayLights`, so no administrator rights are needed.
3. To remove it later, use **Settings > Apps > Installed apps**.

Holiday Lights is not code-signed, so the first time you run Setup.exe, Windows SmartScreen may say "Windows protected your PC". Choose **More info**, then **Run anyway**.

Holiday Lights runs on 64-bit Windows 11. It works completely offline and never collects information about you.

## Checksums (SHA-256)

```text
{{CHECKSUMS}}
```

To check a download in PowerShell, run `Get-FileHash .\{{SELF_CONTAINED_ZIP}}` and compare the hash with the line above (case does not matter). `SHA256SUMS.txt` holds the same lines for `sha256sum -c`.

---

The source code is open source under the MIT License. The bulbs, music and pictures remain the copyright of their authors (see [content/README.md]({{REPOSITORY_URL}}/blob/{{TAG}}/content/README.md)). Built by GitHub Actions from {{COMMIT}}.
