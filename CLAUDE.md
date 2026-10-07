# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Trivia Battle is a 4-station venue trivia room (it started from the DigiStroke mini-golf app, which lives in its own
separate repo). `README.md` and `docs/ARCHITECTURE.md` are the source of truth
for structure, terminology (Station, Match, Round, Answer owner, Snapshot) and how to extend things. The
README's status table shows which of the 6 implementation phases are done.

## Ground rules

- **Code must stay readable by ordinary developers.** Prefer plain, explicit code over clever abstractions,
  one clear job per file, comments that explain *why*, and small commits. Update README/ARCHITECTURE
  in the same change when structure or behaviour changes.
- All game logic lives in `src/TriviaBattle.Core` (no ASP.NET/EF/hardware references there).
  Browsers never contain game rules. They render snapshots and send intents.
- Only the input layer (`IButtonInput` implementations, `ButtonRouter`) knows raw button ids (10-25).
  Game code works with `{stationId, answerIndex}`.
- Never hardcode 4 stations, a single room, or "stations 1+2 vs 3+4". Ask `StationLayout`; teams come from `MatchSetup`.

## Commands

```powershell
dotnet build                                                  # from repo root
dotnet test                                                   # all tests
dotnet test --filter "FullyQualifiedName~GameModeTests"       # one class
dotnet test --filter "FullyQualifiedName~Group_first_teammate" # one test
cd src/TriviaBattle.Server; dotnet run                        # http://0.0.0.0:5000, launcher page at /
dotnet tool restore; dotnet ef migrations add <Name> --project src/TriviaBattle.Server --output-dir Data/Migrations
```

For manual testing, shorten a match with environment variables (any config key works this way), e.g.
`Game__IntroSeconds=1 Game__AnswerSeconds=5 Game__QuestionsPerMatch=2 Port=5055`. Dev helpers
(`/dev/buttons`, `POST /api/dev/buttons/{id}`, hub `PressKey`) are on only when `DevTools:Enabled` is true,
which defaults to the Development environment. `src/TriviaBattle.Server/TriviaBattle.http` has sample requests.

## How a button press flows

`IButtonInput` (keyboard or PLC) raises a `ButtonEvent{buttonId, sequence, pressedAt}`
→ `ButtonRouter` translates it with `ButtonMap` (`config/buttons.json`) into a `ButtonPress{stationId, answerIndex}`
→ `MatchHost.HandlePress` (one lock for all matches) → `MatchEngine.HandlePress` returns `MatchEvent`s
→ each `IMatchBroadcaster` (log, `SignalRMatchBroadcaster`) runs, still inside the lock
→ screens receive a per-viewer `State` (built by `SnapshotBuilder`) plus answer-free `Event`s.

`MatchTicker` calls `MatchHost.Tick()` at 20 Hz to advance phases. When Results starts (or on abort),
`MatchHost` builds a `MatchResult` and saves it in the background (`MatchHistory`), then pushes any
`LeaderboardPlacement` events. Leaderboards are computed from the history tables on demand (`LeaderboardService`).
Synced media (intro video) is a `MediaCue {url, startAtMs}` attached by `ScreenStateFactory`, played by `js/synced-video.js`.

Outputs: `RoomOutputCoordinator` (also an `IMatchBroadcaster`) turns match state into LED states per button id and
a `LightingCue` per room for each `IRoomOutput` (console in dev, PLC in the venue). `HardwareServices` picks the
providers from `Plc:Enabled`. PLC protocol: `docs/PLC-PROTOCOL.md`; test without hardware via `tools/PlcSimulator`.

Admin: `/admin` page (`js/admin/*`, one file per tab, all via `admin-api.js`) over `/api/admin/*` (`Server/Admin`).
`QuestionRules` validates questions for both editor and import; `QuestionTransfer` does CSV/JSON packs and also seeds the DB.

Themes: never hard-code screen text. Use `Theme.text('key', vars, seed)` (HTML-escaped) or `Theme.plain(...)`, and add new keys to
`wwwroot/themes/neon/copy.json` (the base). Styling goes through the tokens in `css/screens.css`; theme-specific rules
live in `themes/<name>/theme.css`, scoped to `[data-theme="<name>"]`. See docs/THEMES.md. `ThemeFilesTests` enforces keys.

## Non-obvious rules

- `config/*.json` files are loaded after appsettings, then environment variables and command-line args are re-added
  so they still override everything (e.g. `Plc__Enabled=true`). Keep that order if you add a config file.
- Only `PlcPollingService` touches the Modbus connection. `IRoomOutput` calls must return instantly
  (they run inside `MatchHost`'s lock), so outputs store the wanted state and a loop flushes it.

- `MatchEngine` reads no clock and has no locking. Time is always passed in, and `MatchHost` serializes all calls.
  Tests drive it with a fake time via `tests/TriviaBattle.Tests/TestMatch.cs`.
- Broadcasters run inside `MatchHost`'s lock. They must build what they need from the engine there,
  then start network sends without awaiting them (see `SignalRMatchBroadcaster.Send`).
- Every visibility rule lives in `SnapshotBuilder`: correct answer hidden until Reveal, other players' answers
  hidden until Reveal, player sees own/team answer. `ScreenEvent`s must never carry answer indexes.
- Starting a match and registering RFID cards are REST calls (`POST /api/matches`, `/api/players`), not hub
  calls, so the kiosk gets validation problems back as a 400.
- `MatchSettings` is read once per match at start, so config edits never change a running match.
- Seed questions (`Data/Seed/*.json`, one file per category) load only into an empty database. Delete
  `src/TriviaBattle.Server/triviabattle.db` to reseed. Migrations apply automatically at startup.
- Screens are served from routes like `/display/player/2`, so asset and script paths in `wwwroot` must be absolute
  (`/js/ui.js`). Third-party scripts are vendored under `wwwroot/lib` (the venue LAN may be offline).
  Always pass user-typed text through `UI.escape()`.
- Each screen script follows the same pattern: `new ScreenConnection({ role, onState: render })` + one `render(state)`
  switching on `state.match.phase`. Countdowns come from `data-ends-at` attributes (server-clock ms) via `UI.startCountdowns`.

## Key places

- `Core/Matches/MatchEngine.cs`: phase state machine (Intro → QuestionLeadIn → QuestionOpen → Reveal → Standings … → Results → Finished).
- `Core/Modes`, `Core/Modes/LockIn`, `Core/Scoring`: strategy classes for game modes, team lock-in, points (selected in `GameModes.Create` / `ScoringRules.Create`).
- `Server/Game/MatchHost.cs`, `Server/Hubs/` (`GameHub` at `/gamehub`, `ScreenState`, groups per room/station).
- `Server/config/*.json`: stations, button map, keyboard map. `appsettings.json` `"Game"`: timings, scoring rule, team lock-in rule.
