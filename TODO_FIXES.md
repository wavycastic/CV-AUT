# TODO - Verify and finish SimpliMixi fixes

## 1. Resource threshold per village

**Goal:** When the user sets 700k gold/elixir, the bot must scout using 700k for the active village, not an old/fallback value.

- [x] Update `GeneralViewModel.LoadConfig()` to load `gold_threshold`, `elixir_threshold`, and `dark_elixir_threshold` from the active village profile first.
- [x] Keep main config as fallback only when the village profile does not contain the keys.
- [x] Ensure `GeneralViewModel.SaveConfig()` writes the same threshold values to both main config and active village profile.
- [x] Add/verify startup log showing the exact active village and thresholds used by `GetFarmingThresholds()`.
- [ ] Test by setting 700000 in UI, restarting the app, starting bot, and confirming scout log shows `gold_req=700000 elixir_req=700000`.

## 2. Electro Dragon strategy consistency

**Goal:** Selecting Electro Dragon must train Electro Dragons and run `ElectroDragon_Attack`, never `Dragon_Attack`.

- [x] Verify `ArmyViewModel.SaveConfig()` persists `attack = ElectroDragon_Attack` to the active village profile.
- [x] Verify `CVAutomationFramework.GetAttackStrategy()` reads the active village profile before root config.
- [x] Verify `Training.SmartTrain()` receives `ElectroDragon_Attack` and maps it to `electro_dragon`.
- [x] Verify `Attacks.Run()` enters the `ElectroDragon_Attack` branch and deploys `e_drag` first.
- [x] Add/verify logs for selected strategy in train phase and attack phase.
- [ ] Test by selecting Electro Dragon, saving, restarting, and confirming logs show `strategy=ElectroDragon_Attack` in both training and attack.

## 3. Wall level selection

**Goal:** Wall upgrade must only support levels `8` through `17`; level `18` must be disabled/skipped instead of searched or upgraded.

- [x] Limit wall level selection in UI to `8` through `17`.
- [x] Clamp `GeneralViewModel` load/save values to the supported `8` through `17` range.
- [x] Change core wall fallback from level `12` to level `14` where defaults are intended.
- [x] Prefer root config before default when village profile exists but misses `wall_level`.
- [x] Disable wall upgrade in core when config contains unsupported levels such as `18`.
- [x] Prevent `WallUpdater.GetWallSearchTemplates()` from falling back to generic `walls/wall.png` when a specific wall level is configured but missing templates.
- [x] Add clear warning logs when selected wall level is unsupported or templates are missing.
- [ ] Test with `wall_level = 18` and confirm logs show upgrade skipped because supported range is `8-17`.

## 4. Full troop deployment in battle

**Goal:** During battle, bot must deploy the selected main troop fully before/alongside balloons, instead of only dropping balloons.

- [x] Verify deployment tab detection for `dragon` and `e_drag` uses all available fallback templates.
- [x] Review duplicate-tab filtering so `dragon`/`e_drag` are not incorrectly skipped when near another troop icon.
- [x] Add logs when required tabs are missing, even when verbose logs are disabled.
- [x] Verify `DeployTroops("dragon")` and `DeployTroops("e_drag")` report tap count and tab coordinate.
- [x] Review `EnsureTroopFullyDeployed()` behavior when OCR cannot read remaining troop count; consider fallback redeploy taps instead of immediate skip.
- [ ] Test with Dragon and Electro Dragon armies and confirm logs show main troop deployment before balloon deployment.

## 5. Return home after battle

**Goal:** After a battle ends, bot must reliably return to home village and continue the next cycle without hanging.

- [x] Verify `ReturnHome()` handles the normal Continue button path.
- [x] Verify fallback tap, Android Back key, star bonus popup, and treasure chest popup flows.
- [x] Add a final log showing whether `EnsureHomeBase()` passed or failed after return-home recovery.
- [x] Consider retrying Continue/Home action more than once before boot recovery.
- [ ] Test a normal battle end, a star bonus popup, and a treasure chest popup case.
- [ ] Confirm bot reaches `[FSM-CS] phase=home_check status=success` after battle.

## 6. Writable paths outside Program Files

**Goal:** Installed app must not write config, profiles, logs, stats, or debug images under `C:\Program Files\SimpliMixi`.

- [x] Keep main config path at `%LocalAppData%\SimpliMixi\Config\test_config.json`.
- [x] Keep village profiles at `%LocalAppData%\SimpliMixi\profiles\Village_{id}.json`.
- [x] Change `StatsFilePath()` to `%LocalAppData%\SimpliMixi\profiles\Stats_{id}.json`.
- [x] Ensure stats directory is created before `File.WriteAllText()`.
- [x] Keep debug images/logs fallback at `%LocalAppData%\SimpliMixi\logs` when app directory is not writable.
- [x] Search all source for direct writes to `AppContext.BaseDirectory`, `Directory.GetCurrentDirectory()`, relative `logs`, and relative `profiles`.
- [ ] Test from a read-only install directory and confirm no access denied errors are emitted.

## 7. Regression validation

**Goal:** Confirm all fixes are compile-safe and visible in runtime logs.

- [ ] Run `dotnet build CV-AUT.csproj --no-restore`.
- [ ] Run a UI save/reload check for General settings and Army settings.
- [ ] Run one bot cycle with wall upgrade disabled and verify threshold/attack strategy logs.
- [ ] Run one bot cycle with wall upgrade enabled and verify wall level/threshold logs.
- [ ] Collect logs from `%LocalAppData%\SimpliMixi\logs` and confirm there are no Program Files write failures.
