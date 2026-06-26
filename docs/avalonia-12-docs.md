# Avalonia 12 Documentation (Local Reference)

Complete Avalonia **12.0.5** documentation downloaded locally into
`docs/avalonia-12/` for offline development reference. Source: the official
[`AvaloniaUI/avalonia-docs`](https://github.com/AvaloniaUI/avalonia-docs)
repository (shallow + sparse checkout of the 12.0.5 version only).

> The `docs/avalonia-12/` folder is **gitignored** — it is local reference only
> and is NOT committed to the project (avoids bloating history with ~44 MB of
> third-party content). See [Updating](#updating) to refresh it.

## Layout

```
docs/avalonia-12/
├── docs/                              # Concept guides (current = 12.0.5)
│   ├── get-started/                   # Installation, IDE extensions, first app
│   ├── fundamentals/                  # Controls, styles, data binding basics
│   ├── xaml/                          # XAML reference, namespaces, markup extensions
│   ├── data-binding/                  # Compiled bindings, MVVM, INPC
│   ├── data-templates/                # ViewLocator, DataTemplate
│   ├── styling/                       # Styles, themes, FluentTheme
│   ├── layout/                        # Grid, StackPanel, DockPanel, etc.
│   ├── controls/  (concepts)          # Built-in control overview
│   ├── input-interaction/             # Input, focus, drag-drop, gestures
│   ├── events/                        # Routed events, tunneled/bubbled
│   ├── properties/                    # StyledProperty, DirectProperty, AvaloniaProperty
│   ├── graphics-animation/            # Drawing, transforms, animations
│   ├── custom-controls/               # Authoring custom controls
│   ├── app-development/               # App lifecycle, services, DI
│   ├── services/                      # Application services, IClassicDesktopStyleApplicationLifetime
│   ├── platform-specific-guides/      # Windows-specific notes
│   ├── deployment/                    # Publish, AOT, self-contained, trimming
│   ├── testing/                       # Unit/UI testing
│   ├── samples-tutorials/             # Sample apps & walkthroughs
│   ├── how-to/                        # Task-oriented recipes
│   ├── migration/                     # Migration guides
│   ├── avalonia12-breaking-changes.md # ⭐ v12 breaking changes — read first
│   ├── supported-platforms.mdx
│   └── welcome.md
├── controls/                          # Per-control reference (12.0.5)
│   ├── input/        (TextBox, Button, CheckBox, ComboBox, ...)
│   ├── layout/       (Grid, StackPanel, Border, Expander, ...)
│   ├── display/      (DataGrid, ItemsControl, ListBox, TreeView, ...)
│   ├── navigation/   (TabControl, Menu, ...
│   ├── feedback/     (ProgressBar, ...
│   ├── menus/        (Menu, ContextMenu, ...
│   ├── primitives/   (Popup, ScrollViewer, ...
│   ├── media/        (Image, ...
│   ├── web/          (WebView, ...
│   └── index.md
├── api_versioned_docs/version-12.0.5/ # API reference (generated, 12.0.5)
│   ├── avalonia/      avaloniaui/     compiledavaloniaxaml/
│   ├── corerpc/       global/         packages/
│   ├── tmds/          xamlx/          index.mdx
├── troubleshooting/                   # Known issues & fixes
│   ├── installation.md, app-performance-issues.md
│   ├── platform-specific-issues/, ui-development/, tools/, controls/
├── sidebars.ts, controls-sidebar.ts, troubleshooting-sidebar.ts
│   # Docusaurus navigation tables — use these as a table-of-contents
├── api-sidebars.ts                    # API reference nav (large)
├── api_versions.json                  # ["12.0.5"]
└── README.md                          # Upstream README
```

## Most relevant sections for this project

This project is an Avalonia 12.0.5 desktop app built with **Native AOT**,
**Compiled Bindings**, and **CommunityToolkit.Mvvm**. When developing the UI,
check these first:

| Topic | Path | Why it matters here |
|-------|------|---------------------|
| v12 breaking changes | `docs/avalonia12-breaking-changes.md` | Avoid errors from 11.x→12 migration |
| Compiled bindings | `docs/data-binding/` | Required for AOT (`AvaloniaUseCompiledBindingsByDefault=true`) |
| Deployment / AOT | `docs/deployment/` | Native AOT publish, trimming, self-contained |
| App lifecycle | `docs/app-development/`, `docs/services/` | `IClassicDesktopStyleApplicationLifetime` used in `Program.cs` |
| XAML + x:DataType | `docs/xaml/` | `x:DataType` compiled bindings on `MainWindow.axaml` |
| MVVM / ViewLocator | `docs/data-templates/` | Static `ViewLocator` (AOT-safe, no reflection) |
| Styling / FluentTheme | `docs/styling/` | `FluentTheme` in `App.axaml` |
| Control reference | `controls/` | Building the real UI (device picker, log panel, etc.) |
| Troubleshooting | `troubleshooting/` | Diagnose runtime/build issues |

## Updating

To refresh the docs to the latest upstream 12.0.5 content, re-run from the
project root (PowerShell or bash):

```bash
rm -rf docs/avalonia-12
git clone --depth 1 --sparse https://github.com/AvaloniaUI/avalonia-docs.git docs/avalonia-12
git -C docs/avalonia-12 sparse-checkout set docs api_versioned_docs/version-12.0.5 controls troubleshooting
rm -rf docs/avalonia-12/.git
```

The folder is gitignored, so it will not appear in `git status`.
