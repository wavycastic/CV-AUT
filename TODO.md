# TODO

## Backend hardening follow-up

### 1. Remove backend friend assembly access

- [x] Search for backend `internal` APIs still used by the `SimpliMixi` frontend assembly.
- [x] Move any remaining frontend calls behind public backend facade methods.
- [x] Keep `IAutomationRunner` / `AutomationRunner` as the only UI-facing backend API unless a new contract is clearly needed.
- [x] Delete `InternalsVisibleTo("SimpliMixi")` from the backend assembly.
- [x] Run `dotnet build CV-AUT.csproj -c Release`.

### 2. Tighten the backend facade

- [x] Review public members in `Simplimixi.Backend.dll`.
- [x] Make public types internal unless they are required by the frontend boundary.
- [x] Keep status/logging events explicit and minimal at the boundary.
- [x] Verify the frontend does not reference backend implementation classes directly.
- [x] Run `dotnet build CV-AUT.csproj -c Release`.

### 3. Build protected release 0.6.2

- [x] Build the native DLL with `scripts/build-native.ps1`.
- [x] Run `scripts/build-installer.ps1` for version `0.6.2`.
- [x] Verify `publish/SimpliMixi-v0.6.2/simplimixi_native.dll` is included.
- [x] Verify `publish/SimpliMixi-v0.6.2-Setup.exe` is produced.
- [x] Confirm no `.pdb` or `.xml` debug artifacts are present in the protected package.

### 4. Inspect obfuscated backend output

- [x] Inspect protected `Simplimixi.Backend.dll` output.
- [x] Check whether sensitive class names are hidden: `CVAutomationFramework`, `VisionEngine`, `ADBHelper`, `Training`, `WallUpdater`, `IsTarget`, `TemplateAssetLoader`.
- [x] Check whether template key strings and decode details are still readable.
- [x] Check whether attack, target scoring, and wall-upgrade flows are still easy to follow.
- [x] Document any exposed symbols or strings that need another hardening pass.

Inspection notes:
- `ilspycmd`, `dotnet-ildasm`, and `ildasm` were not available locally, so inspection used direct binary/string scanning.
- Protected `publish/SimpliMixi-v0.6.2/Simplimixi.Backend.dll` is valid and non-empty (`155648` bytes).
- Sensitive implementation class names are not present as plain UTF-8 strings in the protected backend DLL.
- The old template key string `SimpliMixi-Templates-051` is not present.
- Remaining readable strings are low-level public/dependency/facade names such as `LoadTemplatePngBytes`, `RunWorkflowTemplate`, `ImDecode`, `MatchTemplate`, `SharpAdbClient`, `templatesPath`, `attackStrategy`, and `simplimixi_decode_template`.
- No additional native-hardening candidate is required from this inspection alone; task 5 should decide whether these remaining strings are acceptable.

### 5. Decide whether to native-harden more logic

- [x] Review the inspection notes from the protected backend DLL.
- [x] Only select additional native candidates if managed obfuscation still exposes high-value logic.
- [x] Prefer small stable functions such as target scoring, template key derivation, or wall-upgrade scoring.
- [x] Avoid moving large workflow/state-machine logic native unless there is a clear protection benefit.
- [x] Document the decision before implementing more native code.

Decision:
- Do not move more backend logic native in this pass.
- The protected backend no longer exposes sensitive implementation class names or the old template key as plain strings.
- Remaining readable strings are facade/dependency names or broad operational terms, not enough to justify native migration cost.
- Keep the current native boundary focused on template decode; revisit small candidates only if manual IL inspection later shows readable high-value logic.
