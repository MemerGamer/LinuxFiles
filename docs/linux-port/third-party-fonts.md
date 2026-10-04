# Third-party fonts (Linux build)

Segoe UI, Segoe UI Variable, Segoe Fluent Icons and Segoe MDL2 Assets are Microsoft-licensed and not redistributable, so the Linux (Uno Skia) build substitutes them. The overrides live in `src/Files.App/Styles/LinuxDesktopResources.xaml`, which is merged last in `App.xaml`.

| Role | Font | License | Where |
|---|---|---|---|
| UI text (`ContentControlThemeFontFamily`) | Selawik 1.01 (Microsoft, Segoe UI metric-compatible): `selawk.ttf` (Regular), `selawkb.ttf`, `selawksb.ttf`, `selawkl.ttf` | SIL OFL 1.1 (`Selawik-OFL-LICENSE.txt`), https://github.com/microsoft/Selawik | `src/Files.App/Assets/Fonts/Linux/` |
| Icons (`SymbolThemeFontFamily`) | Uno.Fonts.Fluent 2.6.1 (`uno-fluentui-assets.ttf`, Segoe Fluent Icons code points) | Apache-2.0 (NuGet package, transitively referenced by Uno.WinUI) | NuGet, copied to `Uno.Fonts.Fluent/Fonts/` in the output |
| Monospace | none bundled; `DejaVu Sans Mono, Liberation Mono` from the system | n/a | `Files.Linux.MonospaceFontFamily` |

Selawik carries a Reserved Font Name, so the files must stay unmodified. Do not rename or subset them.

## Glyph coverage
Every PUA code point (`&#xE...;`, `\uE...`) used in `src/**/*.xaml|cs` was checked against the Uno.Fonts.Fluent cmap: 110 distinct, 107 covered.
Uncovered: `E621` (LayoutPage settings card icon), `E67A` (FoldersPage settings card icon), `F571` (FileIcon combined-items glyph). These render as a blank box.

## Known limitations
- Uno loads a font file as a single face, so weights are resolved by family name instead: `Program.Main` registers `Assets/Fonts/Linux/*.ttf` with fontconfig (`FcConfigAppFontAddFile`, `FontConfigNative`) and `ContentControlThemeFontFamily` is `Selawik, Segoe UI, Noto Sans, DejaVu Sans`, so Skia matches Bold/SemiBold/Light by weight.
- The three uncovered glyphs were replaced: E621 -> E8A9 (LayoutPage), E67A -> E8B7 (FoldersPage), F571 -> E8F1 (FileIcon).
- "Segoe MDL2 Assets" literals are already mapped to the Fluent icon font by Uno; `BladeView` and `GridSplitter` now use `SymbolThemeFontFamily` explicitly.
