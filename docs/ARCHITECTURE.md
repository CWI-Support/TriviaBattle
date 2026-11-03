# Architecture

## The big idea

**One server owns all game state. Every screen is a thin browser window that shows what the
server tells it and sends back what the user did.** No game rules live in the browser.

```
  physical buttons ──► PLC ──Modbus──┐
                                     ▼
  keyboard (dev) ──────────────► IButtonInput          (input layer: knows raw button ids)
                                     │ ButtonEvent {buttonId 10-29, sequence, time}
                                     ▼
                               ButtonRouter ──uses──► ButtonMap (config/buttons.json)
                                     │ ButtonPress {stationId, answerIndex, sequence, time}
                                     ▼
  kiosk ──HTTP (start match)──► MatchHost ──────────► MatchEngine (one per match)
  admin ──HTTP──────────────►   (one lock)               uses IGameMode + MatchSettings
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
| **Station** | One player position: a screen plus 4 answer buttons and an action button. Id = room + number, e.g. `A1`. |
| **Match** | One game, played by a *set of stations*. A match isn't tied to a room, so two rooms can later be linked into one 8-station match. |
| **Seat** | A player sitting at a station for a match. |
| **Answer owner** | Who an answer counts for: the station (individual modes) or the team (2v2 Group). |
| **Question stream** | One answer owner's own run of questions while the clock runs (`QuestionStream`). Answering moves straight on to the next one. |
| **Phase** | Where the match is: Intro, Playing, Results, Finished. |
| **Snapshot** | The full state of a match as one particular screen is allowed to see it. |

## The match engine

`Core/Matches/MatchEngine.cs` is a plain state machine. Three things drive it:

- `Tick(now)`: called 20x/second by `MatchTicker`. It ends wrong-answer lockouts that have run out, and moves to the next phase when the current one's time is up.
- `HandlePress(press, now)`: a button press from one of this match's stations.
- `Abort(now)`: operator stop.

Each call returns the `MatchEvent`s that happened. The engine never reads the clock itself and
has no threads or locks, so tests can play a whole match in microseconds with a fake time.
`MatchHost` holds the only lock, so presses, ticks and admin actions never overlap.

Phase flow:

```
Intro → Playing (the clock runs for Game:MatchSeconds) → Results → Finished
           ↑   ↓
         Continue?   solo Dungeon games only: the clock ran out; a swipe goes back to Playing
```

Every match is a **timed rapid-fire round**: answer as many questions as you can before the clock
runs out. While Playing, each answer owner has its own `QuestionStream`:

- A press answers that owner's current question, scores it, and shows the next question at once.
- Each stream gets its own shuffled order of the whole category (chosen difficulty first), so
  neighbours can't read ahead on each other's screens. A stream that runs out starts again.
- A press within `Game:NewQuestionGraceSeconds` of a new question appearing is ignored, so one press
  (or two 2v2 Group teammates pressing together) can't also answer the next, unseen question.
- Right: `Game:CorrectPoints`. Wrong: depends on `WrongAnswerPenalty`. `Lockout` freezes that
  owner's buttons for `LockoutSeconds` (the engine raises `LockoutEnded` when it runs out);
  `LosePoints` takes off `WrongAnswerPoints`, so scores can go below zero.

The round length and the penalty are game rules the operator sets on the Operator screen (`/operator`, the admin page's first tab).
`GameRulesService` saves them in the `AppSettings` table and lays them over `"Game"` from
appsettings.json when a match starts.

### Fair timing

Every press carries a `Sequence` (press order) and a `PressedAt` time from the input layer. With
the PLC, sequence comes from the PLC's own latch order. Who pressed first is therefore decided by
the hardware, not by network or polling delays. Response time is `PressedAt - question shown`;
total time on correct answers breaks score ties.

## Game modes

Game modes are small strategy classes (`IGameMode` in `Core/Modes`: `FreeForAllMode`,
`TeamSharedAnswerMode`, `TeamCombinedScoreMode`), chosen at the kiosk for each match, so a new
mode doesn't touch the engine. A game mode answers three questions:
1. Does it use teams?
2. Who owns a player's answers (`GetAnswerOwnerId`)? That's also whose question stream they play:
   their own, or their team's shared one in 2v2 Group.
3. Who gets the points (or penalty) for an answer (`ScoreAnswer`)?

### Dungeon format (Dungeon Quiz Quest)

`MatchSettings.Format` picks the game: `Classic` (the rapid-fire quiz) or `Dungeon`. A mode (who
owns answers, who scores) is independent of the format, so every mode works in both.

In Dungeon format the engine has a `DungeonRun` (`Core/Dungeon`), which holds the combat rules;
`MatchEngine` runs the match as usual (phases, question streams, scoring) and hands it every answer and
tick. The run is built around parties, so solo and group play share one set of rules:
- A **party** is a team, or a player on their own (a solo game is a party of one). Each party has one
  `Delve`: its floor and the monster it's fighting. Parties race each other down.
- Each answer owner is an **`Adventurer`** with their own HP, streak and hourglass. Every adventurer in a
  party hits the same monster. In 2v2 Group teammates share their question, so the team is one adventurer;
  in 2v2 Combined each teammate is an adventurer on the team's delve.

The rules:
- A correct answer is `DungeonRun.Strike`: damage by difficulty, a crit if fast, a combo on the
  adventurer's streak, taken off the party's monster. The answer's points are the damage, so scores,
  results and leaderboards work unchanged.
- A wrong answer, or the adventurer's hourglass running out (`Tick`), is `DungeonRun.TakeHit`. A timeout
  is recorded as a `GivenAnswer` with `TimedOut` (no press) and the question moves on.
- At 0 HP an adventurer is downed: their stream is locked out, then they get up at full HP. On their own
  that's `DownedSeconds`. In a party of two or more they wait for their teammates: `ReviveAnswers` right
  answers from them (counted on whoever has been down longest) revive them at once, with
  `PartyDownedSeconds` as the safety net. When the whole party is down, the monster heals to full and
  everyone gets up after `DownedSeconds`. Nobody is knocked out of a paid game.
- **Party combo:** when every other standing member of the party has answered right within
  `PartyComboSeconds`, the hit that completes it does `PartyComboBonus` more damage
  (`DealtHit.PartyCombo`). Then everyone needs a fresh right answer for the next one.
- The hourglass only runs while their buttons work: it stops during a lockout and restarts after.
- **The clock.** Groups play a fixed round of `MatchSeconds`, so everyone starts and ends together. A
  solo game (one player in the match) runs on an arcade clock: Playing starts at `SoloStartSeconds`,
  and every kill adds time (`DungeonRun.SecondsEarnedBy`: `SoloSecondsPerKill`, or `SoloSecondsPerBoss`
  for a boss). `MatchEngine` moves `PhaseEndsAt` back, never past `SoloMaxSeconds` after Playing
  started (the hard cap). `MatchEngine.RoundSeconds` is the starting length, `HasSoloClock` says which
  clock it is; the snapshot carries both (`MatchSeconds`, `SoloClock`), and screens pop "+10s" when
  the end time moves.
- **Continue?** When a solo clock runs out, the match goes to `MatchPhase.Continue` for
  `SoloContinueCountdownSeconds` instead of Results. `MatchEngine.Continue(now)` (a swipe) goes back
  to Playing for `SoloContinueSeconds`, same floor, same HP, the same question shown again; no swipe
  and the countdown runs into Results. The hard cap counts every stretch of play, so a continue is
  only offered while a whole one still fits. `MatchHost.ContinueAtStation` is what the station card
  readers will call; until they exist it's `POST /api/dev/continue/{stationId}` (the dev buttons page)
  or the Operator screen's "Give a continue" (`POST /api/matches/{id}/continue`). The clock and
  continue numbers are Operator-screen rules (`GameRulesService`).
- **Power-ups** (`PowerUp`): Potion, Fireball, Freeze, 50/50, Shield, Time Crystal (solo only). Each
  adventurer holds one (`Adventurer.Held`). A kill drops one `PowerUpDropChance` of the time (a boss
  always) and every `PowerUpStreak` right answers in a row earn one, only with empty hands
  (`DungeonRun.FindPowerUp`; it's recorded on the hit as `DealtHit.Found`). The action button (the fifth
  button, `ButtonPress.Action`) uses it: `MatchEngine.HandlePress` hands it to `DungeonRun.UsePowerUp`. A
  Potion goes to whoever in the party needs it most (a downed teammate gets straight up); a Fireball
  multiplies the next right answer (`FireballMultiplier`); a Freeze holds the hourglass (`FreezeSeconds`,
  then it starts over); a 50/50 hides two wrong answers of the current question, which the snapshot
  sends as `QuestionView.Hidden` (wrong ones only) and presses on them are ignored; a Shield blocks the
  next hit (`TakenHit.Blocked`, no damage, the streak survives); a Time Crystal adds `TimeCrystalSeconds`
  to a solo clock. The screens show the slot, the armed effects and a pop per use (`js/dungeon-powerups.js`).
  `POST /api/dev/powerup/{stationId}/{powerUp}` hands one out for testing (the dev buttons page has buttons).
- **The arcade lobby** (`Core/Arcade`). Games start at the stations: `Lobby` (one per room) is every
  station that isn't in a game, as a `LobbySeat` stepping Idle, Hero, Photo, Game, Category, Waiting, and
  the one `LobbyGroup` (a party, race or team battle, with its `LobbyCategory`) that may be forming. A
  battle is joined on the host's team (`GameChoice.Join`, side 0) or the other (`JoinOtherSide`, side 1);
  with nobody on the other side it starts as a party, and if the host's team empties the other becomes
  it (`LobbyGroup.Host` is the first on side 0). Like `MatchEngine` it's pure: time is passed in, presses
  and card swipes go in, and it hands back `LobbyStart`s. `LobbyStart.ToSetup` makes the `MatchSetup`:
  solo and race are free-for-all, a party is one team in 2v2 Combined (a single team is allowed: a co-op
  party), and a battle is one team a side, each named after its first player. The categories on offer (`Lobby.SetCategories`) are refreshed by `LobbyHost` every minute. `Heroes` lists the five heroes (`SeatSetup.Hero`, then `Player.Hero` and
  `PlayerView.Hero`); `HeroNames` makes up names. On the server, `ButtonRouter` sends a press to
  `MatchHost` when its station is playing (`IsPlaying`), and to `LobbyHost` otherwise. `LobbyHost` drives
  the room's `Lobby` (held by `Lobbies`, behind one lock), ticks with `MatchTicker`, looks cards up (a new
  card is registered with a made-up name) and starts games with `MatchHost.StartMatchAsync` (every active
  category). `LobbyBroadcaster` pushes the change. Every `ScreenState` carries the room's `LobbyView`.
  Settings: `Game:Arcade` (`LobbySettings`).
- **Hero photos** (a player's face on their hero). At the stations the photo is the lobby's Photo step,
  right after the hero (`LobbySettings.HeroPhotos`, `PhotoSeconds`), following the venue's rule for games
  (`Lobby.SetPhotoRule`, set by `LobbyHost` from the game rules: `LobbyPhotoRule.Off` in Classic or with
  `Dungeon.HeroPhotos` off, `AskFirst` with `PhotoByDefault` off: the step asks, `PhotoTakeAt` null until
  ACTION). The seat's `PhotoTakeAt` goes out as `LobbySeatView.PhotoTakeAtMs`, the station takes it
  (`HeroCamera.followLobby`) and sends it, with that `takeAtMs`, to `PUT /api/photos/lobby/{stationId}`,
  which `PhotoStore` keeps by station (`KeepLobby`) while `Lobby.PhotoArrived` counts it (only for the
  take it's on: a late upload from a replaced take is refused) (`LobbySeat.HasPhoto`, `PhotoVersion`; screens fetch
  `GET /api/photos/lobby/{stationId}/portrait`). ACTION keeps it, B retakes, D drops it; a station without
  a camera posts `.../skip`, refused while another screen open as that station has one (station screens
  tell the hub after each join: `GameHub.ReportCamera`, `ScreenRegistry.AnyWithCamera`). Its camera coming on (`POST .../lobby/{stationId}/camera`, `Lobby.PhotoCameraOn`)
  lets `LobbyHost.Tick` play the "3, 2, 1" and the shutter on its speaker (`Lobby.PhotoCountdowns`,
  `RoomSoundService.PlayPhotoCountdown`, the same sounds as the intro's). `LobbyHost` forgets a lobby photo as soon as its seat has none, and when a
  game starts moves its players' photos into the match (`TakeLobby`, then `Keep` under the new match id in
  `StartMatchAsync`'s `created`, before any screen hears of it). `SeatSetup.Photo` tells the
  engine what the lobby settled (`Player.PhotoFromLobby`: brought one, `Photo` 1, or chose none,
  `PhotoDeclined`): it schedules no intro photo for them, offers none, and their intro ACTION does
  nothing. In a game without photos (`MatchRoster`'s `takesPhotos`) the lobby's photo doesn't count.

  A kiosk game takes them in the intro (`DungeonSettings.HeroPhotos`, `PhotoByDefault`,
  `PhotoCountdownSeconds`). By default the engine schedules everyone's photo when the intro starts
  (`Player.PhotoAt`: `PhotoLead` plus the countdown). The action button in the intro says no
  (`PhotoButton`: `Player.DeclinePhoto`, `PhotoDeclined`): the photo is off for the match, and one already
  taken is dropped. Asking first instead (`PhotoByDefault` false), the button says yes while
  `PhotoOfferEndsAt` hasn't passed (`PhotoAsked`), and a second press says no. The snapshot tells the station
  (`YouView.PhotoOfferUntilMs`, `PhotoAtMs`). The station tells the server when its camera is on
  (`POST /api/photos/{matchId}/{stationId}/camera`, `MatchEngine.PhotoCameraOn`); only then does `Tick`
  raise `PhotoCountdown` (3, 2, 1, then 0 for the shutter), so a station without a camera stays quiet.
  `js/hero-camera.js` takes the photo at `PhotoAtMs` and makes two pictures, both sent to
  `PUT /api/photos/{matchId}/{stationId}` (a form):
  - **face**: `js/face-cutout.js` finds the face with MediaPipe's Face Landmarker (vendored in
    `lib/mediapipe`, run in the browser), cuts it out along its outline and lays it out the same way every
    time: eyes level and a set distance apart, with eyes, mouth and chin at set heights.
  - **portrait**: `js/battle3d/portrait.js` renders the knight's head wearing that face, for every screen
    that draws in 2D. No face found: nothing is sent.

  `PhotoStore` keeps both in memory only, never on disk or in the database, and forgets them when the player
  says no or the match ends (it's an `IMatchBroadcaster` for that). `MatchHost.PhotoTaken` then `MatchEngine.PhotoTaken` count the
  photo (`Player.Photo`, sent as `PlayerView.Photo`, raising `PhotoTaken`). Every screen fetches what it needs
  once per number (`GET /api/photos/{matchId}/{stationId}/{face|portrait}`, via `js/hero-photo.js`) and keeps it as
  an in-memory blob URL (the server sends it `no-store`). The portrait shows on the plate, the lanes, the party
  list and the intro line-up. The station's 3D knight wears the face (`Actor.wearFace`, `js/battle3d/face.js`):
  - the front of that knight's head (its own copy) is split off into a face mesh on the same skeleton, so it
    moves exactly as the head does;
  - the knight's own eye, brow and nose bumps are smoothed to the face's curve, and the triangles that turns
    over are dropped;
  - the photo is shown on the knight's skin colour, redrawn so the player's eyes, mouth and chin land where
    `MODELS.hero.face` says, inside the helmet's opening.

  `RoomSoundService` plays the countdown's blips and the shutter on the station's speaker. A station names
  its camera with `?camera=` when one PC drives several.
- **Faces on everyone's screens.** `HeroPhoto.portrait` (a player's hero photo, or their hero's picture)
  is used wherever a player shows: party members, the main screen's lanes, banners
  (`DungeonView.noteMoments`), line-up and results, and the station's final places. `js/dungeon-feed.js`
  watches the other players in the snapshot (each one's `Delve.LastDealt`, `LastTaken`, floor, and the
  standings) and shows the moments that matter to you (a revive, a teammate down, a party combo, a boss
  slain, being overtaken) with their face, one at a time, in a caption across the top of the monster's
  room (`DungeonFeed.caption`, drawn by `DungeonEncounter.view`), never over your own fight. In a race or a battle the
  monster wears a rival's face (`Battle.maskFoe`, `Actor.wearMask`: their "face" picture as a sprite on
  the monster's `Head` bone).
- Heroes in 3D: `HEROES` in `js/battle3d/models.js` (KayKit adventurers on one rig: their gear, the
  part dyed the station's colour, their attacks, where a face goes). The station's fight shows the
  player's own hero (`Battle.show`); `js/battle3d/portrait.js` draws heroes as pictures, and `/dev/heroes`
  saves each hero's card and portrait to `media/heroes` for the screens that draw in 2D.
- The other players in your 3D fight (`js/battle3d/others.js`, `Others.update` every frame). Teammates with
  a delve of their own (`PlayerView.Delve`) stand in your room, in their heroes, faces and station
  colours (`Battle3D.show`'s `colors`). When a teammate's `lastDealt.Number` goes up they dash in and
  strike, and your monster flinches (`showIdle`'s `foeFlinch`). A new `lastTaken` makes them reel, and
  `downedUntilMs` keeps them on the floor. A rival's moment in the caption (`DungeonFeed.showing` with
  `rival`) pops their hero up beside your monster, cheering. None of it is a beat: it's timed from when the
  screen saw it and never holds up your question. `/dev/battle3d?team=barbarian,mage` and `?cameo` preview it.
- Events: `MonsterHit`, `AdventurerHit`, `AdventurerGotUp`, `PowerUpFound`, `PowerUpUsed`, `PhotoAsked`,
  `PhotoDeclined`, `PhotoTaken`, and from `Tick`: `QuestionShown` when a battle beat ends and `PhotoCountdown`
  (alongside the usual `AnswerGiven`).
- **The room joins in.** `RoomSoundService` plays each fight as a timed sequence that lands with the
  beat's picture (the blow at 0.19 s, the burst at 1 s, the floor or boss under the curtain, the loot with
  its card; the moments in `css/dungeon-battle.css`'s table), plus block, loot and power-up sounds, and
  `Audio:BossMusic` while anyone in the room fights a boss. `RoomOutputCoordinator` turns a station's
  answer LEDs off while its beat plays (presses don't count then) and on again at `QuestionShown`, lights
  the action button while they hold a power-up, and sends the room the `Boss` lighting cue (6) while
  anyone in it faces a boss.

- **Battle beats.** After every answer the next question waits a moment while the screen shows the
  fight (`DungeonRun.BeatSecondsAfter`: `HitBeatSeconds`, `HurtBeatSeconds`, `KillBeatSeconds`,
  `FloorBeatSeconds`). It's simply the next question's `CurrentShownAt` moved later: presses before it
  are ignored like any press before a question appears, and the hourglass starts when it shows. The
  clocks keep running (time per kill and the round length allow for it). `YouView.Beat` tells the
  station what to play (`hit`, `kill`, `floor`, `boss`, `hurt`, `downed`) and from when to when; hits
  carry what the scene needs to draw them (`DealtHit.MonsterSprite`, `MonsterHpLeft`, `TakenHit.HpLeft`).

New dungeon rules (time, power-ups, revives) belong in `DungeonRun`, not in `MatchEngine`.

Floors and monsters are content in `config/dungeon.json`, loaded into a `DungeonMap` at startup (a
broken file stops the server with the problem spelled out). Past the last floor the dungeon loops
with tougher monsters. Snapshots carry a `DelveView` (on `PlayerView`, `TeamView` and `YouView`): one
adventurer's fight, combining their party's delve (floor, monster, kills, `LastHit` by anyone in the
party) with their own HP, hourglass and hits, plus `Party` (every member's HP and revive progress).
`TeamView.Delve` is the whole party's fight for its main-screen lane. While a player is downed in a
party, `YouView.Helping` carries a standing teammate's question so they can shout the answer (the one
exception to "a player only sees their own question"; it never includes the answer). Its `Path` lists
every monster of the current Depth down to its boss (the screens' icon trail);
`js/dungeon-view.js` draws the station fight screen and the main screen's lanes from it, and
`js/pixel-sprites.js` draws placeholder pixel monsters until real art is dropped in (a monster's
`Sprite` can be a picture path).

### Adding a game mode

1. Add a value to `GameModeKind` (`Core/Modes/IGameMode.cs`).
2. Create a class implementing `IGameMode` next to the existing ones.
3. Add it to the `switch` in `GameModes.Create`, and to `GameModes.UsesTeams` if it uses teams.
4. Add tests in `tests/TriviaBattle.Tests/Modes/GameModeTests.cs`.

## What screens receive (the websocket contract)

On every change, the server sends each screen a **full snapshot** (`Core/Snapshots/MatchSnapshot.cs`),
not a diff. A snapshot is only a couple of KB, and a screen that reconnects or joins late is
immediately correct. Each snapshot carries `version`, and screens ignore anything older than
what they have.

Snapshots are built per viewer by `SnapshotBuilder`, which holds all the visibility rules:

- A player screen sees only its own current question (its team's in 2v2 Group), and only while Playing.
- Nobody but admin is told the correct answer to a question still on someone's screen.
- After answering, a player screen sees that question's correct answer ("It was B"): it's behind them.
- The main screen sees scores and *whether* answers were right, never *which* button anyone pressed.

Timers are sent as server-clock deadlines (`phaseEndsAtMs` is the match clock while Playing,
`lockedOutUntilMs` for lockouts). Screens count down locally after correcting for their clock
offset, so the server doesn't send a message every second.

One-off `MatchEvent`s (answer given, lockout ended) are also forwarded so screens can trigger
animations. A screen that misses one still shows the right state.

### SignalR messages (`Server/Hubs`)

| Direction | Name | Payload |
|---|---|---|
| screen → server | `Join(role, roomId, stationNumber)` | returns the current `ScreenState` |
| screen → server | `Ping(clientTimeMs)` | returns server time (clock sync) |
| screen → server | `ServerId()` | changes on every server start; a screen that reconnects to a different one reloads itself |
| screen → server | `ReportError(message)` | the screen's own errors, into the server log and the admin Live log |
| screen → server | `PressKey(key)` | dev only: simulated button |
| server → screen | `State` | `ScreenState { room, station, match, matches, cue }`. Room screens (main, kiosk): `matches` is every game running in the room, in station order. Player screens: `match` is their own game. Empty/null = lobby. `cue` is synced media to play (or null) |
| server → screen | `Event` | `ScreenEvent { type, matchId, phase, ownerId, stationId, placement }`, never contains answers |

Each screen joins one SignalR group (`ScreenGroups`): one per room for the main screen, one per
room for the kiosk, and one per player station. `SignalRMatchBroadcaster` builds a personalised
state for each group whenever a match changes. `ScreenStateFactory` builds every `ScreenState`,
whether a screen is joining or receiving a push, so the two can't disagree.

### Several groups in one room

A match is just a set of stations, so groups can play side by side (a solo on station 1, a trio on
2-4). `MatchHost` only refuses a station that's already in a game. Every `IMatchBroadcaster` gets
all running matches (`runningMatches`), so things the room shares look at the whole room:
- **Main screen:** one game = full layout; two = halves; three or four = quadrants (with three,
  the biggest group gets a whole half). Panels sit on the side of their stations.
- **Kiosk:** never locks; busy stations show their time left. Only "battle in progress" when every
  station is busy.
- **Intro video:** full screen everywhere only when the room was empty; otherwise just that
  group's station screens play it (`ScreenStateFactory.ForRoom` sends no cue to the main screen).
- **Station speakers, dungeon games:** the fight's sounds (`Audio:Dungeon`) instead of the right/wrong
  stingers, at the moment the battle beat shows it; floors, bosses, party combos and revives for the
  whole party.
- **Room speaker:** in-game music while anyone plays, victory only when the last game ends; a
  group's intro and early victory play on its own station speakers.
- **Room lights:** the busiest phase in the room wins (Playing or Continue, then Results, then Intro).

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
  states, and clock sync (`connection.serverNow()`). It reloads the page when the server has
  restarted (so screens never keep running old page code after an update), and sends script errors
  to the server log (`connection.reportError(text)` for anything else).
- Diagnostics (`Server/Diagnostics`): `LiveLog` keeps the last 500 log lines in memory and
  `ScreenRegistry` tracks connected screens, for the admin Live log tab (`GET /api/admin/live-log`).
  Requests the server can't read (an automatic 400) are logged too, so an out-of-date screen shows up.
- `js/ui.js` has the shared helpers: `UI.escape()` (always use it for names), `UI.showView()`, and
  `UI.startCountdowns()`. Countdowns tick on their own from `data-ends-at` attributes, so render
  code just writes deadlines into the HTML.
- The kiosk (`js/kiosk.js`) holds only *form* state (choices not yet submitted). After a match
  starts it resets for the next group straight away; stations in a game show as busy.
- RFID: `js/card-reader.js` assumes a USB keyboard-wedge reader (it types the card id quickly,
  then Enter). A different reader type only needs that file changed.

Answer colors (A red, B blue, C yellow, D green) match the physical button pads and live in
`css/screens.css`.

Dungeon screens: `js/dungeon-view.js` draws the main screen's lanes and hands the station's question view
to `js/dungeon-encounter.js` (`css/dungeon-encounter.css`), laid out like the design lead's example
(`docs/design/station-example.png`): the monster's nameplate and HP, the question scroll hanging from it,
the monster big in its room, the hourglass standing beside the answers (a rune box with the button's
symbol, and a framed answer, each), then you. The main screen has the Host, Q.U.E.R.I.E. (`js/dungeon-host.js`, `css/dungeon-host.css`): a face and a
speech bubble at the foot of the room screen, commenting on each big moment and filling the quiet with
idle quips (`dungeon.host*` in the copy, so a theme or licensed IP can bring its own host).
`dungeon-view.js` also draws the main screen's big moments (banners across the room for a floor
cleared, a boss, a revive, a party combo). During a battle beat the station shows `js/dungeon-battle.js`
instead: the fight full size, animated in `css/dungeon-battle.css`. Its animations are timed from the
beat's start on the server clock (`--t0`), its fade-out from the beat's end (`--t1`), and the next
question's arrival from that same end (`--q0`, `is-after-beat` in `css/dungeon.css`). Short effects
elsewhere are kept with their start time (`DungeonView.effect`), so a redraw halfway through never
restarts or cuts them. `/dev/battle` plays
every beat on a loop with made-up data (`?kind=boss&at=2100` freezes a moment) for tuning the animations.

The fight in 3D (the default; `?view=2d` on a station, or a PC without WebGL 2, gets the CSS version; preview at
`/dev/battle3d`): the art direction is
3D models that read like 2D paintings (Hearthstone-like). `js/battle3d/` draws the room, the fighters and
the effects with three.js (vendored in `lib/three`, found through an import map) on a canvas behind the
station's HTML. The HTML marks where it goes with `data-stage`: `fight` on the beat's `.dg-battle`,
`portrait` on the encounter's `.dg-stage` while a question is up (the monster turns to face you).
The words, damage number, flash, HP bar, the Host's line and the floor curtain stay HTML, so both versions
share them; `css/battle3d.css` hides what the 3D replaces. One file per job: `models.js` (which model and
clips each Sprite key uses; a key with no model stands in the room as its flat picture), `stage.js`
(renderer, camera, lights, room), `actor.js` (one fighter), `effects.js`, `beats.js` (the timelines) and
`battle3d.js` (the entry point). Like the CSS version, every frame is worked out from the time since the
beat's server start (clips are set to a time, never left playing), so a redraw can't put it out of step;
`beats.js` keeps its moments in step with the table at the top of `css/dungeon-battle.css`. The models are
CC0 placeholders (`media/models/CREDITS.md`) until the real art exists.

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

- `config/buttons.json` maps each fixed button id to a station and answer letter:
  `buttonId = 10 + (player - 1) * 4 + answerIndex` for the answer buttons (10-25), and 26-29 for each
  podium's fifth button, the action button (`"Answer": "Action"`, `AnswerIndex` 4 = `ButtonPress.Action`).
  It lights (LED registers 216-219) whenever pressing it would do something (`RoomOutputCoordinator`).
  Keyboard stand-ins: `5` / `T` / `G` / `B`; on a station screen, `5` or Enter.
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
- While Playing, all four of a player's buttons are lit, except during a wrong-answer lockout
  (for the whole team in 2v2 Group).
- At Results the winners' buttons blink.

Outputs only *remember* the wanted state. The PLC output writes it later from its own loop, so
the game never waits on the network.

**Room audio.** All sound is played by the server on the room's StarTech 7.1 card, using
RangerShack's audio engine, ported as-is to `Hardware/Audio` (NAudio):
- `AudioService`: one `MixingSampleProvider` feeding the card; each speaker is a mono zone routed to
  its output. It opens the card exclusive, then shared, then stereo fallback, and skips HDMI outputs.
  When Windows runs the device with fewer than 8 channels (a laptop or headset), it uses RangerShack's
  stereo test mode: every zone in both ears, except attract and sub, which would only double the room
  music out of step.
- `AudioChannel`: a zone with a music loop (decoded once to memory, so looping never seeks a file)
  and an effect on top.
- `Server/Audio/RoomSoundService` is an `IMatchBroadcaster` that maps match events to sounds
  (`RoomSound` per room: stingers as effects on the station zones, music loops on room + sub,
  victory and the intro as effects). It starts once the server is fully up, and only one server
  process per PC plays the audio. The Operator screen has live volume sliders (saved in AppSettings),
  the card's status and a test button per speaker.

Keep `Hardware/Audio` in step with RangerShack rather than reworking it here.

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
- The Insights tab (`GET /api/admin/insights?days=30`) reports the design doc's indicators from the
  match history: time in use (any game running, side-by-side games counted once), matches and seats
  filled, and for card holders (guests can't be followed) visits, average dwell time and how many came
  back on another day. The working-out is `Admin/Insights.cs`, a plain class with its own tests.

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
