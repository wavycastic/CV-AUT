# Plan backend cho Làng đêm / Builder Base

## Mục tiêu

- Thêm backend thật cho `run_session.play_mode = "night_village"` và `targetVillage = "night_village"`.
- Không nhét luồng Làng đêm vào `OneCycle()` của Làng chính.
- Tách cycle riêng để tránh ảnh hưởng farming Làng chính, donate-only, multi-account switch, wall update sau battle.
- Dùng MBR/MyBot.run làm tài liệu tham khảo về flow, không port AutoIt nguyên xi.
- Bắt đầu bằng skeleton + switch base + log rõ, sau đó mới thêm attack.

## Kết luận hiện trạng CV-AUT

Frontend đã có Làng đêm:

- `src/frontend/Models/PlayMode.cs`
  - `NightVillageLabel = "Làng đêm"`
  - token config: `"night_village"`
- `src/frontend/ViewModels/Settings/NightVillageViewModel.cs`
  - ghi `night_village.farm_mode`
  - ghi `night_village.min_cups`
  - ghi `night_village.max_cups`
  - ghi `night_village.upgrade_wall`

Backend hiện chưa có Làng đêm thật:

- `src/backend/Core/CVAutomationFramework.cs` chỉ có `OneCycle()` cho Làng chính.
- Single-account mode đang hardcode:

```csharp
_currentTargetVillage = "main_village";
```

- `OneCycle()` đang guard:

```csharp
if (!_currentTargetVillage.Equals("main_village", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine($"[CONFIG-CS WARNING] phase=main_village_config status=skip action=skip_cycle reason=unsupported_target_village target_village={_currentTargetVillage}");
    return;
}
```

Nghĩa là chọn Làng đêm hiện tại sẽ không có backend action đúng. Nếu multi-account set `targetVillage = "night_village"`, cycle sẽ skip trong `OneCycle()`.

## Nguồn MBR đã đọc

Các file tham khảo:

- `E:\Download\MBR TEST\MyBot.run.au3`
- `E:\Download\MBR TEST\COCBot\functions\Main Screen\isOnBuilderBase.au3`
- `E:\Download\MBR TEST\COCBot\functions\Attack\BuilderBase\PrepareAttackBB.au3`
- `E:\Download\MBR TEST\COCBot\functions\Attack\BuilderBase\AttackBB.au3`
- `E:\Download\MBR TEST\COCBot\functions\Config\ImageDirectories.au3`
- `E:\Download\MBR TEST\COCBot\functions\Config\ScreenCoordinates.au3`
- `E:\Download\MBR (ATK FIX MOD 11-17-2025 UPDATE)\MBR\COCBot\functions\Village\SwitchBetweenBases.au3`

## Ý tưởng chính từ MBR

### Builder Base only mode

Trong `E:\Download\MBR TEST\MyBot.run.au3`, flow Builder Base only khoảng line 701:

```autoit
; BUILDER BASE ONLY MODE
; Flow: Confirm Main Village → Switch to Builder Base → Attack infinitely
```

Flow:

1. Confirm Main Village:

```autoit
checkMainScreen(False)
```

2. Switch sang Builder Base:

```autoit
SwitchBetweenBases(True, True)
```

3. Assert đang ở Builder Base:

```autoit
isOnBuilderBase()
```

4. Loop attack:

```autoit
DoAttackBB()
```

5. Mỗi vòng có recovery:

- `CheckAndroidReboot()`
- Nếu không còn ở Builder Base thì `SwitchBetweenBases(True, True)` lại.
- `checkObstacles()` trước attack.
- Attack xong cooldown ngắn.

### BuilderBase full cycle

Trong cùng file, hàm `BuilderBase($bTest = False)` có flow đầy đủ:

```autoit
SwitchBetweenBases(True, True)
isOnBuilderBase()
CollectBuilderBase()
BuilderBaseReport()
CleanBBYard()
StarLabGuiDisplay()
DoAttackBB()
CollectBuilderBase(False, False, False)
BuilderBaseReport(True, True)
BOBBuildingUpgrades()
StartClockTowerBoost()
StarLaboratory()
MainSuggestedUpgradeCode()
BuilderBaseReport()
SwitchBetweenBases()
_ClanGames()
```

CV-AUT không nên làm full ngay. Nên làm attack skeleton trước, rồi mở rộng collect/boost/upgrade sau.

## Switch base theo MBR

File:

```text
E:\Download\MBR (ATK FIX MOD 11-17-2025 UPDATE)\MBR\COCBot\functions\Village\SwitchBetweenBases.au3
```

Hàm:

```autoit
Func SwitchBetweenBases($bCheckMainScreen = True, $GoToBB = False)
```

Logic đáng học:

- Detect state hiện tại bằng `isOnBuilderBase(True)`.
- Nếu đang Builder Base:
  - search boat Builder Base trong region `480,40,710,250`.
  - image dir: `imgxml\Boat\BuilderBase\`.
- Nếu đang Main Village:
  - search boat Normal Village trong region `60,430,390,630`.
  - image dir: `imgxml\Boat\NormalVillage\`.
- `ZoomOut()` trước khi tìm boat.
- Click boat bằng tọa độ template match.
- Chờ tối đa khoảng 3 giây để state đổi.
- Retry 4 lần.
- Log rõ nếu không tìm thấy boat hoặc switch fail.

MBR còn có `SwitchToBuilderBase()` xử lý tunnel Builder Base 2.0:

```autoit
If QuickMIS("BC1", $sImgTunnel, 0, 190 + $g_iMidOffsetY, $g_iGAME_WIDTH, $g_iGAME_HEIGHT) Then
    SetLog("Back To Main Builder Base", $COLOR_INFO)
    Click(...)
    ZoomOut()
EndIf
```

CV-AUT nên tách tunnel handling sau Phase 1 nếu cần.

## Detect base theo MBR

File:

```text
E:\Download\MBR TEST\COCBot\functions\Main Screen\isOnBuilderBase.au3
```

Builder Base detect:

```autoit
Local $sArea = GetDiamondFromRect("445,0,500,54")
findMultiple($g_sImgIsOnBB, $sArea, $sArea, 0, 1000, 1, "objectname", $bNeedCaptureRegion)
```

Main Village detect:

```autoit
Local $sArea = GetDiamondFromRect("360,0,450,60")
findMultiple($sImgIsOnMainVillage, $sArea, $sArea, 0, 1000, 1, "objectname", $bNeedCaptureRegion)
```

Builder Base enemy village detect:

```autoit
Local $sArea = GetDiamondFromRect("745,0,815,25")
findMultiple($sImgIsOnBuilderBaseEnemyVillage, $sArea, $sArea, 0, 1000, 1, "objectname", $bNeedCaptureRegion)
```

CV-AUT nên dùng template matching tương đương:

- `builder_base_marker.png`
- `main_village_marker.png`
- `builder_enemy_marker.png`

## Attack Builder Base theo MBR

File:

```text
E:\Download\MBR TEST\COCBot\functions\Attack\BuilderBase\AttackBB.au3
```

Hàm chính:

```autoit
Func DoAttackBB()
```

Flow:

1. Check enabled.
2. `PrepareAttackBB($AttackCount)`.
3. `_AttackBB()`.
4. Lặp theo giới hạn config hoặc tối đa khoảng 10 lần.
5. Sau cycle: log `BB Attack Cycle Done`, `ZoomOut()`.

### PrepareAttackBB

File:

```text
E:\Download\MBR TEST\COCBot\functions\Attack\BuilderBase\PrepareAttackBB.au3
```

Các bước đáng học:

- Check trophy range:

```autoit
$g_iTxtBBTrophyUpperLimit
$g_iTxtBBTrophyLowerLimit
```

- Optional attack nếu còn star/loot bonus:

```autoit
CheckLootAvail()
```

- Optional halt nếu storage full:

```autoit
CheckBBGoldStorageFull()
CheckBBElixirStorageFull()
```

- Check troop slots:

```autoit
CheckForSlots()
```

- Click Attack:

```autoit
ClickAttack()
```

- Check army ready:

```autoit
CheckArmyReady()
```

- Check Battle Machine ready:

```autoit
CheckMachReady()
```

### _AttackBB

Flow chính:

```autoit
ClickFindNowButton()
WaitCloudsBB()
ZoomOut()
isOnBuilderBaseEnemyVillage(True)
GetAttackBarBB()
AttackBB($aBBAttackBar)
EndBattleBB()
```

Điểm khác Main Village:

- Không có scout resource loop kiểu `SearchNext()`.
- Không có search target theo loot như farming Main Village.
- Flow là: army ready -> Find Now -> enemy base -> deploy -> wait battle end.
- Có Builder Base 2.0 second stage: sau 100% phase 1, MBR tiếp tục deploy phase 2.

## Config mapping cho CV-AUT

Frontend đã lưu:

```json
{
  "run_session": { "play_mode": "night_village" },
  "night_village": {
    "farm_mode": "auto",
    "min_cups": 0,
    "max_cups": 5000,
    "upgrade_wall": false
  }
}
```

Đề xuất backend config record:

```csharp
private sealed record NightVillageConfig(
    string FarmMode,
    int MinCups,
    int MaxCups,
    bool UpgradeWall,
    int MaxAttacks,
    bool WaitForBattleMachine,
    bool OnlyAttackIfBonusAvailable);
```

Default tối thiểu:

```csharp
FarmMode = "auto"
MinCups = 0
MaxCups = 5000
UpgradeWall = false
MaxAttacks = 1
WaitForBattleMachine = false
OnlyAttackIfBonusAvailable = false
```

Không cần FE mới ngay cho `MaxAttacks`, `WaitForBattleMachine`, `OnlyAttackIfBonusAvailable`. Backend có thể default trước, FE thêm sau.

## Thiết kế backend đề xuất

### Dispatcher play mode

Hiện `BotLoop()` gọi thẳng `OneCycle(Config, token)`.

Nên thêm:

```csharp
private string CurrentPlayMode(JsonElement cfg)
{
    JsonElement session = GetObjectOrDefault(cfg, "run_session");
    return GetStringOrDefault(session, "play_mode", "main_village");
}
```

Dispatcher:

```csharp
private void RunConfiguredCycle(JsonElement cfg, CancellationToken token)
{
    string playMode = CurrentPlayMode(cfg);
    string targetVillage = _currentTargetVillage;

    if (targetVillage.Equals("night_village", StringComparison.OrdinalIgnoreCase) ||
        playMode.Equals("night_village", StringComparison.OrdinalIgnoreCase))
    {
        NightVillageCycle(cfg, token);
        return;
    }

    if (targetVillage.Equals("main_village", StringComparison.OrdinalIgnoreCase) ||
        playMode.Equals("main_village", StringComparison.OrdinalIgnoreCase))
    {
        OneCycle(cfg, token);
        return;
    }

    Console.WriteLine($"[CONFIG-CS WARNING] phase=play_mode status=skip action=skip_cycle reason=unsupported_play_mode play_mode={playMode} target_village={targetVillage}");
}
```

Single-account mode should set `_currentTargetVillage` from `run_session.play_mode`, not hardcode `main_village`:

```csharp
_currentTargetVillage = CurrentPlayMode(Config);
```

### NightVillageCycle skeleton

```csharp
private void NightVillageCycle(JsonElement cfg, CancellationToken token)
{
    WaitIfPaused(token);
    if (CheckStop(token)) return;

    NightVillageConfig config = GetNightVillageConfig(cfg);
    Console.WriteLine($"[NIGHT-CS] phase=cycle status=start village={_currentVillageIdx} farm_mode={config.FarmMode} min_cups={config.MinCups} max_cups={config.MaxCups} upgrade_wall={config.UpgradeWall}");

    if (!EnsureBuilderBase(token))
    {
        Console.WriteLine("[NIGHT-CS WARNING] phase=cycle status=fail action=boot_recovery reason=builder_base_not_detected");
        BootRecovery();
        return;
    }

    Console.WriteLine("[NIGHT-CS] phase=cycle status=success step=ensure_builder_base");
}
```

### EnsureBuilderBase

```csharp
private bool EnsureBuilderBase(CancellationToken token)
{
    Console.WriteLine("[NIGHT-CS] phase=builder_base_check status=start");

    if (IsOnBuilderBase(out string reason))
    {
        Console.WriteLine($"[NIGHT-CS] phase=builder_base_check status=success reason={reason}");
        return true;
    }

    if (!EnsureHomeBase(maxWaitSeconds: 20, allowBootRecovery: false))
    {
        Console.WriteLine("[NIGHT-CS WARNING] phase=builder_base_check status=retry action=boot_recovery reason=main_village_not_detected");
        BootRecovery();
        if (!EnsureHomeBase(maxWaitSeconds: 20, allowBootRecovery: false))
        {
            Console.WriteLine("[NIGHT-CS WARNING] phase=builder_base_check status=fail reason=main_village_not_detected_after_recovery");
            return false;
        }
    }

    if (!SwitchToBuilderBase(token, out string switchReason))
    {
        Console.WriteLine($"[NIGHT-CS WARNING] phase=switch_base status=fail action=skip_cycle reason={switchReason}");
        return false;
    }

    return IsOnBuilderBase(out _);
}
```

### SwitchToBuilderBase

Use template first, coordinate fallback only if template missing.

```csharp
private bool SwitchToBuilderBase(CancellationToken token, out string reason)
{
    reason = "none";
    Console.WriteLine("[NIGHT-CS] phase=switch_base status=start from=main_village to=builder_base");

    for (int attempt = 1; attempt <= 4; attempt++)
    {
        ZoomOut();
        if (InterruptibleSleep(500, token))
        {
            reason = "stopped";
            return false;
        }

        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty())
        {
            reason = "screenshot_failed";
            Console.WriteLine($"[NIGHT-CS WARNING] phase=switch_base status=retry attempt={attempt} reason={reason}");
            continue;
        }

        if (!TryMatchTemplate(screenshot, "boat_main_village.png", BuilderBoatMainRoi, 0.70, out Point boatCenter, out double score))
        {
            reason = "boat_not_found";
            Console.WriteLine($"[NIGHT-CS WARNING] phase=switch_base status=retry attempt={attempt} reason={reason}");
            continue;
        }

        Console.WriteLine($"[NIGHT-CS] phase=switch_base action=tap_boat x={boatCenter.X} y={boatCenter.Y} score={score:F3} attempt={attempt}");
        _adb.Tap(boatCenter.X, boatCenter.Y);
        if (InterruptibleSleep(2500, token))
        {
            reason = "stopped_after_tap_boat";
            return false;
        }

        if (IsOnBuilderBase(out string baseReason))
        {
            Console.WriteLine($"[NIGHT-CS] phase=switch_base status=success reason={baseReason} attempt={attempt}");
            return true;
        }

        reason = "builder_base_not_detected_after_boat_tap";
    }

    return false;
}
```

### Detect functions

```csharp
private bool IsOnBuilderBase(out string reason)
private bool IsOnMainVillage(out string reason)
private bool IsOnBuilderBaseEnemyVillage(out string reason)
```

Template names:

```text
builder_base_marker.png
main_village_marker.png
builder_enemy_marker.png
```

Roi rough mapping from MBR 860x732 to CV-AUT 1600x900 should be calibrated by screenshot. Start broad, then narrow:

```csharp
private static readonly Rect BuilderBaseMarkerRoi = Rect.FromLTRB(780, 0, 980, 90);
private static readonly Rect MainVillageMarkerRoi = Rect.FromLTRB(650, 0, 900, 90);
private static readonly Rect BuilderEnemyMarkerRoi = Rect.FromLTRB(1320, 0, 1520, 80);
private static readonly Rect BuilderBoatMainRoi = Rect.FromLTRB(80, 520, 720, 880);
private static readonly Rect BuilderBoatBaseRoi = Rect.FromLTRB(850, 50, 1350, 360);
```

These ROIs must be verified with 1600x900 screenshots before shipping.

## Phase plan

### Phase 1: BE skeleton + switch base + logs

Files likely touched:

- `src/backend/Core/CVAutomationFramework.cs`
- `assets/Templates/...` for new templates

Tasks:

1. Add `RunConfiguredCycle()` dispatcher.
2. Replace direct `OneCycle(Config, token)` calls with `RunConfiguredCycle(Config, token)` in `BotLoop()`.
3. Read single-account play mode from `run_session.play_mode`.
4. Add `NightVillageCycle()` skeleton.
5. Add `GetNightVillageConfig()`.
6. Add `EnsureBuilderBase()`.
7. Add `SwitchToBuilderBase()`.
8. Add base detect helpers.
9. Add logs.
10. Do not attack yet.

Done criteria:

- Selecting Làng đêm logs:

```text
[NIGHT-CS] phase=cycle status=start ...
[NIGHT-CS] phase=builder_base_check status=start
[NIGHT-CS] phase=switch_base status=start from=main_village to=builder_base
[NIGHT-CS] phase=switch_base action=tap_boat ...
[NIGHT-CS] phase=switch_base status=success ...
[NIGHT-CS] phase=builder_base_check status=success ...
```

- Selecting Làng chính still runs existing `OneCycle()`.
- No `SearchAttack()` for Làng đêm in Phase 1.
- No deploy/troop action in Phase 1.

### Phase 2: Attack MVP

Files likely touched:

- `src/backend/Core/CVAutomationFramework.cs`
- maybe new `src/backend/Core/NightVillageAttack.cs` only if `CVAutomationFramework.cs` becomes too large
- templates under `assets/Templates`

Tasks:

1. Add `DoNightVillageAttack()`.
2. Add `PrepareNightVillageAttack()`.
3. Add `ClickBuilderAttackButton()`.
4. Add `ClickFindNowButton()`.
5. Add `WaitForBuilderEnemyBase()`.
6. Add `DeployBuilderTroopsSimple()`.
7. Add `WaitBuilderBattleEnd()`.

MVP deploy strategy:

- Do not invent complex AI.
- Pick one side/corner.
- Tap all visible troop slots.
- Drop all troops near one edge/corner.
- Trigger machine ability opportunistically if detected.

Logs:

```text
[NIGHT-CS] phase=attack status=start
[NIGHT-CS] phase=prepare_attack status=start cups=...
[NIGHT-CS] phase=prepare_attack status=skip reason=trophy_out_of_range
[NIGHT-CS] phase=prepare_attack status=skip reason=army_not_ready
[NIGHT-CS] phase=attack step=open_attack status=success
[NIGHT-CS] phase=attack step=find_now status=success
[NIGHT-CS] phase=attack step=enemy_base status=success
[NIGHT-CS] phase=deploy status=start strategy=simple_corner
[NIGHT-CS] phase=battle_wait status=success reason=return_home_detected
[NIGHT-CS] phase=attack status=success
```

Done criteria:

- Làng đêm can start one Builder Base attack.
- If button/template not found, log exact missing step.
- If no army, log `reason=army_not_ready`, no blind tap spam.
- If enemy base not detected after Find Now, recover to Builder Base or reboot once.

### Phase 3: Collect/report/boost/upgrade

Tasks:

1. `CollectBuilderBase()` equivalent.
2. `BuilderBaseReport()` equivalent.
3. `StartClockTowerBoost()` equivalent.
4. `CleanBuilderBaseYard()` equivalent.
5. `UpgradeBuilderBaseWall()` if `upgrade_wall=true`.

This phase should only start after attack MVP is stable.

## Required templates

Minimum for Phase 1:

```text
builder_base_marker.png
main_village_marker.png
boat_main_village.png
boat_builder_base.png
```

Minimum for Phase 2:

```text
bb_attack_button.png
bb_find_now_button.png
builder_enemy_marker.png
bb_return_home_button.png
bb_attack_bonus_popup.png
bb_attack_start_marker.png
```

Optional later:

```text
bb_battle_machine_ready.png
bb_machine_ability.png
bb_bomber_ability.png
bb_army_not_ready.png
bb_train_button.png
bb_fill_camp.png
bb_gold_full.png
bb_elixir_full.png
bb_clock_tower_available.png
bb_collect_resource.png
bb_elixir_cart.png
```

## GitNexus impact checklist trước khi code

Must run before edits:

```text
gitnexus_impact({ target: "BotLoop", direction: "upstream", repo: "CV-AUT" })
gitnexus_impact({ target: "OneCycle", direction: "upstream", repo: "CV-AUT" })
gitnexus_impact({ target: "EnsureHomeBase", direction: "upstream", repo: "CV-AUT" })
gitnexus_impact({ target: "CVAutomationFramework", direction: "upstream", repo: "CV-AUT" })
```

Expected risk:

- `BotLoop`: HIGH, because all automation sessions pass through it.
- `OneCycle`: HIGH, but should not be modified much beyond dispatcher separation.
- `EnsureHomeBase`: HIGH if changed. Avoid changing it for Phase 1 unless necessary.
- New Night Village methods: LOW initially, only dispatcher calls them.

If GitNexus returns HIGH/CRITICAL, report before editing.

## Build/test checklist

After code:

```powershell
dotnet build E:\Projects\CV-AUT\src\backend\Simplimixi.Backend.csproj
dotnet build E:\Projects\CV-AUT\src\frontend\Simplimixi.csproj
dotnet test E:\Projects\CV-AUT\tests\Simplimixi.Frontend.Tests\Simplimixi.Frontend.Tests.csproj
```

Before commit:

```text
gitnexus_detect_changes({ repo: "CV-AUT", scope: "unstaged" })
```

## Manual test checklist Phase 1

Setup:

```json
{
  "run_session": { "play_mode": "night_village" },
  "night_village": {
    "farm_mode": "auto",
    "min_cups": 0,
    "max_cups": 5000,
    "upgrade_wall": false
  }
}
```

Start from Main Village home screen.

Expected logs:

```text
[NIGHT-CS] phase=cycle status=start
[NIGHT-CS] phase=builder_base_check status=start
[NIGHT-CS] phase=switch_base status=start from=main_village to=builder_base
[NIGHT-CS] phase=switch_base action=tap_boat ...
[NIGHT-CS] phase=switch_base status=success ...
[NIGHT-CS] phase=builder_base_check status=success ...
[NIGHT-CS] phase=cycle status=success step=ensure_builder_base
```

Failure logs should include one of:

```text
reason=screenshot_failed
reason=boat_not_found
reason=builder_base_not_detected_after_boat_tap
reason=main_village_not_detected
reason=main_village_not_detected_after_recovery
reason=builder_base_not_detected
```

## Manual test checklist Phase 2

Start from Builder Base home screen or Main Village home screen.

Expected logs:

```text
[NIGHT-CS] phase=attack status=start
[NIGHT-CS] phase=prepare_attack status=start
[NIGHT-CS] phase=attack step=open_attack status=success
[NIGHT-CS] phase=attack step=find_now status=success
[NIGHT-CS] phase=attack step=enemy_base status=success
[NIGHT-CS] phase=deploy status=start strategy=simple_corner
[NIGHT-CS] phase=battle_wait status=success
[NIGHT-CS] phase=attack status=success
```

Failure logs should include one of:

```text
reason=attack_button_not_found
reason=find_now_button_not_found
reason=enemy_base_timeout
reason=army_not_ready
reason=machine_not_ready
reason=return_home_not_found
reason=bonus_popup_detected
```

## Things to avoid

- Do not reuse Main Village `SearchAttack()` for Builder Base.
- Do not call `_attacks.Run()` for Builder Base until attack-bar layout is understood.
- Do not add FE settings until BE skeleton works.
- Do not add collect/upgrade/clock-tower before base switching is stable.
- Do not hardcode blind tap only; each blind tap needs at least follow-up state verification and log reason.
- Do not reboot immediately on first switch/attack failure; retry once or twice with clear logs.

## Recommended first PR/diff

Smallest useful diff:

1. `RunConfiguredCycle()` dispatcher.
2. Single-account `_currentTargetVillage = CurrentPlayMode(Config)`.
3. `NightVillageConfig` record + parser.
4. `NightVillageCycle()` skeleton.
5. `EnsureBuilderBase()` and `SwitchToBuilderBase()` using templates.
6. Logs only; no attack.

This gives a safe manual test: UI chọn Làng đêm should switch to Builder Base and log exact fail/success reason.
