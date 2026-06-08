# TODO

## Backend protection roadmap

### 1. Reorganize source folders

- [x] Create `src/Simplimixi/Backend/`.
- [x] Create `src/Simplimixi/Frontend/Wpf/`.
- [x] Move `src/Simplimixi/Core/*` to `src/Simplimixi/Backend/Core/`.
- [x] Move `src/Simplimixi/Wpf/*` to `src/Simplimixi/Frontend/Wpf/`.
- [x] Update `CV-AUT.csproj` compile paths.
- [x] Run `dotnet build CV-AUT.csproj -c Release`.

### 2. Fix paths after restructure

- [x] Search for old paths: `src/Simplimixi/Core`, `src\\Simplimixi\\Core`.
- [x] Search for old paths: `src/Simplimixi/Wpf`, `src\\Simplimixi\\Wpf`.
- [x] Update scripts, configs, docs, or release notes that reference old paths.
- [x] Verify XAML `x:Class`, namespaces, and resource references still work.
- [x] Run `dotnet build CV-AUT.csproj -c Release`.

### 3. Split backend into a separate project

- [x] Create `src/Simplimixi/Backend/Simplimixi.Backend.csproj`.
- [x] Move backend compile items into `Simplimixi.Backend.csproj`.
- [x] Add backend package references: `OpenCvSharp4`, `OpenCvSharp4.runtime.win`, `SharpAdbClient`.
- [x] Remove backend package references from the WPF app if no longer needed there.
- [x] Add a project reference from `CV-AUT.csproj` to `Simplimixi.Backend.csproj`.
- [x] Run `dotnet build CV-AUT.csproj -c Release`.

### 4. Keep frontend project WPF-only

- [x] Keep WPF package references in `CV-AUT.csproj`, especially `WPF-UI`.
- [x] Ensure `ApplicationDefinition` points to `src/Simplimixi/Frontend/Wpf/App.xaml`.
- [x] Ensure WPF `Page` items only include `src/Simplimixi/Frontend/Wpf/**/*.xaml`.
- [x] Verify asset/resource copy items still publish correctly.
- [x] Run `dotnet build CV-AUT.csproj -c Release`.

### 5. Obfuscate backend separately

- [x] Update `Obfuscar.xml` to process `Simplimixi.Backend.dll`.
- [x] Keep WPF/UI obfuscation conservative to avoid breaking bindings.
- [x] Use stronger backend rules where safe: `HideStrings`, `OptimizeMethods`, `RenameFields`, `UseUnicodeNames`, `ReuseNames`.
- [x] Evaluate whether backend can safely enable `RenameProperties`.
- [x] Update `scripts/build-installer.ps1` to copy the obfuscated backend DLL into the protected package.
- [x] Run the protected release build script.

### 6. Verify protected release artifacts

- [x] Update `Test-ProtectedAssembly` to scan both `SimpliMixi.dll` and `Simplimixi.Backend.dll`.
- [x] Fail release if backend DLL exposes sensitive class names: `CVAutomationFramework`, `VisionEngine`, `ADBHelper`, `Training`, `WallUpdater`, `IsTarget`, `TemplateAssetLoader`.
- [x] Fail release if old template key strings are exposed.
- [x] Fail release if `.pdb` or `.xml` debug artifacts are present in the package.
- [x] Verify the installer is produced successfully.

### 7. Reduce backend public surface

- [x] Review backend classes and make non-entry classes `internal` where possible.
- [x] Keep only the minimal API needed by WPF public.
- [x] Introduce a small public facade if useful, e.g. `AutomationRunner`.
- [x] Hide implementation classes behind the facade.
- [x] Run `dotnet build CV-AUT.csproj -c Release`.

### 8. Introduce a backend boundary

- [x] Define a small backend-facing contract for the UI, e.g. `IAutomationRunner`.
- [x] Move direct `CVAutomationFramework` usage behind the contract.
- [x] Update `BotService` to depend on the facade/contract instead of backend internals.
- [x] Keep logging/status events explicit at the boundary.
- [x] Run `dotnet build CV-AUT.csproj -c Release`.

### 9. Identify native-candidate logic

- [x] List backend logic with the highest reverse-engineering value.
- [x] Prioritize candidates with few dependencies and stable inputs/outputs.
- [x] Evaluate candidates such as template decode, target scoring, attack strategy, wall-upgrade heuristics, or OCR scoring.
- [x] Document candidate function signatures before rewriting anything.

### 10. Optional native proof of concept

- [x] Create `src/Simplimixi/Native/` only if native hardening is still needed.
- [x] Implement one small Rust or C++ function as a proof of concept.
- [x] Call it from C# through P/Invoke.
- [x] Validate packaging and installer behavior.
- [x] Decide whether more backend logic should move native.

## Suggested commit sequence

- [x] `Reorganize frontend and backend folders`
- [x] `Split backend into separate project`
- [x] `Obfuscate backend assembly separately`
- [x] `Narrow backend public API`
- [x] `Add native module proof of concept`
