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
   station): it signs you in, or takes a continue when a dungeon game's "Continue?" is up.
2. **Kiosk window** (optional): pick a mode, add players with *Simulate card swipe* or *Play as guest*,
   then start. Everyone plays the Tank.
3. Close the screens with `.	ools\launch-screens.ps1 -Close`, and close the server's window to stop it.

This works the same with the screens on separate devices (e.g. four tablets as player screens):
open `http://<server-ip>:5000/display/player/1` on each and tap the answers.

Other ways to press buttons without hardware:
- `/dev/buttons`: all 20 buttons on one page (4 answers and the action button per station), plus a
  "card swipe" per station to continue a dungeon game when its "Continue?" is up, and buttons to
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
| `/display/player/{n}` | Player station screen, locked to portrait 9:16 (a standard 1080p monitor on its side): on any other window it's shown as big as fits, with black bars. The Dungeon fight is drawn in 3D; `?view=2d` for the older CSS version. `/display/player/{n}/screen` is the same page without the frame |
| `/display/leaderboard` | Stand-alone rotating leaderboards, e.g. for a lobby TV |
| `/admin` | Operator admin: questions, categories, import/export, players, live matches and PLC health, and Insights (time in use, dwell time, returning players) |
| `/dev/buttons` | Dev stand-in for the buttons (only when dev tools are on) |
| `/dev/battle` | The station's battle animations on a loop, with made-up data (`?kind=boss&at=2100` freezes a moment) |
| `/dev/battle3d` | The same, drawn in 3D (`js/battle3d`), with the same options (`?hero=wizard` for another hero, `?rival=/picture.jpg` for a rival's face on the monsters) |
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
   with a made-up name. A different card swiped at a station someone left half-way starts afresh
   (their hero and photo don't carry over).
2. **Choose your hero**: Tank, Healer, Wizard, Rogue, Ranger, Pugilist or Warlock. A and B flip through
   them, ACTION picks one. Each is a class with its own power in the fight (see "Hero classes" below).
3. **Your hero photo** (see "Hero photos" below): the camera comes on, counts 3, 2, 1 and shows your
   hero wearing your face. ACTION keeps it, B takes another, D goes on without one. A station without
   a camera skips this step by itself.
4. **Choose your game**, laid out like a question: A *Solo quest* (on the arcade clock), B *Start a
   party* (co-op: one shared dungeon), C *Start a race* (everyone on their own delve), D *Team battle*
   (two teams, everyone picks a side). ACTION goes back to the heroes.
5. **Pick your questions**: *Everything!* or one category (only ones with active questions). A and B
   flip, ACTION picks, D goes back. A solo game starts now; any other starts forming.
6. **Joining**: while one is forming, the other stations show "…is forming! Press any button to join".
   Their choices: for a party or race, A *Join* or B *Solo*; for a team battle, A *Join {host}'s team*,
   B *Join the other team* or C *Solo*. Joiners play the host's category. Only one forms at a time.
7. **Waiting**: everyone in it presses ACTION when ready (D leaves). It starts when all are ready, or
   when its join timer (`Game:Arcade:JoinSeconds`, 20) runs out. A late joiner gets at least
   `LateJoinSeconds` (6). Alone, it starts as a solo game. A team battle is team against team (1v1,
   2v2, 3v1...), each team sharing a delve; with nobody on the other team it starts as a party.

A station left choosing for `Game:Arcade:IdleSeconds` (30) goes back to "press any button". Games
started here use `Game:Arcade:Difficulty` (Easy).

In a group game everyone sees everyone's face: their hero photo, or their hero without one. Your
teammates' faces are on your station, and the other players' moments that matter to you show in a
caption across the top of the monster's room, one at a time, with their face ("…revived you!", "…is
down! Answer right to revive them!", "…slew the Goblin Quizmaster!", "…overtook you!").
The main screen's banners, line-up and results show faces too. In a race or a battle, the monsters
you fight wear your opponents' faces (one rival per monster, in turn). In the 3D fight your teammates'
heroes stand in the room with yours, wearing their faces: when one answers right they dash in and hit
your monster, when one is hit they reel, and a downed one lies on the floor until revived. When a rival
overtakes you or slays a boss, their hero pops up beside your monster and cheers. The main screen shows
the room's lobby under its title. A station's card reader calls `POST /api/stations/{id}/swipe`
with `{ "cardId": "..." }`.

### Hero classes

Every hero is a class with its own power (`Core/Dungeon/HeroClass.cs`; the rules are in `DungeonRun`, the
numbers in `ClassSettings`, set under `"Game:Dungeon:Classes"` in `appsettings.json`). A **fast** answer is a
right answer within a set time: 3 seconds (`Game:Dungeon:CritWithinSeconds`), the same for every question.
Fast answers are critical hits (x1.5) for everyone.

Every class also has three basic stats, set in `"Game:Dungeon:Classes"` in `appsettings.json` (all seven are
written out there): `Hp` (a share of the 100 HP everyone starts with), `DamageDealt` (a share of a right answer's
damage) and `DamageTaken` (a share of a monster's hit). 1 is normal: `"Wizard": { "Hp": 0.8, "DamageDealt": 1.2 }` is a
frailer, harder-hitting Wizard. All are 1 except the Tank, which takes 0.6. Changes apply from the next match.

| Class | Power | Numbers (defaults) |
|---|---|---|
| **Rogue** | Fast answers do much more damage. | crit x2.5 instead of x1.5 |
| **Tank** | Has longer to answer fast, and takes less damage from monsters. | fast within 5 s; takes 60% |
| **Healer** | A fast answer heals whoever in the party is lowest on HP (themselves included). With nobody hurt, it poisons the monster instead: one stack more each time, every stack hurting it every second until it dies. Healing scores like damage; poison scores nothing. | heals 15; 2 per stack per second |
| **Wizard** | A fast answer hits every monster in the room: the one in front fully, the others half as hard. | splash 50% |
| **Warlock** | Half the time, as the hourglass gets down to its last 4 seconds, their patron shows them the right answer (only on their screen). Any answer after 6 seconds costs them 8 HP (it never downs them). | 50%, at 4 s left; after 6 s, 8 HP |
| **Pugilist** | The only class with a combo: every right answer in a row does 15% more, up to +150%. A wrong answer or a hit ends it. | +15% a step, max +150% |
| **Ranger** | Sees the next question 1.5 seconds before the pause after each answer ends. Buttons, the hourglass and the fast window still start when it ends. | 1.5 s early |

Rooms can hold several monsters at once (`config/dungeon.json`: `"Rooms": [["rat", "slime"]]`). The party
fights the one in front; the others wait their turn, and only the one in front attacks.

**Bosses** end each floor. Clearing the room before one plays its entrance on the station (and its sound, and a
banner on the main screen). A boss powers up: each player's questions against it go round a cycle where it hits
for 50% of its damage, then 100%, then at **full charge** 250%. At full charge a wrong answer (or running out of
time) takes that big blow, and a right answer **staggers** it instead (50% more damage). Either way it starts over.
The station shows the charge, and warns at full power. Set in `"Game:Dungeon"`: `BossChargeSteps` (3; 0 = no
cycle), `BossWeakHit` (0.5), `BossChargedHit` (2.5), `StaggerBonus` (0.5).

A kiosk game has no hero choice: everyone plays the Tank. In 2v2 Group, where teammates share one question,
the team fights as the first teammate's hero.

To add a hero (a new class, or a new model for one): put the model in `wwwroot/media/models/characters`
(a `.glb`; credit it in `media/models/CREDITS.md`), give it an entry in `HEROES` in
`wwwroot/js/battle3d/models.js`, add its key to `Heroes.All` (`Core/Arcade/Heroes.cs`) and its two lines to
`heroes` in `themes/neon/copy.json`, then open `/dev/heroes` and save its pictures into `wwwroot/media/heroes`.
The Healer and Warlock borrow the mage's and ranger's models for now; their pictures in `media/heroes` are
copies of the Wizard's and Ranger's until someone saves theirs from `/dev/heroes`.

### Hero photos (a camera at each station)

At the stations the photo comes right after choosing a hero (`Game:Arcade:HeroPhotos`, `true`), following
the same rule as games: none in Classic or with `Game:Dungeon:HeroPhotos` off, and where the venue asks
first (`PhotoByDefault` false) the step asks "Put your face on your hero?" (ACTION yes, D no). The
station shows the player's camera picture, with an outline to put their face in, and counts 3, 2, 1
(`Game:Arcade:PhotoSeconds`, 4, from the step starting to the photo), with the blips and the shutter
from their station's speaker once its camera is on. The station then finds the face
(MediaPipe's face finder, vendored in `lib/mediapipe`, runs in the browser), cuts out just the face and
paints it onto their hero's own face. It moves with every swing and hit in the 3D fight. A portrait of
that hero shows on their plate, in the lobby, on their lane and in the line-up on the main screen. If no
face can be found, nothing is kept, and B tries again. ACTION keeps the photo, B takes another, D goes
on without one. The photo goes into their game when it starts; that game's intro takes no other.

A game started at the kiosk takes its photos in the intro instead: each station with a camera shows the
camera picture with "Press ACTION to skip"; after a moment there's a 3, 2, 1, with the blips and the
shutter from their station's speaker. Pressing ACTION during the intro says no: the camera stops, or a
photo already taken comes off again, for the rest of the match.

The photo is kept only in the server's memory, never on disk or in the database. It's forgotten when the
player drops it, takes another or walks away from the lobby, and when their match ends.

- `Game:Dungeon:PhotoByDefault` (`true`) takes everyone's photo in a kiosk game's intro unless they skip.
  Set it to `false` to ask first: the intro then offers "Put your face on your hero? Press ACTION", and
  only those who press get one; the stations' photo step asks first too. Some venues and regions need
  that, especially where children play. A photo from the stations never shows in a game without photos.
- A station needs a webcam on its screen. Without one nothing is shown, the lobby's photo step skips
  itself (unless another screen open as that station has a camera: that one takes it), and no intro countdown is heard (the station tells the server when its camera is on). The server log (and the admin's Live log) says when
  a station's camera came on and when its photo arrived, or why a photo couldn't be taken: not an
  https/localhost page, no camera, the camera refused or busy, no face found. A station page that isn't
  https or localhost also says so on its intro screen. It also warns when a station has more than one
  screen open (each one with a camera takes the photo; only the first counts), and when a station can
  only draw the 2D fight (no WebGL 2), where heroes wear no faces and teammates aren't in the room.
- Browsers only let `https` or `localhost` pages use a camera, so the server also listens on
  `https://<server-ip>:5001` (setting `HttpsPort`; `0` turns it off), with a certificate it makes for
  itself on first run (`certs/lan-https.pfx`, never committed; delete it to make a new one, e.g. after the
  PC's address changes). On a station PC, open the server's front page (`http://<server-ip>:5000`) and
  click its player number: station links go to the https address. The first time, the browser warns
  about the certificate (Advanced, then Proceed), then asks to use the camera (Allow). Both are
  remembered. The front page shows which player numbers already have a screen open (and on which PC),
  and a station screen opened as a number that's already open says so in a bar along its top. A station page opened over http by address switches to https by itself once the browser
  trusts it, and until then shows a "Turn on this station's camera" bar. Each screen must be its own
  station number. The server's log lists the exact addresses when it starts. If
  other PCs can't reach port 5001, allow it in Windows Firewall. Instead of https, a station's Chrome can
  also be started with `--unsafely-treat-insecure-origin-as-secure=http://<server-ip>:5000`. A kiosk window also needs
  camera permission for that address, granted once by hand in that profile or by the Chrome policy
  `VideoCaptureAllowedUrls`. `--use-fake-ui-for-media-stream` grants every camera and microphone
  prompt automatically, so only use it on a PC that shows nothing but the game.
  `tools/launch-screens.ps1` doesn't add any of these: it's the venue's call.
- One PC driving several stations, with a webcam each: give each station's page part of its camera's
  name, as in `/display/player/2?camera=c920`. A station that doesn't find its camera takes no photo
  rather than using the wrong one.
- `Game:Dungeon:HeroPhotos` (`false` turns photos off in games) and `Game:Dungeon:PhotoCountdownSeconds`
  (3). By default a kiosk game's photo is taken 4 seconds into the 7-second intro. Asking first, a yes only counts while the
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
| Dungeon floors and monsters (HP, damage, hourglass, sprite; HP and damage added per loop) | `src/TriviaBattle.Server/config/dungeon.json` (read at startup: restart after editing) |
| Dungeon combat numbers (adventurer HP, damage per difficulty, crits, downed time, revives, party combos, battle beat lengths) | `"Game:Dungeon"` in `appsettings.json` |
| Hero class powers (Rogue's crit, Tank's fast window and armour, Healer's heal and poison, Wizard's splash, Warlock's patron, Pugilist's combo, Ranger's early look) | `"Game:Dungeon:Classes"` in `appsettings.json` (see "Hero classes") |
| Classic quiz or Dungeon Quiz Quest | Operator screen, `/operator` (default: `"Game:Format"` in `appsettings.json`) |
| Round length (groups, and every Classic game) | Operator screen (default: `"Game:MatchSeconds"`) |
| Dungeon clock (solo and group, no cap): starting time, time per monster and per boss, a continue's clock, continue countdown | Operator screen (defaults: `"Game:Dungeon"`: `ClockStartSeconds`, `SecondsPerKill`, ...) |
| Floors: how many rooms, each room's HP budget and monster pool, the boss, each floor's question mix | `config/dungeon.json` (made up fresh for each match) |
| How much tougher monsters are for a party | `"Game:Dungeon:PartyHpPerMember"` |
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
  (the Dungeon clock, Classic's round length, wrong-answer penalty), theme, running matches (with the correct answers) and abort,
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
