# Third-Party Notices

PaperNote Desktop includes or depends on the following third-party components. These components remain under their own licenses; the project-level MIT License does not replace them.

| Component | Version | License | Copyright / author | Project |
| --- | ---: | --- | --- | --- |
| PDFtoImage | 5.2.1 | MIT | Copyright (c) 2021-2025 David Sungaila | https://github.com/sungaila/PDFtoImage |
| SkiaSharp | 3.119.2 | MIT | Microsoft Corporation and contributors | https://github.com/mono/SkiaSharp |
| SkiaSharp.NativeAssets.Win32 | 3.119.2 | MIT plus bundled third-party notices | Microsoft Corporation and contributors | https://github.com/mono/SkiaSharp |
| bblanchon.PDFium.Win32 | 147.0.7690 | Apache-2.0 | Copyright © Benoît Blanchon 2017-2025 | https://github.com/bblanchon/pdfium-binaries |
| GSAP | 3.14.2 | GreenSock Standard “No Charge” License | GreenSock, Inc. | https://gsap.com/standard-license/ |

GSAP is included only in the reproducible promotional video project under `videos/papernote-promo/`; it is not linked into the PaperNote desktop executable.

The dependency graph can be reviewed with:

```powershell
dotnet list .\src\PaperNote.Desktop\PaperNote.Desktop.csproj package --include-transitive
```

## Included license files

- `legal/third-party/PDFtoImage-LICENSE.txt`
- `legal/third-party/SkiaSharp-LICENSE.txt`
- `legal/third-party/SkiaSharp-THIRD-PARTY-NOTICES.txt`
- `legal/third-party/Apache-2.0.txt`

Public binary releases produced by `scripts/build-release.ps1` preserve these files under `legal/third-party/` in the release package.
