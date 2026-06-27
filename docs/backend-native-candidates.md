# Backend Native Candidate Logic

This document records backend logic that has high reverse-engineering value and could be moved behind a native boundary later. It is intentionally analysis-only; no native rewrite has been started.

## Prioritization

| Priority | Candidate | Current Location | Value | Dependency Risk |
| --- | --- | --- | --- | --- |
| P1 | Template asset decode/key derivation | `src/backend/Core/TemplateAssetLoader.cs` | Protects encrypted template assets and decode key material | Low: byte-array in/out, no UI/ADB dependency |
| P2 | OCR digit scoring | `src/backend/Core/VisionEngine.cs` | Protects digit templates, thresholds, and IoU scoring used for loot/resource reads | Medium: currently tied to OpenCV `Mat`, but can be flattened to bytes |
| P3 | Target acceptance scoring | `src/backend/Core/CVAutomationFramework.cs` and `src/backend/Core/IsTarget.cs` | Protects farming decision thresholds and acceptance behavior | Low: numeric inputs/outputs |
| P4 | Attack coordinate strategy generation | `src/backend/Core/Attacks.cs` | Protects deployment patterns, jitter, side mirroring, and ordering | Medium: many points and config variants, but pure after tab detection |
| P5 | Wall upgrade decision heuristics | `src/backend/Core/WallUpdater.cs` | Protects resource choice, candidate ordering, saved offset behavior | Medium: mixed with ADB flow; decision subset can be isolated |

## Candidate Signatures

### P1: Template Asset Decode

Current sensitive logic:

- `TemplateAssetLoader.Decode(byte[] encryptedBytes)`
- `TemplateAssetLoader.CreateKey()`

Native boundary candidate:

```csharp
internal static partial class NativeTemplateCodec
{
    public static byte[] DecodeTemplate(ReadOnlySpan<byte> encryptedBytes);
}
```

Native-friendly shape:

```c
int simplimixi_decode_template(
    const unsigned char* input,
    int input_len,
    unsigned char* output,
    int output_capacity,
    int* output_len);
```

Notes:

- Best first proof-of-concept target.
- No OpenCV, ADB, JSON, WPF, or filesystem dependency if file read stays in C#.
- C# keeps path normalization and `Cv2.ImDecode`; native only decodes bytes.

### P2: OCR Digit Scoring

Current sensitive logic:

- `VisionEngine.InitializeDigitTemplates()`
- `VisionEngine.TryExtractNumericalMetrics(...)`
- IoU scoring loop over 12x16 binary digit masks.

Native boundary candidate after C# preprocessing:

```csharp
internal readonly record struct OcrDigitCandidate(byte[] BinaryMask12x16, int Width, int Height);

internal static partial class NativeOcrScorer
{
    public static OcrScoreResult ScoreDigits(ReadOnlySpan<OcrDigitCandidate> candidates, bool includeOfflineTemplate);
}
```

Native-friendly shape:

```c
int simplimixi_score_digits(
    const unsigned char* masks,
    int candidate_count,
    int include_offline_template,
    int* digits,
    double* scores,
    int output_capacity);
```

Notes:

- Keep OpenCV crop/threshold/contour extraction in C# initially.
- Move static digit templates and IoU scoring native.
- Stable input is a contiguous list of 12x16 masks.

### P3: Target Acceptance Scoring

Current sensitive logic:

- Resource extraction call: `IsTarget.ExtractResources(...)`
- Acceptance check in `CVAutomationFramework.OneCycle(...)` after extracting `Gold`, `Elixir`, `DarkElixir`.

Native boundary candidate:

```csharp
internal readonly record struct LootSnapshot(int Gold, int Elixir, int DarkElixir);
internal readonly record struct LootThresholds(int Gold, int Elixir, int DarkElixir);

internal static partial class NativeTargetScorer
{
    public static bool ShouldAttack(LootSnapshot loot, LootThresholds thresholds);
}
```

Native-friendly shape:

```c
int simplimixi_should_attack(
    int gold,
    int elixir,
    int dark_elixir,
    int gold_threshold,
    int elixir_threshold,
    int dark_elixir_threshold);
```

Notes:

- Very low integration risk.
- Security value is moderate unless scoring becomes more nuanced than simple threshold comparison.
- Good candidate if future target logic adds weighted scoring or anti-pattern filters.

### P4: Attack Strategy Coordinates

Current sensitive logic:

- Static point patterns in `Attacks.cs`.
- `InitializePatterns()` side selection/mirroring.
- `JitterCoord(Point pt)` randomization.
- Deployment ordering in `Run(...)`.

Native boundary candidate:

```csharp
internal readonly record struct AttackPlanRequest(string Strategy, string Side, int ScreenWidth, int RandomSeed);
internal readonly record struct AttackCommand(string TroopKey, int X, int Y, int DelayMs);

internal static partial class NativeAttackPlanner
{
    public static IReadOnlyList<AttackCommand> BuildPlan(AttackPlanRequest request);
}
```

Native-friendly shape:

```c
int simplimixi_build_attack_plan(
    int strategy_id,
    int side_id,
    int screen_width,
    int random_seed,
    SimplimixiAttackCommand* commands,
    int command_capacity,
    int* command_count);
```

Notes:

- Keep tab detection, screenshots, cancellation, and ADB taps in C#.
- Native returns a command plan; C# executes it.
- Higher refactor cost because current deployment is interleaved with detection and sleeps.

### P5: Wall Upgrade Heuristics

Current sensitive logic:

- `WallUpdater.HandleHomeResources(...)` resource readiness.
- Candidate ordering and saved offset behavior in `UpgradeWall(...)`.
- Upgrade resource point choice in `GetUpgradePoint(...)`.

Native boundary candidate for pure decision subset:

```csharp
internal readonly record struct WallUpgradeRequest(
    int WallLevel,
    int Gold,
    int Elixir,
    int GoldThreshold,
    int ElixirThreshold,
    int CandidateCount,
    int? SavedOffset);

internal readonly record struct WallUpgradeDecision(
    bool UseGold,
    bool UseElixir,
    int CandidateIndexFromEnd);

internal static partial class NativeWallHeuristics
{
    public static WallUpgradeDecision Decide(WallUpgradeRequest request);
}
```

Native-friendly shape:

```c
int simplimixi_decide_wall_upgrade(
    int wall_level,
    int gold,
    int elixir,
    int gold_threshold,
    int elixir_threshold,
    int candidate_count,
    int saved_offset,
    int has_saved_offset,
    SimplimixiWallDecision* decision);
```

Notes:

- Keep template matching, taps, and validation in C#.
- Native only decides resource order and candidate index.
- Security value is lower than template decode or attack plan unless wall logic becomes more advanced.

## Recommended Native POC

Start with P1 template decode because it has the best risk/reward profile:

1. Keep `TemplateAssetLoader.Load(...)` and path handling in C#.
2. Replace only `Decode(...)`/`CreateKey()` with a native call.
3. Add managed fallback behind a debug-only or build-time switch if needed for development.
4. Validate protected publish still loads encrypted `.dat` templates and installer includes the native library.

## Non-Candidates For Now

- ADB command execution: high platform/process dependency and low algorithm secrecy.
- WPF UI state/view models: reflection/XAML-sensitive and not high-value for reverse engineering.
- Full `CVAutomationFramework.OneCycle(...)`: too broad and stateful; isolate smaller pure decisions first.
