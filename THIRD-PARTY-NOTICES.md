# Third-Party Notices

PaperNote includes or depends on the following third-party components. These components remain under their own licenses; the project-level MIT License does not replace them.

## Direct and bundled components

| Component | Version | Used by | License | Project |
| --- | ---: | --- | --- | --- |
| PDFtoImage | 5.2.1 | Windows | MIT | https://github.com/sungaila/PDFtoImage |
| SkiaSharp / SkiaSharp.NativeAssets.Win32 | 3.119.2 | Windows PDF rendering dependency chain | MIT plus bundled third-party notices | https://github.com/mono/SkiaSharp |
| bblanchon.PDFium.Win32 | 147.0.7690 | Windows PDF rendering dependency chain | Apache-2.0 | https://github.com/bblanchon/pdfium-binaries |
| Microsoft.Maui.Controls / Microsoft.Maui.Essentials | 10.0.0 | Android | MIT plus bundled third-party notices | https://github.com/dotnet/maui |
| Microsoft.Extensions.Logging.Debug | 10.0.0 | Android development diagnostics | MIT plus bundled third-party notices | https://github.com/dotnet/runtime |
| Microsoft Android bindings, AndroidX, Google Android, Kotlin and related libraries | NuGet-resolved versions | Android dependency chain | MIT, Apache-2.0 and other licenses listed in the bundled notices | https://github.com/dotnet/android |
| Open Sans | Bundled font files | Android UI | SIL Open Font License 1.1 | https://github.com/googlefonts/opensans |
| GSAP | 3.14.2 | Promotional video source only | GreenSock Standard “No Charge” License | https://gsap.com/standard-license/ |

GSAP is included only in the reproducible promotional video project under `videos/papernote-promo/`; it is not linked into either PaperNote application. Open Sans font files are stored under `src/PaperNote.Mobile/Resources/Fonts/`.

The exact NuGet dependency graph for the current source tree can be reviewed with:

```powershell
dotnet list .\src\PaperNote.Desktop\PaperNote.Desktop.csproj package --include-transitive
dotnet list .\src\PaperNote.Mobile\PaperNote.Mobile.csproj package --include-transitive
```

## Included license and notice files

- `legal/third-party/PDFtoImage-LICENSE.txt`
- `legal/third-party/SkiaSharp-LICENSE.txt`
- `legal/third-party/SkiaSharp-THIRD-PARTY-NOTICES.txt`
- `legal/third-party/Apache-2.0.txt`
- `legal/third-party/Microsoft-MAUI-LICENSE.txt`
- `legal/third-party/Microsoft-MAUI-THIRD-PARTY-NOTICES.txt`
- `legal/third-party/Microsoft-Extensions-THIRD-PARTY-NOTICES.txt`
- `legal/third-party/Microsoft-Android-Bindings-LICENSE.md`
- `legal/third-party/Microsoft-Android-Bindings-THIRD-PARTY-NOTICES.txt`
- `legal/third-party/OpenSans-OFL.txt`

Windows packages produced by `scripts/build-release.ps1` preserve `THIRD-PARTY-NOTICES.md` and these files under `legal/third-party/`. Android builds embed the same notice document and license directory as application assets so they remain available with the distributed APK. Public release pages should also retain this notice file next to the downloadable packages.

This inventory is maintained for release transparency and is not legal advice. When dependencies change, regenerate the package graph and refresh this document and the copied upstream notice files before publishing.
