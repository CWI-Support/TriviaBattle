# Architecture

## The big idea

**One server owns all game state. Every screen is a thin browser window that shows what the
server tells it and sends back what the user did.** No game rules live in the browser.

```
  physical buttons ──► PLC ──Modbus──┐
                                     ▼
  keyboard (dev) ──────────────► IButtonInput          (input layer: knows raw button ids)
                                     │ ButtonEvent {buttonId 10-25, sequence, time}
                                     ▼
                               ButtonRouter ──uses──► ButtonMap (config/buttons.json)
                                     │ ButtonPress {stationId, answerIndex, sequence, time}
                                     ▼
  kiosk ──HTTP (start match)──► MatchHost ──────────► MatchEngine (one per match)
  admin ──HTTP──────────────►   (one lock)               uses IGameMode + IScoringRule
                                     │ MatchEvents
                                     ▼
                              IMatchBroadcaster(s) ──SignalR snapshots──► main / player / kiosk screens
                                                   ──(Phase 4)──────────► button LEDs, room lights
```

Screens can all run on one PC (several Chromium kiosk windows on several outputs) or on separate
machines on the LAN. A screen knows what it is from its URL (`/display/main`, `/display/player/2`,
`/kiosk`, `/admin`), so the deployment doesn't matter to the code.

## Words used in the code

| Term | Meaning |
|---|---|
| **Room** | A physical space with stations, e.g. room `A`. |
| **Station** | One player position: a screen plus 4 buttons. Id = room + number, e.g. `A1`. |
| **Match** | One game, played by a *set of stations*. A match isn't tied to a room, so two rooms can later be linked into one 8-station match. |
| **Seat** | A player sitting at a station for a match. |
| **Round** | One question within a match. |
| **Answer owner** | Who an answer counts for: the station (individual modes) or the team (2v2 Group). |
| **Phase** | Where the match is: Intro, QuestionLeadIn, QuestionOpen, Reveal, Standings, Results, Finished. |
| **Snapshot** | The full state of a match as one particular screen is allowed to see it. |

## The match engine

`Core/Matches/MatchEngine.cs` is a plain state machine. Three things drive it:

- `Tick(now)`: called 20x/second by `MatchTicker`. It moves to the next phase when the current one's time is up.
- `HandlePress(press, now)`: a button press from one of this match's stations.
- `Abort(now)`: operator stop.

Each call returns the `MatchEvent`s that happened. The engine never reads the clock itself and
has no threads or locks, so tests can play a whole match in microseconds with a fake time.
`MatchHost` holds the only lock, so presses, ticks and admin actions never overlap.

Phase flow:

```
Intro → QuestionLeadIn → QuestionOpen → Reveal → Standings ─┐
              ▲                                             │ (more questions)
              └─────────────────────────────────────────────┘
                                         Reveal → Results → Finished   (after the last question)
```

A question closes early when every answer owner has answered (`Game:EndQuestionWhenAllAnswered`).

### Fair timing

Every press carries a `Sequence` (press order) and a `PressedAt` time from the input layer. With
the PLC, sequence comes from the PLC's own latch order. Who pressed first is therefore decided by
the hardware, not by network or polling delays. Response time (for time-remaining points) is
`PressedAt - question opened`.

## Game modes, lock-in rules and scoring rules

These are small strategy classes, so new rules don't touch the engine.

| Interface | Implementations | Chosen by |
|---|---|---|
| `IGameMode` (`Core/Modes`) | `FreeForAllMode`, `TeamSharedAnswerMode`, `TeamCombinedScoreMode` | the kiosk (per match) |
| `ILockInRule` (`Core/Modes/LockIn`) | `FirstPressLocksRule` | `Game:TeamLockIn` |
| `IScoringRule` (`Core/Scoring`) | `TimeRemainingScoring`, `FirstCorrectBuzzScoring` | `Game:Scoring:Rule` |

A game mode answers three questions:
1. Does it use teams?
2. Who owns a player's answer (`GetAnswerOwnerId`)?
3. Who gets the points for a correct answer (`AwardCorrectAnswer`)?

### Adding a game mode

1. Add a value to `GameModeKind` (`Core/Modes/IGameMode.cs`).
2. Create a class implementing `IGameMode` next to the existing ones.
3. Add it to the `switch` in `GameModes.Create`, and to `GameModes.UsesTeams` if it uses teams.
4. Add tests in `tests/TriviaBattle.Tests/Modes/GameModeTests.cs`.

### Adding a lock-in rule (e.g. "both teammates must agree")

1. Add a value to `LockInRuleKind`.
2. Implement `ILockInRule`. `TryLock` is given every station that shares the answer, so a
   unanimous rule can wait until all of them have pressed the same button. Store pending presses
   on `Round` if needed.
3. Add it to `GameModes.CreateLockInRule`.

### Adding a scoring rule

Implement `IScoringRule` (it only sees correct answers; wrong ones are always 0), add a
`ScoringRuleKind` value, and add it to `ScoringRules.Create`.

## What screens receive (the websocket contract)

On every change, the server sends each screen a **full snapshot** (`Core/Snapshots/MatchSnapshot.cs`),
not a diff. A snapshot is only a couple of KB, and a screen that reconnects or joins late is
immediately correct. Each snapshot carries `version`, and screens ignore anything older than
what they have.

Snapshots are built per viewer by `SnapshotBuilder`, which holds all the visibility rules:

- The correct answer is hidden until Reveal (except for admin).
- Before Reveal, screens see *that* someone answered, not *what* they answered.
- A player always sees the answer that counts for them. In 2v2 Group that's the team's answer, plus who pressed it.

Timers are sent as server-clock deadlines (`phaseEndsAtMs`, `closesAtMs`). Screens count down
locally after correcting for their clock offset, so the server doesn't send a message every second.

One-off `MatchEvent`s (answer locked, round revealed) are also forwarded so screens can trigger
animations. A screen that misses one still shows the right state.

### SignalR messages (`Server/Hubs`)

| Direction | Name | Payload |
|---|---|---|
| screen → server | `Join(role, roomId, stationNumber)` | returns the current `ScreenState` |
| screen → server | `Ping(clientTimeMs)` | returns server time (clock sync) |
| screen → server | `PressKey(key)` | dev only: simulated button |
| server → screen | `State` | `ScreenState { room, station, match, cue }`. `match` is the snapshot (null in the lobby); `cue` is synced media to play (or null) |
| server → screen | `Event` | `ScreenEvent { type, phase, ownerId, stationId, placement }`, never contains answers |

Each screen joins one SignalR group (`ScreenGroups`): one per room for the main screen, one per
room for the kiosk, and one per player station. `SignalRMatchBroadcaster` builds a personalised
state for each group whenever a match changes. `ScreenStateFactory` builds every `ScreenState`,
whether a screen is joining or receiving a push, so the two can't disagree.

### Synced moments (intro video)

The server never streams video. It sends a **cue**: `{ id, url, startAtMs }`, meaning "this
video's first frame is at server time `startAtMs`". Screens sync their clocks with the server
(`Ping`, keeping the fastest of several round trips), so each one knows where the video should
be at any instant. `js/synced-video.js` keeps it there:
- Before `startAtMs`, the video stays paused on its first frame.
- A screen that joins late jumps to the right spot.
- Small drift is corrected by nudging playback speed; large drift by seeking.

In testing, five screens stayed within ~4 ms of each other, including one that joined after the
video had started.

Cues come from `ScreenStateFactory.CueFor`. Right now that's the intro video, which starts
`Media:IntroVideoDelaySeconds` after the Intro phase begins so every screen has time to load it.
To add another synced moment (e.g. a results fanfare), return a cue for that phase there.

**Starting a match is plain HTTP** (`POST /api/matches`), not a hub call. The kiosk then gets
validation problems back as an ordinary 400 response. Registering an RFID card is also HTTP
(`GET /api/players/rfid/{tag}`, `POST /api/players`).

## Screens (`Server/wwwroot`)

Plain HTML, CSS and JavaScript with no build step. Every screen follows the same pattern:

```js
const connection = new ScreenConnection({ role: 'main', onState: render });
connection.start();
function render(state) { /* pick a view from state.match.phase and fill it in */ }
```

- `js/screen-connection.js` handles connecting, re-joining after reconnects, ignoring stale
  states, and clock sync (`connection.serverNow()`).
- `js/ui.js` has the shared helpers: `UI.escape()` (always use it for names), `UI.showView()`, and
  `UI.startCountdowns()`. Countdowns tick on their own from `data-ends-at` attributes, so render
  code just writes deadlines into the HTML.
- The kiosk (`js/kiosk.js`) holds only *form* state (choices not yet submitted). Once a match
  starts, it shows whatever the server says.
- RFID: `js/card-reader.js` assumes a USB keyboard-wedge reader (it types the card id quickly,
  then Enter). A different reader type only needs that file changed.

Answer colors (A red, B blue, C yellow, D green) match the physical button pads and live in
`css/screens.css`.

### Themes

Looks and wording are separate from layout. `css/screens.css` is the shared layout and only uses
CSS variables (tokens). Each theme folder (`wwwroot/themes/<name>/`) has two files:
- `theme.css` overrides the tokens and adds decorations, scoped to `[data-theme="<name>"]`.
- `copy.json` holds the screen text by key. `neon` is the base, and other themes override lines.

Screens never hard-code text. They call `Theme.text('player.correct', { points })` (`js/theme.js`),
which also HTML-escapes everything.

`ThemeService` keeps the active theme in the `AppSettings` table. It's part of every `ScreenState`,
and changing it in admin pushes a `Theme` message to all screens. `ScreenConnection` loads the
theme before the first draw and redraws when it changes. Details for designers and writers are in
[THEMES.md](THEMES.md).

## Buttons and hardware

- `config/buttons.json` maps each fixed button id (10-25) to a station and answer letter:
  `buttonId = 10 + (player - 1) * 4 + answerIndex`.
- Only `IButtonInput` implementations and `ButtonRouter` ever see raw button ids. Game logic
  only sees `{stationId, answerIndex}`.
- When the PLC register map is final, it gets mapped to these button ids in `config/plc.json`.
  Game code doesn't change.

`Server/Configuration/HardwareServices.cs` is the one place that picks the hardware:

| | Development (`Plc:Enabled` = false) | Venue (`Plc:Enabled` = true) |
|---|---|---|
| Inputs (`IButtonInput`) | `KeyboardButtonInput` | `PlcModbusButtonInput` (keyboard still works if dev tools are on) |
| Outputs (`IRoomOutput`) | `ConsoleRoomOutput` (logs) | `PlcModbusRoomOutput` |

**Outputs.** `RoomOutputCoordinator` is an `IMatchBroadcaster`, so it runs on every match change,
and turns match state into LED states (per button id) and a lighting cue per room:
- When a question opens, all four of a player's buttons light.
- Once they lock in, only their choice stays lit (for the whole team in 2v2 Group).
- At Reveal the correct button blinks; at Results the winners' buttons blink.

Outputs only *remember* the wanted state. The PLC output writes it later from its own loop, so
the game never waits on the network.

**PLC.** All PLC code is in `Hardware/Plc`. `PlcPollingService` is the single loop that owns the
Modbus connection. `PlcEventReader` is the press protocol (event counter, ring buffer, ack,
PLC-clock timestamps), and it's unit-tested against `FakePlcRegisters`. The protocol is
documented for the PLC programmer in [PLC-PROTOCOL.md](PLC-PROTOCOL.md), and `tools/PlcSimulator`
implements the PLC side.

## Database

SQLite through EF Core (`Server/Data`). Schema changes use EF migrations (see README).

| Table | Holds |
|---|---|
| `Categories`, `Questions` | The question bank. The four answers are four columns, so the data is readable in any SQLite browser. |
| `PlayerProfiles` | RFID card → player name |
| `Matches` | One row per match: mode, category, difficulty, times, aborted flag |
| `MatchTeams`, `MatchParticipants` | Final scores and ranks. A participant's score in a team mode is their contribution. |
| `MatchAnswers` | Every answer to every question (who pressed what, how fast, points) |

### Question editing and import (`Server/Admin`)

All admin endpoints live under `/api/admin/*` and use the `[AdminApiKey]` filter. The admin page
(`wwwroot/screens/admin.html`) has one script per tab in `wwwroot/js/admin/`, and all of them go
through `admin-api.js`. That script adds the key and turns server problems into readable errors.

- `QuestionRules` is the single definition of a valid question. Both the editor and import use it.
- `QuestionTransfer` reads and writes CSV and JSON packs. Import is all-or-nothing, reports problems
  by line number, and skips questions already in the category.
- The starter questions in `Data/Seed/` (one file per category) are loaded through the same importer. Each
  seed file *is* a JSON export.
- Uploaded pictures and videos get random file names under `wwwroot/media/questions/`, which is
  gitignored (it's venue data). The question stores the URL, and screens show it with
  `UI.questionMedia()`.

### When results are saved

`MatchHost` saves a match when the **Results** phase starts, because scores are final then. It
doesn't wait for the match to end, so the save finishes while Results is still on screen. An
aborted match is saved with `WasAborted = true`. Saving runs in the background after the lock is
released, so a slow disk never stalls the game.

### Leaderboards

There is no leaderboard table. `LeaderboardService` calculates the boards from match history
whenever they're asked for (`GET /api/leaderboards`). That keeps everything consistent: delete a
match and it disappears from the boards.

- There's one board per game mode, for today, the last 7 days, or all time.
- Free-for-all ranks individual scores, and a registered (RFID) player appears once, with their best.
- Team modes rank team scores, showing both teammates' names.
- Aborted matches and zero scores never count. `Leaderboards:RegisteredPlayersOnly` hides guests.

After saving, `MatchHost` checks where the match landed and pushes a `LeaderboardPlacement`
event, which the results screens show as "New high score!".
