# GoXLR Control Studio — Refactoring Report

## 1. Initial findings

Audit of the real repository identified:

- **P0:** `GoXlrUtilityProvider.StartAsync`/`StopAsync` could leave duplicate `RunAsync` loops after sleep/resume (`AppBootstrapper.OnPowerModeChanged`).
- **P1:** Concurrent `PublishStatusAsync`, soft-takeover not reset on hardware resync, `HttpDisabled` pipe poll never re-checking HTTP, lighting colour-cache races, corrupt profiles silently skipped.
- **P2:** Missing fader coalesce, UI sync `Dispatcher.Invoke` thrash from lighting/faders, unused `System.Reactive`, sticky button debounce on bounce, Discord Fallback connection tautology, thin Engine orchestration tests.
- **Deferred:** MainViewModel split, Audio→Engine port relocation, session EventContext COM shim, Discord Hybrid/Native, live hardware validation.

Full detail: [`REFACTORING_PLAN.md`](REFACTORING_PLAN.md).

## 2. Changes actually implemented

### Wave 1 — Hardware lifecycle
- [`GoXlrUtilityProvider.cs`](src/GoXlrControl.Hardware/GoXlrUtilityProvider.cs): Start ignores if already running; Stop cancels, awaits run task (5s timeout + log), clears caches, disposes CTS.
- [`SimulatedHardwareProvider.cs`](src/GoXlrControl.Hardware/SimulatedHardwareProvider.cs): same Start/Stop re-entrancy guard.
- Tests: [`SimulatedHardwareLifecycleTests.cs`](tests/GoXlrControl.Hardware.Tests/SimulatedHardwareLifecycleTests.cs).

### Wave 2 — Reliability
- Serialize status publish via `_publishGate`.
- `PollPipeAsync` re-probes HTTP every 5s; exits to WS when enabled.
- `ControllerEngine` resets soft-takeover on `Connected` / `HttpDisabled`.
- `LightingFeedbackService` protects colour caches with `_colourCacheGate`; diagnostics publish only on change.

### Wave 3 — Engine
- Fader coalesce latest-wins ~12 ms.
- `ButtonDebouncer`: always accept release; debounce press edges only.
- Integration tests: soft-takeover, coalesce, pause, reconnect reset.

### Wave 4 — UI / config / deps
- `MainViewModel`: `Dispatcher.InvokeAsync` for high-frequency events; lighting text update only when changed.
- Discord Fallback: documented intentional `Disconnected` voice-API connection; tests renamed to Mirrored semantics.
- Removed unused `System.Reactive` from Engine and Hardware csproj.
- Corrupt profiles renamed to `.bak` before skip.

### Wave 5 — Docs
- [`docs/architecture/system-overview.md`](docs/architecture/system-overview.md): actual dependency graph.
- [`docs/architecture/event-processing.md`](docs/architecture/event-processing.md): coalesce + soft-takeover reset as implemented.

## 3. Architectural improvements

- Hardware lifecycle is single-loop-safe across resume reconnect.
- Status mutation is serialized; Engine reconnect semantics match documented soft-takeover reset.
- Lighting cache access is synchronized; UI no longer sync-blocks on every hardware/lighting tick.
- Dead Reactive dependency removed; architecture docs match event-based reality.

## 4. Removed duplication and obsolete code

- Unused `System.Reactive` package references (Engine, Hardware).
- Misleading Discord test name (`CommandSent` → `Mirrored`).
- No large dead-code deletions of public APIs (deferred per plan).

## 5. Performance changes and measurements

| Change | Verified? | Notes |
|---|---|---|
| Fader coalesce 12 ms | Behavior via Engine tests | Latency not measured on device |
| Lighting diagnostics only on change | Code review | Idle UI thrash expected lower; not profiled |
| HttpDisabled HTTP re-probe | Code review | Avoids permanent 100 ms poll after HTTP re-enabled |

No idle CPU, memory, or hardware latency baselines were measured in this pass.

## 6. Tests added or updated

| Suite | Change |
|---|---|
| Hardware.Tests | +3 Simulated lifecycle tests (11 total) |
| Engine.Tests | Debounce cases updated; +4 ControllerEngine integration tests (19 total) |
| Config.Tests | +1 corrupt → `.bak` (3 total) |
| Integrations.Tests | Renamed/assert Mirrored + Disconnected connection (15 total) |

## 7. Exact validation commands and results

```powershell
dotnet test GoXlrControl.sln -c Release --nologo
dotnet build GoXlrControl.sln -c Release --nologo
```

**Results (2026-09-25):**
- Config.Tests: 3 passed
- Hardware.Tests: 11 passed
- Integrations.Tests: 15 passed
- Engine.Tests: 19 passed
- **Total: 48 passed, 0 failed**
- Build: **0 warnings, 0 errors**

Not run: live GoXLR hardware, DiagHost against Utility, sleep/resume on real machine, WASAPI against live Windows audio.

## 8. Remaining risks and technical debt

- Pipe-poll path can still miss short button presses (C4) when HTTP disabled.
- `MainViewModel` still owns concrete Audio/Hardware patcher (P2f deferred).
- Audio project still depends on Engine for `IVolumeSink`.
- Session EventContext COM shim still missing (documented NAudio gap).
- `AppSettings.LogLevel` / `SelectedDeviceSerial` still unused.
- Lighting PeakProxy still queries process paths at 10 Hz (unchanged).
- Hardware Start/Stop race fixed in code; **not verified on sleep/resume with real Utility**.

## 9. Recommended follow-up

1. Manual sleep/resume + Utility HTTP toggle validation (hardware-validation checklist).
2. ~~Split `MainViewModel` into mapping/settings/Discord facades without UI redesign.~~ Partially done: extracted `FaderVm`, `ButtonVm`, `DiscordStatusPresentation`; removed unused `SettingsStore` ctor dependency.
3. Move `IVolumeSink` / Discord ports to a shared abstractions package.
4. ~~Wire `LogLevel` into Serilog; redact MachineName/paths on diagnostic export.~~ Done.
5. ~~Optional: PeakProxy process-path caching.~~ Done (5s TTL cache in `WindowsAudioService`).

## 10. Follow-up wave (post-plan)

Implemented after the main refactor waves:

| Change | Files |
|---|---|
| Extract fader/button VMs + Discord formatting | `FaderVm.cs`, `ButtonVm.cs`, `DiscordStatusPresentation.cs` |
| Honor `AppSettings.LogLevel` | `DiagnosticLog.SetMinimumLevel`, `AppBootstrapper` |
| Redact machine name + filesystem paths on export | `DiagnosticLog.ExportReport` / `RedactSensitive` |
| Cache process path/name for session matching | `WindowsAudioService` (5s TTL) |
| Diagnostics unit tests | `DiagnosticLogTests.cs` |
| Move `IVolumeSink` to `GoXlrControl.Abstractions` | Audio no longer references Engine |
| Dispose process handles in daemon detection | `UtilityDaemonLifecycle` |
| Synthetic button release on disconnect/stop | `GoXlrUtilityProvider`, `SimulatedHardwareProvider` |

**Hardware validation:** deferred by choice — code mitigations and automated tests cover reconnect/lifecycle; live Mini sleep/resume remains untested on device.

## Distinction: verified vs expected

| Item | Status |
|---|---|
| All automated tests green | **Verified** |
| Release build succeeds | **Verified** |
| No duplicate Start loops (sim provider) | **Verified** |
| Soft-takeover reset / coalesce / debounce | **Verified** (unit/integration) |
| Audio → Abstractionsctions (no Engine ref) | **Verified** (project graph) |
| Sleep/resume with real GoXLR | **Not verified** (user deferred) |
| UI responsiveness / idle CPU improvement | **Expected, unmeasured** |
| HttpDisabled → WS upgrade on live Utility | **Not verified** |
