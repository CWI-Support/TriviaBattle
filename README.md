# Trivia Battle

A 4-player trivia room for an entertainment venue. Players swipe in at a kiosk outside the room,
pick a category, mode and difficulty, then battle it out on physical answer buttons while a
main screen and four player screens show the action.

## Quick start

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
cd src/TriviaBattle.Server
dotnet run
```

The server listens on `http://0.0.0.0:5000`. On first run it creates `triviabattle.db` and loads
the starter questions from `Data/Seed/` (one file per category).

Run the tests from the repo root:

```powershell
dotnet test
```

### Demo on one PC (no hardware)

```powershell
.\tools\demo.ps1                  # starts the server and opens every screen in small windows
.\tools\demo.ps1 -ResetDatabase   # same, but rebuild the database with the latest starter questions
                                  # (also wipes match history, leaderboards and players)
```

1. **Kiosk window**: pick a mode, add players with *Simulate card swipe* or *Play as guest*, then start.
2. **Player windows**: each one is that player's keypad. Click into a player window and press
   `1` `2` `3` `4` (or `A`–`D`, or the numpad), or just click/tap an answer.
3. Close the screens with `.	ools\launch-screens.ps1 -Close`, and close the server's window to stop it.

This works the same with the screens on separate devices (e.g. four tablets as player screens):
open `http://<server-ip>:5000/display/player/1` on each and tap the answers.

Other ways to press buttons without hardware:
- `/dev/buttons`: all 16 buttons on one page.
- `/display/main?devkeys=1`: type for every player from the main screen
  (`1234` = Player 1, `QWER` = Player 2, `ASDF` = Player 3, `ZXCV` = Player 4).

These test buttons only work while dev tools are on. That's the default when you run with
`dotnet run`, and they're off in Production.

Screen URLs and what they show:

| URL | Screen |
|---|---|
| `/kiosk` | Touchscreen setup outside the room |
| `/display/main` | Main room screen (question, timer, scores; leaderboards between matches) |
| `/display/player/{n}` | Player station screen (portrait) |
| `/display/leaderboard` | Stand-alone rotating leaderboards, e.g. for a lobby TV |
| `/admin` | Operator admin: questions, categories, import/export, players, live matches and PLC health |
| `/dev/buttons` | Dev stand-in for the buttons (only when dev tools are on) |

Add `?room=B` to any URL when there's more than one room. It doesn't matter which machine a screen
runs on. Point a browser at `http://<server-ip>:5000/...`.

### Running the room screens from one PC

`tools/launch-screens.ps1` opens each screen full-screen on its own monitor (Chromium, Chrome or Edge),
with autoplay enabled so the intro video can play with sound. Edit the `$Screens` table at the
top of the script to match your monitor layout.

```powershell
.\tools\launch-screens.ps1 -Demo     # small tiled windows on one monitor, to try it out
.\tools\launch-screens.ps1           # venue: full screen on each monitor
.\tools\launch-screens.ps1 -Close    # close them all
```

### Intro video

Every room screen plays `wwwroot/media/intro.webm` in sync at the start of a match. The main
screen plays sound and player screens are muted. The current file is a **placeholder** with an
on-screen timecode, which is handy for checking sync. Regenerate it with
`tools/placeholder-intro/make-intro.html`. To use the real intro, replace the file (or change
`Media:IntroVideoUrl`) and set `Game:IntroSeconds` to at least `Media:IntroVideoDelaySeconds` plus
the video's length.

You can also drive a match with no screens at all, using the requests in
`src/TriviaBattle.Server/TriviaBattle.http`.

## How the code is organised

```
src/
  TriviaBattle.Core/      All game rules. No web, database or hardware code, so it is easy to test.
    Matches/              MatchEngine (the phase state machine), setup, players/teams, rounds, standings
    Modes/                One class per game mode (Free-for-All, 2v2 Group, 2v2 Combined)
      LockIn/             How a team's shared answer gets locked in (2v2 Group)
    Scoring/              Points rules (time-remaining, first-correct-buzz)
    Snapshots/            The state each screen receives, and who may see what
    Input/                Button events and the button-id map
    Stations/             Rooms and stations
    Questions/            The Question type and where questions come from
  TriviaBattle.Hardware/  Buttons in, LEDs/lighting out: keyboard + console (dev), PLC over Modbus TCP (venue)
  TriviaBattle.Server/    ASP.NET Core host: wiring, database, HTTP API, SignalR, screens
    config/               stations.json, buttons.json, keyboard.json  <- hand-edited config
    Data/                 EF Core database, migrations, seed questions
    Game/                 MatchHost (runs matches), MatchTicker (game clock), ButtonRouter
    Hubs/                 GameHub (/gamehub websocket) and what it sends to screens
    Leaderboards/         High-score boards, calculated from match history
    Admin/                Admin API: question editor, categories, CSV/JSON import-export, media, players
    Controllers/          HTTP API
    wwwroot/              Browser screens (plain HTML/JS/CSS, no build step)
      screens/            One HTML page per screen type
      js/                 One script per screen + shared helpers (screen-connection.js, ui.js); js/admin/ = admin page
      lib/                Third-party scripts, served locally (venue LAN may be offline)
      media/              Intro video
      themes/             One folder per theme: theme.css (looks) + copy.json (all on-screen text)
tests/TriviaBattle.Tests/ Unit tests
tools/                    launch-screens.ps1 (multi-monitor launcher), PlcSimulator, placeholder intro generator
docs/                     Architecture notes and reference images
```

Start with [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how the pieces fit together and how
to add a game mode, scoring rule, station or button mapping.

## Configuration

| What | Where |
|---|---|
| Rooms and stations (count, labels, colors) | `src/TriviaBattle.Server/config/stations.json` |
| Button id -> station + answer | `src/TriviaBattle.Server/config/buttons.json` |
| Keyboard key -> button id (dev stand-in for buttons) | `src/TriviaBattle.Server/config/keyboard.json` |
| PLC on/off, address, register map, button codes, LED registers | `src/TriviaBattle.Server/config/plc.json` (see docs/PLC-PROTOCOL.md) |
| Timer lengths, questions per match, scoring rule, team lock-in rule | `"Game"` in `appsettings.json` |
| Intro video file and start delay | `"Media"` in `appsettings.json` |
| Leaderboard size, whether guests count | `"Leaderboards"` in `appsettings.json` |
| Database file | `ConnectionStrings:TriviaBattle` in `appsettings.json` |
| Admin API key (empty = admin endpoints open, a warning is logged) | `AdminApiKey` in `appsettings.json` |
| HTTP port | `Port` in `appsettings.json` |
| Dev helpers such as `/api/dev/buttons` | `DevTools:Enabled` (defaults to on in Development only) |

Any setting can also be overridden with an environment variable, e.g. `Game__AnswerSeconds=20`.

## Themes

The screens come in themes: **neon** (default), **showcase** (70s game show) and **dungeon**
(sarcastic dungeon-AI host). Each theme is a `theme.css` for looks and a `copy.json` for every line
of on-screen text, so designers and writers can change them without touching code. Switch every
screen live from Admin → Live, or preview one with `?theme=dungeon` in its URL.
How to edit or add a theme: [docs/THEMES.md](docs/THEMES.md).

## Managing questions

Open **/admin**. If `AdminApiKey` is set in `appsettings.json`, the page asks for it once per browser tab.

- **Questions**: search, add and edit questions. Each one has a category, difficulty, four answers,
  the correct one, and an optional picture or video (uploaded files go to `wwwroot/media/questions/`).
  Untick *Active* to retire a question without deleting it.
- **Categories**: add, rename, hide from the kiosk (*Active*), or delete along with their questions.
- **Import / Export**: question packs as CSV (spreadsheet-friendly) or JSON (the same format as
  the files in `Data/Seed/`). Imports are all-or-nothing, with every problem listed by line.
  Questions that already exist are skipped, so re-importing a file is safe.
- **Players**: rename or forget RFID cards.
- **Live**: running matches (with the correct answer, for the operator), abort, and PLC health.

Changes take effect from the next match.

## Hardware (PLC)

With `Plc:Enabled` false (the default), buttons come from the keyboard stand-in and the
button LEDs / room lighting are only written to the server log. Set it to true in
`config/plc.json` to talk to the PLC over Modbus TCP. The protocol, register map and how to test
with `tools/PlcSimulator` are in [docs/PLC-PROTOCOL.md](docs/PLC-PROTOCOL.md).
`GET /api/hardware/status` shows whether the PLC is connected.

## Database changes

The schema is managed with EF Core migrations. After changing an entity in `Data/Entities`:

```powershell
dotnet tool restore
dotnet ef migrations add DescribeYourChange --project src/TriviaBattle.Server --output-dir Data/Migrations
```

The server applies pending migrations automatically on startup.

## Project status

| Phase | What | Status |
|---|---|---|
| 1 | Server core: match engine, modes, scoring, question bank | Done |
| 2 | Kiosk, main screen and player screens + keyboard input | Done |
| 3 | Synced intro video, results and leaderboards | Done |
| 4 | PLC (Modbus TCP) buttons and lighting | Done (register map is a placeholder) |
| 5 | Admin question editor | Done |
| 6 | Themes (editable looks + copy), first visual pass | Done: neon, showcase, dungeon (see docs/THEMES.md) |
