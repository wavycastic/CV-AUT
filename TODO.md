# TODO

## Backend hardening follow-up

### 1. Remove backend friend assembly access

- [ ] Search for backend `internal` APIs still used by the `SimpliMixi` frontend assembly.
- [ ] Move any remaining frontend calls behind public backend facade methods.
- [ ] Keep `IAutomationRunner` / `AutomationRunner` as the only UI-facing backend API unless a new contract is clearly needed.
- [ ] Delete `InternalsVisibleTo("SimpliMixi")` from the backend assembly.
- [ ] Run `dotnet build CV-AUT.csproj -c Release`.

### 2. Tighten the backend facade

- [ ] Review public members in `Simplimixi.Backend.dll`.
- [ ] Make public types internal unless they are required by the frontend boundary.
- [ ] Keep status/logging events explicit and minimal at the boundary.
- [ ] Verify the frontend does not reference backend implementation classes directly.
- [ ] Run `dotnet build CV-AUT.csproj -c Release`.

### 3. Build protected release 0.6.2

- [ ] Build the native DLL with `scripts/build-native.ps1`.
- [ ] Run `scripts/build-installer.ps1` for version `0.6.2`.
- [ ] Verify `publish/SimpliMixi-v0.6.2/simplimixi_native.dll` is included.
- [ ] Verify `publish/SimpliMixi-v0.6.2-Setup.exe` is produced.
- [ ] Confirm no `.pdb` or `.xml` debug artifacts are present in the protected package.

### 4. Inspect obfuscated backend output

- [ ] Open protected `Simplimixi.Backend.dll` in ILSpy or dnSpy.
- [ ] Check whether sensitive class names are hidden: `CVAutomationFramework`, `VisionEngine`, `ADBHelper`, `Training`, `WallUpdater`, `IsTarget`, `TemplateAssetLoader`.
- [ ] Check whether template key strings and decode details are still readable.
- [ ] Check whether attack, target scoring, and wall-upgrade flows are still easy to follow.
- [ ] Document any exposed symbols or strings that need another hardening pass.

### 5. Decide whether to native-harden more logic

- [ ] Review the inspection notes from the protected backend DLL.
- [ ] Only select additional native candidates if managed obfuscation still exposes high-value logic.
- [ ] Prefer small stable functions such as target scoring, template key derivation, or wall-upgrade scoring.
- [ ] Avoid moving large workflow/state-machine logic native unless there is a clear protection benefit.
- [ ] Document the decision before implementing more native code.
