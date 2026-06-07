<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **CV-AUT** (2874 symbols, 5526 relationships, 203 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

> If any GitNexus tool warns the index is stale, run `npx gitnexus analyze` in terminal first.

## Always Do

- **MUST run impact analysis before editing any symbol.** Before modifying a function, class, or method, run `gitnexus_impact({target: "symbolName", direction: "upstream"})` and report the blast radius (direct callers, affected processes, risk level) to the user.
- **MUST run `gitnexus_detect_changes()` before committing** to verify your changes only affect expected symbols and execution flows.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, use `gitnexus_query({query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `gitnexus_context({name: "symbolName"})`.

## Never Do

- NEVER edit a function, class, or method without first running `gitnexus_impact` on it.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with find-and-replace — use `gitnexus_rename` which understands the call graph.
- NEVER commit changes without running `gitnexus_detect_changes()` to check affected scope.

## Resources

| Resource | Use for |
|----------|---------|
| `gitnexus://repo/CV-AUT/context` | Codebase overview, check index freshness |
| `gitnexus://repo/CV-AUT/clusters` | All functional areas |
| `gitnexus://repo/CV-AUT/processes` | All execution flows |
| `gitnexus://repo/CV-AUT/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
|------|---------------------|
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->

# WPF UI coding rules
- This project uses the `Wpf.Ui` (lepoco/wpfui) library for all UI.
- BEFORE writing or editing any XAML/UI code, READ the relevant files in:
  - `docs/wpf-ui/docs/`     → official control documentation (markdown)
  - `docs/wpf-ui/gallery/`  → real source code of the WPF UI Gallery app (use as the canonical example)
- NEVER invent Wpf.Ui control names, properties, or namespaces. If unsure, find the exact usage in `docs/wpf-ui/gallery/` first.
- Match the Gallery's patterns: `ui:FluentWindow`, `ui:NavigationView` + `ui:NavigationViewItem`, `ui:CardControl`, `ui:CardExpander`, `ui:CardAction`, `ui:TextBlock FontTypography=...`, `ui:Button Appearance="Primary"`.
- Use theme resource keys (e.g. `ApplicationBackgroundBrush`, `TextFillColorPrimary`) instead of hardcoded hex, except documented fallbacks.
