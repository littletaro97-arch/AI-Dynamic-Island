# Program order and tray icon design QA

- Source visual truth: `E:\课外项目\AI Dynamic Island\设置-程序顺序重设计-原型.html` and `C:\Users\LITTLE~1\AppData\Local\Temp\codex-clipboard-211a5e51-875e-4415-afe3-0b6e06d01dde.png`
- Implementation screenshot: unavailable for the native WPF settings window; the current Computer Use surface returned no native applications/windows.
- Tray implementation preview: `C:\Users\LittleTaro\AppData\Local\Temp\ai-dynamic-island-tray-preview.png`
- Viewport: intended native WPF settings window at 920 x 720 DIPs; no valid captured implementation viewport was available.
- Source pixels: tray reference 322 x 120; provider-order source is responsive HTML rather than a fixed screenshot.
- Implementation pixels: tray preview 64 x 64; provider-order implementation capture unavailable.
- Density normalization: tray comparison was reviewed at native pixels; provider-order normalization could not be completed without a native window capture.
- State: settings Behavior card, light and dark themes, default three-provider order.

## Findings

- [P2] Native provider-order visual comparison is blocked.
  - Location: Settings > Behavior > Program order.
  - Evidence: the WPF process is running and responding, but the available Computer Use inventory exposed no native app window, so the coded row, focus, drag, insertion, disabled-button, and dark-theme states could not be captured beside the HTML source.
  - Impact: compilation and code-path checks do not prove final spacing, contrast, or interaction feedback.
  - Fix: perform manual visual acceptance in the currently running Debug app, or repeat capture when native window discovery is available.

## Required fidelity surfaces

- Fonts and typography: implemented with the existing settings typography and weights; visual comparison blocked.
- Spacing and layout rhythm: row height, icon tile, padding, and gaps follow the existing input and switch-card proportions; visual comparison blocked.
- Colors and visual tokens: the rows reuse `SettingsAccent`, `SettingsInputBackground`, and `SettingsInputBorder`; light/dark rendered comparison blocked.
- Image and icon fidelity: the tray preview visibly matches the dark rounded-square, green/purple/yellow-dot logo. Provider icons reuse existing WPF Geometry resources; no new image dependency was introduced.
- Copy and content: the short explanation states that order affects collapsed quota, status dots, expanded rows, and multi-task summaries.

## Interaction checks

- XAML and code-behind compile with 0 warnings and 0 errors using `UseAppHost=false`.
- Legacy orders and 4-/8-key orders were exercised through `ProviderCatalog.NormalizeOrder`; valid order strings remained comma-separated and were not reset.
- Unknown keys are preserved in storage, rendered with their raw key in settings, and ignored safely by the current three-provider island rendering.
- Codex limits succeeded in the running build with no error (latest sample: 5-hour 42%, weekly 91%, 1676 ms read).
- Drag/drop, Alt+Up/Alt+Down, and insertion-line behavior remain pending manual UI acceptance because native UI input/capture was unavailable.

## Comparison history

- Pass 1: tray preview matched the supplied logo reference with no P0/P1/P2 finding.
- Pass 1: provider-order comparison blocked before a valid same-state implementation screenshot could be captured.

## Implementation checklist

- [x] Whole-row native drag model and midpoint insertion calculation
- [x] Dragging opacity, dashed outline, and insertion indicator
- [x] Up/down controls with stable disabled states
- [x] Alt+Up/Alt+Down keyboard path
- [x] Immediate comma-string persistence
- [x] Unknown-provider safe degradation and 8-item normalization
- [x] Tray logo replacement
- [ ] Native light/dark visual and drag interaction acceptance

final result: blocked
