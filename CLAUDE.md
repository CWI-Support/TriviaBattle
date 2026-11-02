# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Trivia Battle is a 4-station venue trivia room (it started from the DigiStroke mini-golf app, which lives in its own
separate repo). `README.md` and `docs/ARCHITECTURE.md` are the source of truth
for structure, terminology (Station, Match, Answer owner, Question stream, Snapshot) and how to extend things. The
README's status table shows which of the 6 implementation phases are done.

The room is moving to Dungeon Quiz Quest (`docs/DUNGEON-QUIZ-QUEST.md`): Dungeon format is the default
(`Core/Dungeon`, `config/dungeon.json`, `js/dungeon-view.js`, `css/dungeon.css`, `themes/dungeon`). Keep the
Classic format working until it's formally dropped. `MatchSettings.Format` picks the game (default in `appsettings.json`,
changeable on the Operator screen). In Dungeon format `MatchEngine` hands every answer and tick to a `DungeonRun`,
which holds all combat rules: each party (a team, or a solo player) shares one `Delve` (floor, monster), and each answer
owner is an `Adventurer` (own HP, streak, hourglass, the one power-up they hold). New dungeon rules go in `DungeonRun`, not `MatchEngine`.
Power-ups (`PowerUp`) are found by kills and streaks and used with the action button (`js/dungeon-powerups.js` draws them).
Games start at the stations, arcade style: `Core/Arcade/Lobby.cs` (press any button or swipe, pick one of five heroes,
have the hero photo taken, pick solo / party / race / team battle or join the one forming, pick a category) builds the `MatchSetup`; `ButtonRouter` sends presses from stations not in
a game to `LobbyHost`; `js/arcade-lobby.js` draws it. The kiosk can still start games. Heroes: `Core/Arcade/Heroes.cs`,
their models in `HEROES` (`js/battle3d/models.js`), their pictures in `media/heroes` (made by `/dev/heroes`).
In group games players see each other's faces: `HeroPhoto.portrait` everywhere a player shows, `js/dungeon-feed.js`
for the others' moments, and rivals' faces on the monsters (`Actor.wearMask`).
Hero photos: the station puts the player's face on their hero right after they pick it in the lobby (the Photo step;
`PhotoStore` keeps it by station until their game starts, then under the match), or in a kiosk game's intro (by
default; the action button says no, or with `PhotoByDefault` off it says yes). `js/hero-camera.js` takes it,
`js/face-cutout.js` cuts out the face (MediaPipe, vendored in `lib/mediapipe`), `js/battle3d/face.js` paints it on the
knight's head, `js/battle3d/portrait.js` renders the 2D portrait, `js/hero-photo.js` fetches them, and `PhotoStore` +
`/api/photos` hold them. Photos live in server memory until the player drops it, leaves the lobby or the match ends: never write one
to disk, the database or a log.
After each answer a battle beat holds the next question back while the station plays the fight full screen
(`js/dungeon-battle.js`, `css/dungeon-battle.css`, preview at `/dev/battle`). Beat animations are timed from the
beat's server start (`--t0`) so redraws don't restart them; keep their offsets in step with the beat lengths.
Stations draw the fight in 3D (`js/battle3d/`, three.js; `?view=2d` or no WebGL 2 falls back to the CSS version; preview at
`/dev/battle3d`): it shares the HTML words and bars, and `js/battle3d/beats.js` follows the same timing table.
Combat numbers are `Game:Dungeon` (`DungeonSettings`);
floors and monsters are `config/dungeon.json`, loaded by `Server/Game/DungeonContent.cs` at startup.

Three projects: `TriviaBattle.Core` (game rules), `TriviaBattle.Hardware` (keyboard and PLC input, PLC/console
outputs, the audio engine), `TriviaBattle.Server` (ASP.NET host, EF Core, SignalR, admin, every screen in `wwwroot`).
`tools/PlcSimulator` fakes the PLC; tests are in `tests/TriviaBattle.Tests` (one folder per area).

## Ground rules

- **Code must stay readable by ordinary developers.** Prefer plain, explicit code over clever abstractions,
  one clear job per file, comments that explain *why*, and small commits. Update README/ARCHITECTURE
  in the same change when structure or behaviour changes.
- All game logic lives in `src/TriviaBattle.Core` (no ASP.NET/EF/hardware references there).
  Browsers never contain game rules. They render snapshots and send intents.
- Only the input layer (`IButtonInput` implementations, `ButtonRouter`) knows raw button ids (10-25 answers,
  26-29 each podium's action button). Game code works with `{stationId, answerIndex}` (4 = `ButtonPress.Action`).
- Never hardcode 4 stations, a single room, or "stations 1+2 vs 3+4". Ask `StationLayout`; teams come from `MatchSetup`.

## Commands

```powershell
dotnet build                                                  # from repo root
dotnet test                                                   # all tests
dotnet test --filter "FullyQualifiedName~GameModeTests"       # one class
dotnet test --filter "FullyQualifiedName~Group_first_teammate" # one test
dotnet run --project src/TriviaBattle.Server                  # http://0.0.0.0:5000, launcher page at /
dotnet tool restore; dotnet ef migrations add <Name> --project src/TriviaBattle.Server --output-dir Data/Migrations
```

For manual testing, shorten a match with environment variables (any config key works this way), e.g.
`$env:Game__IntroSeconds=1; $env:Game__MatchSeconds=20; $env:Port=5055` (game rules saved on the admin page override these). Dev helpers
(`/dev/buttons`, `POST /api/dev/buttons/{id}`, `POST /api/dev/continue/{stationId}` (a card swipe; `Space` on a station screen), hub `PressKey`) are on only when `DevTools:Enabled` is true,
which defaults to the Development environment. `src/TriviaBattle.Server/TriviaBattle.http` has sample requests.

## How a button press flows

`IButtonInput` (keyboard or PLC) raises a `ButtonEvent{buttonId, sequence, pressedAt}`
→ `ButtonRouter` translates it with `ButtonMap` (`config/buttons.json`) into a `ButtonPress{stationId, answerIndex}`
→ a station not in a game: `LobbyHost` (the arcade lobby); otherwise
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

Audio: ALL room sound is played by the server on the room's StarTech 7.1 card, never by the screen PCs. The engine
(`src/TriviaBattle.Hardware/Audio`: `AudioService`, `AudioChannel`) is RangerShack's, ported as-is: keep it in step with RangerShack
instead of reworking it. `RoomSoundService` (an `IMatchBroadcaster`) maps match events to sounds on zones; outputs are
0-based (`config/stations.json`). Videos are silent; their sound is a separate file (`Audio:IntroSound`).
Dungeon games play fight sounds (`Audio:Dungeon`, placeholders in `media/audio/dungeon/sfx`) instead of stingers,
timed to land with the battle beat's picture (constants in `RoomSoundService`; keep them in step with the beat table).

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
- Every visibility rule lives in `SnapshotBuilder`: a player sees only its own (or team's) current question, never its
  correct answer until answered (exception: downed in a Dungeon party, it also sees a teammate's question, to shout the
  answer); main screen sees right/wrong and scores only. `ScreenEvent`s must never carry answer indexes.
- At the kiosk, starting a match and registering RFID cards are REST calls (`POST /api/matches`, `/api/players`),
  not hub calls, so the kiosk gets validation problems back as a 400. A match takes `categoryIds` (one or more,
  mixed). At the stations the lobby starts them (`LobbyHost`); a station's card reader calls `POST /api/stations/{id}/swipe`.
- Several matches can run in one room at once (one per set of stations). Room-level outputs (main screen, kiosk,
  room speaker, room lights) must consider every running match: `IMatchBroadcaster.MatchChanged` gets
  `runningMatches`, and room screens get `ScreenState.Matches`. Never assume one match per room.
- `MatchSettings` is read once per match at start, so config edits never change a running match.
- Seed questions (`Data/Seed/*.json`, one file per category) load only into an empty database. Delete
  `src/TriviaBattle.Server/triviabattle.db` to reseed. Migrations apply automatically at startup.
- Screens are served from routes like `/display/player/2`, so asset and script paths in `wwwroot` must be absolute
  (`/js/ui.js`). Third-party scripts are vendored under `wwwroot/lib` (the venue LAN may be offline).
  Always pass user-typed text through `UI.escape()`.
- Each screen script follows the same pattern: `new ScreenConnection({ role, onState: render })` + one `render(state)`
  switching on `state.match.phase`. Countdowns come from `data-ends-at` attributes (server-clock ms) via `UI.startCountdowns`.

## Key places

- `Core/Matches/MatchEngine.cs`: phase state machine (Intro → Playing → Results → Finished; solo Dungeon games can go
  Playing → Continue → Playing on a swipe). Every match is a timed round;
  each answer owner has a `QuestionStream` (answer → next question at once). Wrong answers: lockout or lose points.
- `Core/Modes`: strategy classes for game modes (selected in `GameModes.Create`).
- `Server/Game/GameRulesService.cs`: round length, wrong-answer penalty, format and the solo dungeon clock set on the Operator screen, `/operator` = admin's first tab (AppSettings table), laid over `"Game"`.
- `Server/Game/MatchHost.cs`, `Server/Hubs/` (`GameHub` at `/gamehub`, `ScreenState`, groups per room/station).
- `Server/config/*.json`: stations, button map, keyboard map. `appsettings.json` `"Game"`: timings, points, penalty defaults.
