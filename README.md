# Trivia Battle

A 4-player trivia room for an entertainment venue, played like an arcade: walk up to a station,
press any button (or swipe a card), pick one of five heroes and a game (solo, or a party or race the
other stations can join), then race the clock on the physical buttons while a main screen and the
player screens show the action. Several groups can play at once (a solo player on one station, a
trio on the other three), and the main screen splits into a panel per game. The kiosk outside the
room can still start games too.

The room is becoming **Dungeon Quiz Quest** (design: [docs/DUNGEON-QUIZ-QUEST.md](docs/DUNGEON-QUIZ-QUEST.md)):
every right answer is an attack on a monster, wrong answers and slow ones get you hit back, and you
fight your way down floor by floor. It's the default game format; the classic quiz is still one
switch away on the Operator screen.

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

1. **Player windows**: each one is a station and its keypad. Click into a player window and press
   `1` `2` `3` `4` (or `A`–`D`, or the numpad) for buttons A–D, or click/tap them on screen. `5` or
   `Enter` is the action button. Any button wakes the station; `A`/`B` flip through the heroes and
   ACTION picks one; then A, B, C... pick a game, as on screen (D goes back). In a party or race that's
   forming, ACTION is ready and D leaves. `Space` is a card swipe at that station (a pretend card per
   station): it signs you in, or takes a continue when a solo dungeon game's "Continue?" is up.
2. **Kiosk window** (optional): pick a mode, add players with *Simulate card swipe* or *Play as guest*,
   then start. It plays the knight.
3. Close the screens with `.	ools\launch-screens.ps1 -Close`, and close the server's window to stop it.

This works the same with the screens on separate devices (e.g. four tablets as player screens):
open `http://<server-ip>:5000/display/player/1` on each and tap the answers.

Other ways to press buttons without hardware:
- `/dev/buttons`: all 20 buttons on one page (4 answers and the action button per station), plus a
  "card swipe" per station to continue a solo dungeon game when its "Continue?" is up, and buttons to
  hand any power-up to that station's player.
- `/display/main?devkeys=1`: type for every player from the main screen
  (`12345` = Player 1, `QWERT` = Player 2, `ASDFG` = Player 3, `ZXCVB` = Player 4: answers A-D, then the
  action button, in a line).

These test buttons only work while dev tools are on. That's the default when you run with
`dotnet run`, and they're off in Production.

Screen URLs and what they show:

| URL | Screen |
|---|---|
| `/kiosk` | Touchscreen setup outside the room |
| `/display/main` | Main room screen: clock and live scores, split into halves/quadrants when several groups play at once; leaderboards between matches |
| `/display/player/{n}` | Player station screen (portrait). The Dungeon fight is drawn in 3D; `?view=2d` for the older CSS version |
| `/display/leaderboard` | Stand-alone rotating leaderboards, e.g. for a lobby TV |
| `/admin` | Operator admin: questions, categories, import/export, players, live matches and PLC health, and Insights (time in use, dwell time, returning players) |
| `/dev/buttons` | Dev stand-in for the buttons (only when dev tools are on) |
| `/dev/battle` | The station's battle animations on a loop, with made-up data (`?kind=boss&at=2100` freezes a moment) |
| `/dev/battle3d` | The same, drawn in 3D (`js/battle3d`), with the same options (`?hero=mage` for another hero, `?rival=/picture.jpg` for a rival's face on the monsters) |
| `/dev/heroes` | Draws every hero's pictures for `media/heroes` (hero cards and portraits), to save after changing a hero |

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

### Start-of-game intro

During the Intro phase (`Game:IntroSeconds`, 7 by default) the main screen and the players' station
screens put on a short show in the active theme: the title slams in, the line-up (or the two teams
and "VS") arrives, then a big 3, 2, 1. Each theme styles it in its `theme.css` (see
[docs/THEMES.md](docs/THEMES.md)): neon tubes in Neon, poker chips in Vegas, a light-up sign in
Showcase. It's timed off the server clock, so every screen in the room moves together.

To play a produced video on top instead, set `Media:IntroVideoUrl` (e.g. `/media/intro.webm`) and
make `Game:IntroSeconds` at least `Media:IntroVideoDelaySeconds` plus the video's length. Its sound
comes from the server's room audio (`Audio:IntroSound`, see Room audio below).

You can also drive a match with no screens at all, using the requests in
`src/TriviaBattle.Server/TriviaBattle.http`.

### The arcade lobby (starting a game at a station)

A station not in a game is the lobby (`Core/Arcade/Lobby.cs`, drawn by `js/arcade-lobby.js`). It
uses the five buttons:

1. **Press any button**, or swipe a card. A card the game knows plays under its name. A card seen
   for the first time is given a made-up hero name and keeps it. Without a card you're a guest
   with a made-up name.
2. **Choose your hero**: Knight, Barbarian, Mage, Rogue or Ranger. A and B flip through them, ACTION
   picks one. For now it's just a look; it's the hero you see fight.
3. **Choose your game**, laid out like a question: A *Solo quest* (on the arcade clock), B *Start a
   party* (co-op: one shared dungeon), C *Start a race* (everyone on their own delve), D back.
4. **Pick your questions**: *Everything!* or one category (only ones with active questions). A and B
   flip, ACTION picks, D goes back. A solo game starts now; a party or race starts forming.
5. **Joining**: while one is forming, the other stations show "…is forming! Press any button to join".
   Their choices: for a party, A *Join* (the host's side), B *Challenge* (the other side) or C *Solo*;
   for a race, A *Join* or B *Solo*. Joiners play the host's category. Only one forms at a time.
6. **Waiting**: everyone in it presses ACTION when ready (D leaves). It starts when all are ready, or
   when its join timer (`Game:Arcade:JoinSeconds`, 20) runs out. A late joiner gets at least
   `LateJoinSeconds` (6). Alone, it starts as a solo game. A party someone challenged is a **battle**:
   team against team (1v1, 2v2, 3v1...), each team sharing a delve.

A station left choosing for `Game:Arcade:IdleSeconds` (30) goes back to "press any button". Games
started here use `Game:Arcade:Difficulty` (Easy).

In a group game everyone sees everyone's face: their hero photo, or their hero without one. Your
teammates' faces are on your station; the other players' big moments pop up over your fight with
their faces ("…slew the Cellar Rat!", "…is down! Answer right to revive them!", "…overtook you!").
The main screen's banners, line-up and results show faces too. In a race or a battle, the monsters
you fight wear your opponents' faces (one rival per monster, in turn). The main screen shows
the room's lobby under its title. A station's card reader calls `POST /api/stations/{id}/swipe`
with `{ "cardId": "..." }`.

### Hero photos (a camera at each station)

When a Dungeon game's intro starts, each station with a camera shows the player's camera picture, with
an outline to put their face in and "Press ACTION to skip". After a moment there's a 3, 2, 1; the blips
and the shutter come from their station's speaker. The station then finds the face (MediaPipe's face
finder, vendored in `lib/mediapipe`, runs in the browser), cuts out just the face and paints it onto
the 3D knight's own face, under the helmet. It moves with every swing and hit. A portrait of that
knight shows on their plate, on their lane and in the line-up on the main screen. If no face can be
found, nothing is kept.

Pressing ACTION during the intro says no: the camera stops, or a photo already taken comes off again,
for the rest of the match. The photo is kept only in the server's memory, never on disk or in the
database, and it's forgotten when the match ends.

- `Game:Dungeon:PhotoByDefault` (`true`) takes everyone's photo unless they skip. Set it to `false` to ask
  first: the intro then offers "Put your face on your hero? Press ACTION", and only those who press get
  one. Some venues and regions need that, especially where children play.
- A station needs a webcam on its screen. Without one nothing is shown, and no countdown is heard (the
  station tells the server when its camera is on). The server log (and the admin's Live log) says when
  a station's camera came on and when its photo arrived, or why a photo couldn't be taken: not an
  https/localhost page, no camera, the camera refused or busy, no face found. A station page that isn't
  https or localhost also says so on its intro screen.
- Browsers only let `https` or `localhost` pages use a camera, so the server also listens on
  `https://<server-ip>:5001` (setting `HttpsPort`; `0` turns it off), with a certificate it makes for
  itself on first run (`certs/lan-https.pfx`, never committed; delete it to make a new one, e.g. after the
  PC's address changes). Open a station PC's screen at `https://<server-ip>:5001/display/player/N`: the
  first time, the browser warns about the certificate (Advanced, then Proceed), then asks to use the
  camera (Allow). Both are remembered. The server's log lists the exact addresses when it starts. If
  other PCs can't reach port 5001, allow it in Windows Firewall. Instead of https, a station's Chrome can
  also be started with `--unsafely-treat-insecure-origin-as-secure=http://<server-ip>:5000`. A kiosk window also needs
  camera permission for that address, granted once by hand in that profile or by the Chrome policy
  `VideoCaptureAllowedUrls`. `--use-fake-ui-for-media-stream` grants every camera and microphone
  prompt automatically, so only use it on a PC that shows nothing but the game.
  `tools/launch-screens.ps1` doesn't add any of these: it's the venue's call.
- One PC driving several stations, with a webcam each: give each station's page part of its camera's
  name, as in `/display/player/2?camera=c920`. A station that doesn't find its camera takes no photo
  rather than using the wrong one.
- `Game:Dungeon:HeroPhotos` (`false` turns photos off) and `Game:Dungeon:PhotoCountdownSeconds` (3). By
  default the photo is taken 4 seconds into the 7-second intro. Asking first, a yes only counts while the
  whole countdown still fits in the intro, about 4 seconds to press. A longer `Game:IntroSeconds` gives more.
- `/dev/battle3d?photo=/some/picture.jpg` previews the knight wearing the face in any picture on the
  server, without a camera.

## How the code is organised

```
src/
  TriviaBattle.Core/      All game rules. No web, database or hardware code, so it is easy to test.
    Matches/              MatchEngine (the phase state machine), question streams, setup, players/teams, standings
    Modes/                One class per game mode (Free-for-All, 2v2 Group, 2v2 Combined)
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
tools/                    launch-screens.ps1 (multi-monitor launcher), PlcSimulator
docs/                     Architecture notes and reference images
```

Start with [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how the pieces fit together and how
to add a game mode, station or button mapping.

## Configuration

| What | Where |
|---|---|
| Rooms and stations (count, labels, colors) | `src/TriviaBattle.Server/config/stations.json` |
| Dungeon floors and monsters (HP, damage, hourglass, sprite) | `src/TriviaBattle.Server/config/dungeon.json` |
| Dungeon combat numbers (adventurer HP, damage per difficulty, crits, combos, downed time, revives, party combos, battle beat lengths) | `"Game:Dungeon"` in `appsettings.json` |
| Classic quiz or Dungeon Quiz Quest | Operator screen, `/operator` (default: `"Game:Format"` in `appsettings.json`) |
| Round length (groups, and every Classic game) | Operator screen (default: `"Game:MatchSeconds"`) |
| Solo dungeon clock: starting time, time per monster and per boss, longest solo game, time per continue, continue countdown | Operator screen (defaults: `"Game:Dungeon:Solo*"`) |
| Button id -> station + answer | `src/TriviaBattle.Server/config/buttons.json` |
| Keyboard key -> button id (dev stand-in for buttons) | `src/TriviaBattle.Server/config/keyboard.json` |
| PLC on/off, address, register map, button codes, LED registers | `src/TriviaBattle.Server/config/plc.json` (see docs/PLC-PROTOCOL.md) |
| Round length and wrong-answer penalty (lockout or lose points) | Operator screen, `/operator` (defaults: `"Game"` in `appsettings.json`) |
| Intro/results timings, points per answer, lockout and grace times | `"Game"` in `appsettings.json` |
| Intro video file and start delay | `"Media"` in `appsettings.json` |
| Room audio: sound files, starting volumes, channel count | `"Audio"` in `appsettings.json` |
| Which speaker is on which sound card output | `config/stations.json` (`AudioOutput`, `Audio`) |
| Leaderboard size, whether guests count | `"Leaderboards"` in `appsettings.json` |
| Database file | `ConnectionStrings:TriviaBattle` in `appsettings.json` |
| Admin API key (empty = admin endpoints open, a warning is logged) | `AdminApiKey` in `appsettings.json` |
| HTTP port | `Port` in `appsettings.json` |
| Dev helpers such as `/api/dev/buttons` and `/api/dev/continue/{stationId}` | `DevTools:Enabled` (defaults to on in Development only) |

Any setting can also be overridden with an environment variable, e.g. `Game__MatchSeconds=30`
(the admin page's game rules win once they've been saved there).

## Themes

The screens come in themes: **neon** (default; vaporwave LED: sunset, rolling grid, colour-cycling
LED strips), **showcase** (70s game show) and **vegas** (blue-and-gold Las Vegas showroom: chasing
marquee bulbs, gold-foil titles). Each theme is a `theme.css` for looks and a `copy.json` for every line
of on-screen text, so designers and writers can change them without touching code. Switch every
screen live from the Operator screen (`/operator`), or preview one with `?theme=vegas` in its URL.
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
- **Operator** (the default tab, also at `/operator`, linked from the start page): the venue staff's
  screen. Sound (volume sliders, sound card status, a test button per speaker), game rules
  (round length, solo clock, wrong-answer penalty), theme, running matches (with the correct answers) and abort,
  and PLC health.
- **Live log**: is anything going wrong right now? Which screens are connected (a missing one shows
  red), and the server log as it happens: matches, buttons, sound, rejected requests (e.g. a kiosk
  start the server refused, and why) and errors the screens themselves hit. Refreshes every second.

Changes take effect from the next match.

## Room audio

All sound comes from the **server**, through the room's StarTech 7.1 USB sound card (the screen
PCs play nothing), using RangerShack's audio engine: one 8-output card, three Dayton stereo
amps and a powered sub. Windows must have the card set to **7.1**.

| Output (0-based) | Jack | Speaker (default wiring) |
|---|---|---|
| 0 / 1 | green (front L/R) | amp 1: Player 1, Player 2 |
| 2 | orange (centre) | powered sub |
| 4 / 5 | black (rear L/R) | amp 2: Player 3, Player 4 |
| 6 / 7 | grey (side L/R) | amp 3: Room, Attract (outside) |

What plays where:
- **Each station's speaker:** a success or fail stinger the moment that station answers (both
  teammates hear it in 2v2 Group). One is picked at random from the three of each. Dungeon games
  play the fight instead (`Audio:Dungeon`): hit, crit, kill, floor, boss, party combo, revive, hurt,
  downed. The files in `media/audio/dungeon/sfx` are generated 8-bit placeholders.
- **Room speaker, plus the same on the sub:** main theme in the lobby, the intro's sound,
  in-game music while the clock runs, victory music at the results.
- **Attract:** the main theme, looping all the time.

Wiring and the card's name are in `config/stations.json` (`AudioOutput` per station, `Audio` per
room). Sound files and volumes are `"Audio"` in `appsettings.json`; the files live in
`wwwroot/media/audio`. The Operator screen (`/operator`) has a volume slider for each kind of sound (room music, attract,
stingers, intro, sub; 0-200%), which apply instantly and are saved. After wiring, use **Test**
next to each speaker there; it also shows how the card opened.

On a PC without the card (a laptop or headset) it runs in RangerShack's stereo test mode by itself:
every speaker in both ears (except attract and sub, which would just double the room music). At
the venue, "2ch" or "STEREO FALLBACK" in the status means the card isn't set to 7.1, or the wrong
device was picked (set `Audio.Device` in stations.json to part of its Windows name).

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
| 6 | Themes (editable looks + copy), first visual pass | Done: neon, showcase, vegas (see docs/THEMES.md) |
