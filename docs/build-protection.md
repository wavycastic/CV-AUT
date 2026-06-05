# Build Protection Strategy

This document captures the recommended approach for making the released SimpliMixi build harder to decompile or modify without adding license checks.

## Goal

The goal is not perfect protection. A local WPF/.NET application must still be executable on the user's machine, so a determined attacker can always inspect or patch it eventually. The practical target is:

- Make decompiled code difficult to read.
- Avoid exposing source names, debug symbols, and obvious strings.
- Keep runtime performance stable.
- Avoid breaking WPF bindings and asset loading.

## Recommended Stack

### 1. Obfuscation

Use an obfuscator as the primary protection layer.

Recommended starting point:

- `Obfuscar` for a free, low-risk first implementation.

Stronger commercial options if needed later:

- `.NET Reactor`
- `Babel Obfuscator`
- `Eazfuscator.NET`

Recommended policy:

- Obfuscate the `CvAut` core namespace strongly.
- Keep WPF views and binding-facing ViewModel members safe from rename.
- Do not blindly rename public properties used by XAML bindings.

High-value core targets:

- `CVAutomationFramework`
- `ADBHelper`
- `EmulatorBootstrapper`
- `Attacks`
- `Training`
- `VisionEngine`
- `WallUpdater`
- target detection and state-machine logic

WPF-sensitive targets to handle carefully:

- `MainViewModel`
- `ArmyViewModel`
- `GeneralViewModel`
- command properties such as `StartCommand`, `StopCommand`
- binding properties such as `StatusPillText`, `ArmyVM`, `SelectedAttackStrategy`

## Protection Options

### Rename Obfuscation

Priority: high

Performance impact: negligible

Use this broadly, especially for private/internal types and members in core logic. This gives the best protection-to-performance ratio.

### String Encryption

Priority: medium/high

Performance impact: low if selective

Use for strings that reveal implementation details, such as:

- ADB commands
- package names
- internal state names
- sensitive paths
- template keys
- automation messages that reveal flow

Avoid encrypting every string unless testing confirms startup and logging remain acceptable.

### Control-Flow Obfuscation

Priority: selective

Performance impact: possible

Use only on non-hot-path logic or business logic that is valuable to protect. Avoid heavy control-flow obfuscation around tight OpenCV loops, image processing, or frequently called ADB polling paths.

Suggested use:

- Light or medium control-flow on state-machine and attack decision code.
- Avoid or minimize it in `VisionEngine` hot paths.

### Anti-Tamper / Anti-Debug

Priority: optional

Performance impact: usually low, but compatibility risk exists

Only add after the basic obfuscation pipeline is stable. These features can trigger false positives in antivirus tools or make debugging production issues harder.

## Release Build Hygiene

Release packages should not include:

- `.pdb` files
- source files
- unnecessary XML docs
- local diagnostics or logs
- unused test assets
- secrets or private config

Recommended publish direction:

```sh
dotnet publish CV-AUT.csproj -c Release -r win-x64 --self-contained true
```

Optional after testing:

```xml
<PublishSingleFile>true</PublishSingleFile>
```

Single-file publish can make distribution cleaner, but this app uses external assets like `Templates` and `adb`, so test carefully before relying on it.

## Suggested Pipeline

```text
1. dotnet publish Release win-x64
2. Run obfuscator on managed assemblies
3. Copy required external assets: Templates, adb, images, configs
4. Remove PDB/source/debug artifacts
5. Smoke test app startup
6. Smoke test Start flow: BlueStacks, ADB, Clash foreground check
7. Smoke test General tab bindings and bottom dock controls
```

## Recommended First Implementation

Start with a conservative `Obfuscar` setup:

```text
Core namespace:
- Rename: enabled
- String encryption: selective if available/configured
- Control-flow: disabled or minimal

WPF layer:
- Preserve XAML-bound public properties
- Preserve commands
- Preserve view classes and generated partial classes
```

Then test:

- App launches.
- Navigation works.
- `Start` and `End` buttons bind correctly.
- Strategy dropdown binds correctly.
- Army cards render correctly.
- BlueStacks and Clash startup checks still work.

## Key Risk

The biggest risk is WPF binding breakage. XAML bindings refer to property names by string, so renaming those properties can silently break UI behavior. Always exclude binding-facing members unless the obfuscator has WPF-aware rules that are verified in this project.

## Practical Recommendation

Use this combination first:

```text
Rename obfuscation: strong on core
String encryption: selective
Control-flow obfuscation: light and selective
PDB/source removal: always
Single-file publish: optional after testing
License checks: not used
```

This gives a strong difficulty increase with minimal runtime impact and lower risk of breaking the WPF UI.
