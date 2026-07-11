---
name: cv-aut-avalonia-frontend
description: Project-specific CV-AUT skill for Avalonia frontend work. Before proposing or editing UI code, read E:\Projects\CV-AUT\docs\avalonia-12, then follow this repo's Avalonia 12, MVVM Toolkit, compiled-binding, Native AOT, and GitNexus safety rules.
disable-model-invocation: true
---

# CV-AUT Avalonia Frontend

Use this skill only for CV-AUT UI/frontend tasks in `src/frontend`.

## Mandatory docs-first rule

Before proposing, explaining, editing, or writing Avalonia frontend code, read the relevant docs under:

`E:\Projects\CV-AUT\docs\avalonia-12`

Prefer focused reads from docs sections matching the task: controls, layout, binding, compiled bindings, styles/resources, data templates, commands, MVVM, dialogs, theming, validation, animations, performance, and troubleshooting.

Do not guess Avalonia APIs. If the docs cannot be accessed or do not cover the topic, say so before coding.

## CV-AUT frontend stack

Respect the current project shape:

- Frontend project: `src/frontend/Simplimixi.csproj`
- Avalonia version: `12.0.5`
- Target: `net10.0-windows`
- UI framework packages: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`
- MVVM: `CommunityToolkit.Mvvm` `8.4.2`
- Namespace root: `CvAut`
- Views: `src/frontend/Views/*.axaml` and `*.axaml.cs`
- ViewModels: `src/frontend/ViewModels/*.cs`
- App resources/styles: `src/frontend/App.axaml`
- View resolution: `src/frontend/ViewLocator.cs`

## Hard constraints

1. Follow `CV-AUT/AGENTS.md`: run GitNexus impact analysis before editing any function, class, or method. Warn before continuing if risk is HIGH or CRITICAL.
2. Preserve Native AOT and trimming safety:
   - `PublishAot=true`
   - `TrimMode=full`
   - `AvaloniaUseCompiledBindingsByDefault=true`
   - avoid reflection-based view lookup, `Activator.CreateInstance`, runtime type-name lookup, and dynamic patterns that trimming can break.
3. Keep compiled bindings working:
   - every typed view should declare `x:DataType`
   - use properties/commands that exist on the matching ViewModel
   - avoid untyped bindings unless docs and the task justify them.
4. Keep MVVM separation:
   - UI layout and styles in `.axaml`
   - UI-only initialization in `.axaml.cs`
   - state, commands, async operations, and backend coordination in ViewModels/services
   - use `[ObservableProperty]`, `[RelayCommand]`, and `NotifyCanExecuteChangedFor` consistently with existing code.
5. Do not introduce WPF-only assumptions. Verify Avalonia syntax and control behavior against the local Avalonia 12 docs.
6. Keep code minimal and project-consistent. Prefer editing existing View/ViewModel pairs over adding new architecture unless requested.

## Workflow

1. Read the relevant Avalonia docs from `E:\Projects\CV-AUT\docs\avalonia-12`.
2. Summarize the docs points that affect the task in 2-5 bullets.
3. Inspect the current frontend files involved.
4. Run GitNexus impact analysis before editing symbols.
5. Make focused changes in `src/frontend`.
6. Validate with the narrowest useful command first, usually `dotnet build src/frontend/Simplimixi.csproj` from repo root.
7. Report changed files and validation result.

## Coding preferences

- Keep XAML readable: explicit rows/columns, simple margins, no over-nesting.
- Reuse Fluent theme resources where possible.
- Prefer `DynamicResource` for theme-aware colors/resources.
- Keep commands async when doing I/O or backend work.
- Marshal backend thread updates to the UI thread with Avalonia's dispatcher when needed.
- Update `ViewLocator.cs` explicitly if adding a new ViewModel/View pair.
- Update `Simplimixi.csproj` explicit `<Compile>` items when adding new `.cs` frontend files.

If any required step cannot be completed, state exactly what blocked it and continue only with safe, documented assumptions.