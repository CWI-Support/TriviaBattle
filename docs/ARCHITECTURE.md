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
         Continue?   Dungeon games: the clock ran out; a swipe (a second paid session) goes back to Playing
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
  `Delve`: its floor, its room and the monsters in it (`Delve.Foes`, each a `Foe` with its own HP and poison).
  A room holds one monster or a pack (`FloorDef.Rooms` in `config/dungeon.json`); the party fights the one in
  front (`Delve.Encounter`), whose hourglass and attacks count, and the others wait their turn. When the one in
  front dies the next in the room steps up; an empty room leads to the next. Parties race each other down.
- Each answer owner is an **`Adventurer`** with their own HP, streak, hourglass and hero class. Every
  adventurer in a party hits the same monster. In 2v2 Group teammates share their question, so the team is one
  adventurer (fighting as the first teammate's hero); in 2v2 Combined each teammate is an adventurer on the
  team's delve.

The rules:
- A correct answer is `DungeonRun.Strike`: damage by difficulty and a crit if fast (within
  `CritWithinSeconds`, a set 3 seconds), taken off the monster in front. The answer's points are what it did
  (`DealtHit.TotalDamage` plus any HP healed), so scores, results and leaderboards work unchanged.
- **Class stats** (`ClassStats`, one per class in `ClassSettings`, `StatsFor`): `Hp` (of `AdventurerHp`), `DamageDealt` (of a right
  answer's damage, before crits and combos; a Wizard's splash follows it) and `DamageTaken` (of a monster's hit), all shares
  where 1 is normal. Every class is tuned the same way; floor rewards (below) raise them. Monsters
  hit harder on each loop through the dungeon (`LoopDamageBonus`, beside `LoopHpBonus`): an `Encounter` carries its
  `MaxHp` and `Damage` for its floor.
- **Hero classes** (`HeroClass`, numbers in `ClassSettings`, all in `DungeonRun`): the Rogue's crits use
  `RogueCritMultiplier`; the Tank's fast window is `TankFastSeconds` and it takes `Tank.DamageTaken` of a hit; the
  Healer's fast answer heals the standing party member lowest on HP (`DealtHit.HealedOwnerId`), or with nobody hurt
  adds a poison stack to the monster in front (`Delve.Poison`; `DungeonRun.TickPoison` does its damage every
  tick, raising `MonsterPoisoned`, and a poison kill is a kill but scores nothing: `DungeonRun.PartyKilled` gives the party its kill
  beat, as for a teammate's kill (`Adventurer.WatchedKill`; the snapshot's `BeatView.ByPoison`, with the killing `BeatView.Hit`),
  and their question comes back after it); the Wizard's fast answer splashes
  every other monster in the room (`DealtHit.Splashes`); the Pugilist is the only class with a combo
  (`DealtHit.Combo`, `ComboCount`); the Warlock's patron decides once per question (`PlanReveal`, when the hourglass
  turns over) whether to show the answer when the hourglass is down to `WarlockRevealSecondsLeft`
  (`DungeonRun.RevealsDue`, raising `PatronRevealed`; the snapshot sends `QuestionView.Revealed` to that screen
  only), and any answer after `WarlockSlowSeconds` costs `WarlockSlowDamage` HP (`PatronPrice`, never downing
  them); the Ranger's screen gets `QuestionView.PeekFromMs`, `RangerPeekSeconds` before the beat ends (only what
  it shows changes: presses before the question is up are ignored as always). A hero's key is its class name
  (`Heroes.ClassOf`); a kiosk game plays the default, the Tank.
- A wrong answer, or the adventurer's hourglass running out (`Tick`), is `DungeonRun.TakeHit`. A timeout
  is recorded as a `GivenAnswer` with `TimedOut` (no press) and the question moves on.
- At 0 HP an adventurer is downed: their stream is locked out, then they get up at full HP. On their own
  that's `DownedSeconds`. In a party of two or more they wait for their teammates: `ReviveAnswers` right
  answers from them (counted on whoever has been down longest) revive them at once, with
  `PartyDownedSeconds` as the safety net. When the whole party is down, the monster heals to full and
  everyone gets up after `DownedSeconds`.
- **Lives** (`DungeonSettings.Lives`, 3; 0 = no limit): each downing costs one (`Adventurer.Lives`). Downed on the last,
  `Adventurer.IsOut`: `DownedUntil` is `DateTimeOffset.MaxValue` (their buttons dead for good), `GetUp` does nothing, and
  revives, Potions, the whole-party-down get-up, mini games and rewards all pass them by; `TakenHit.Out` marks that hit.
  In a party they still see a teammate's question (`YouView.Helping`). When `DungeonRun.EveryoneOut`, `MatchEngine.Tick`
  ends Playing whatever the clock says ("Continue?"), and `Continue` calls `DungeonRun.RestoreLives` (lives back, the out
  back up, their lockout ended). Snapshots: `DelveView.Lives`/`MaxLives`/`Out`, `PartyMemberView.Lives`/`Out`.
- **Party combo:** when every other standing member of the party has answered right within
  `PartyComboSeconds`, the hit that completes it does `PartyComboBonus` more damage
  (`DealtHit.PartyCombo`). Then everyone needs a fresh right answer for the next one.
- The hourglass only runs while their buttons work: it stops during a lockout and restarts after.
- **The clock.** Every Dungeon game, solo or group, runs on an arcade clock, one per match: a paid session
  starts at `ClockStartSeconds` and every kill adds time (`DungeonRun.SecondsEarnedBy`: `SecondsPerKill`, or
  `SecondsPerBoss` for a boss; a Wizard's splash kills count), so the better they play, the longer they play.
  `MatchEngine` moves `PhaseEndsAt` back, with no cap: a session lasts until the clock runs out. Time per kill
  is scaled two ways: a party faces monsters with `PartyHpPerMember` more HP per extra member
  (`DungeonRun.HpScaleFor`, applied by `Delve`), so its kills earn `HpScaleFor(n) / n` of the time; and parties
  racing on one clock each earn their share of it (divided by the number of parties). The defaults aim at 3-4
  minutes for an average player, about two bosses; `DungeonPacingTests` plays simulated games on the shipped
  `config/dungeon.json` to keep it there (run it after changing the content or the clock). `MatchEngine.RoundSeconds`
  is the starting length, `EarnsTime` says it's the arcade clock (Classic plays a fixed `MatchSeconds`); the
  snapshot carries both (`MatchSeconds`, `EarnsTime`), and screens pop "+10s" when the end time moves.
- **Continue?** When the clock runs out, the match goes to `MatchPhase.Continue` for `ContinueCountdownSeconds`
  instead of Results. `MatchEngine.Continue(now)` (a card swipe at any station in the match: a second paid
  session) goes back to Playing for everyone on a fresh `ContinueSeconds` clock, same floor, same HP, the same
  question shown again; no swipe and the countdown runs into Results. Each continue is a session of its own, so
  there's no limit on how many. `MatchHost.ContinueAtStation` is what the station
  card readers call (`POST /api/stations/{id}/swipe`), as does `POST /api/dev/continue/{stationId}` (the dev buttons
  page) and the Operator screen's "Give a continue" (`POST /api/matches/{id}/continue`). The clock and continue
  numbers are Operator-screen rules (`GameRulesService`).
- **Floors made up per match** (`DungeonPlan`). `config/dungeon.json` is loaded as a plan; a floor written with
  `RoomCount`, `RoomHp`, `Pool` and `Boss` is laid out fresh for each match by `DungeonPlan.Build` (`MatchHost`
  calls it per match, so every party in a match fights the same rooms): `RoomCount` rooms, each filled at random
  from the pool to about `RoomHp` (a monster that would take the room more than a quarter over isn't picked, up to
  `MaxPack` monsters), then the boss's room. The result is a plain `DungeonMap`; floors written out room by room
  still work.
- **Bosses.** A made-up floor ends in its boss's room. The kill that empties the room before it is marked
  `DealtHit.BossAhead`: the beat is `boss` (the boss's entrance, with `FloorBeatSeconds`), the room hears the boss
  sound and the main screen shows its banner. (A floor cleared, `FloorCleared`, is the boss's own death, which leads
  to the next floor.) Against a boss each adventurer's questions go round a **charge cycle** of `BossChargeSteps`
  (`Adventurer.ChargeAgainst` / `ChargeUp`, per adventurer, since each has their own questions): its hits build from
  `BossWeakHit` to normal (`DungeonSettings.BossHitAt`), and on the last step, full charge, a miss or a timeout takes
  `BossChargedHit` of its damage (`TakenHit.Charged`) while a right answer staggers it for `StaggerBonus` more
  (`DealtHit.Staggered`). Any answer then starts the cycle over. The snapshot carries the step for the question now
  (`DelveView.BossCharge`, of `BossChargeSteps`; 0 steps = not fighting a boss) so the screens can warn of the blow.
- **Floor rewards** (`Reward`, numbers in `RewardSettings`, `"Game:Dungeon:Rewards"`). Every answer counts towards the
  adventurer's `FloorTrial` for the floor they're on (`Adventurer.CountAnswer`, from `Strike` and `TakeHit`: timeouts
  count as misses). When a floor clears (`DealtHit.FloorCleared`, from an answer or the Healer's poison),
  `MatchEngine` calls `DungeonRun.OpenRewards`: each party member at or over `Accuracy` gets a `RewardOffer` of
  `Choices` random rewards (`RewardOffered`), opening when the boss's death beat ends. While `Adventurer.IsChoosing`
  their hourglass is stopped (`StartHourglass` won't run it), the snapshot has no question but `YouView.Reward`, and
  `HandlePress` sends answer buttons to `ChooseReward` (after `PickDelaySeconds`); at `EndsAt` the tick picks one at
  random. A pick (`RewardChosen`) goes on `Adventurer.Rewards`, and the question comes back after a `reward` beat
  (`RewardSettings.BeatSeconds`). The arcade clock stands still meanwhile (see Mini games: `IsAway`, `HoldTheClock`).
  Rewards are read live where the numbers are used (`DamageShare`, `DamageTakenShare`, `FastSecondsOf`, and each class
  power's `...Of`); only Vitality (max HP) and Last Stand (a count, used in `Adventurer.TakeHit`) change state.
  `RewardAmount` is the number each card shows. Screens: `js/dungeon-rewards.js`, `css/dungeon-rewards.css`.
- **Mini games** (`Core/Dungeon/Minigames`, numbers in `MinigameSettings`, `"Game:Dungeon:Minigames"`). `DungeonMap.MinigameOn`
  rolls every floor reached for one by `MinigameChance` (config/dungeon.json), loops included (a loop repeats a floor's
  rooms, not its mini game): its own `Random` from the match's seed and the floor number, so every party meets the same
  ones; after `MinigameEvery` (3) less one floors without one, the next floor that can hold one has one whatever its roll. The kind comes from `MinigameCatalog.Pick` (every kind as likely) and it follows a random room, never the one
  before the boss's. A floor written with `FloorDef.Minigame`/`MinigameAfterRoom` always has that one (tests use it). When that room empties,
  `Delve.ClearTheDead` marks the kill `DealtHit.MinigameAhead`, and `MatchEngine` calls `DungeonRun.StartMinigames`: each
  party member not already away gets their own `Minigame` (`MinigameStarted`), its intro once the kill beat ends. A
  `Minigame` keeps the timing every game shares (`IntroAt`, `PlaysFrom`, `EndsAt`, `FinishedAt`, `ResultEndsAt`); each
  game is a subclass with its own rules for a press, all timed on the server and only animated by the screen from the
  same numbers: `RuneLock` (the needle's angle from the press time and the turn it's on), `RuneEcho` (a "Simon says":
  the round's runes shown from `ShowFrom`, presses from `InputFrom`) and `ChestShuffle` (a shell game: the round's
  `Swaps`, picks from `PickFrom`; the action button is the fifth chest, so `HandlePress` sends it to a mini game before
  a power-up; `MinigamePress.Lost` is a press that loses it). `HandlePress` sends
  answer buttons to `PlayMinigame` (`MinigamePlayed`); the tick's `MinigamesDue` loses those out of time (`MinigameLost`)
  and ends those whose result has shown (`MinigameOver`): a winner gets a reward offer (`RewardOffer.ForMinigame`), the
  others their question back. **The clock:** while `Adventurer.IsAway` (a mini game, or a reward to pick) for anyone,
  `DungeonRun.ClockStopped` is true and `MatchEngine.HoldTheClock` pushes `PhaseEndsAt` back every tick, so everything
  that checks the clock works as normal; `ClockHeldFor` adds it up. Snapshots: `YouView.Minigame` (`MinigameView` with
  each game's part, `RuneLockView`), `DelveView.Minigame`/`MinigamesWon` for the main screen, and
  `MatchSnapshot.ClockStoppedLeftMs`/`ClockHeldMs` (`UI.matchClock` shows a stopped clock; the "+10s" pop leaves the held
  time out). Screens: `js/dungeon-minigames.js`, `css/dungeon-minigames.css`; sounds in `RoomSoundService`.
- **Each floor's questions.** A floor's `Questions` weights (`{ "Easy": 80, "Medium": 20 }`) pick each next
  question's difficulty (`DungeonRun.NextDifficulty`, by the adventurer's floor); `QuestionStream.Record` takes it
  and gives the first question of that difficulty not yet asked (any, if none is left), and `QuestionStream.Prefer`
  picks the first question. Harder questions also hit harder (`DungeonSettings.DamageFor`), so a deeper floor's
  tougher monsters come with bigger blows. Without weights the match's chosen difficulty comes first, as in Classic
  (the kiosk's difficulty only matters there now).
- **Power-ups** (`PowerUp`): Potion, Fireball, Freeze, 50/50, Shield, Time Crystal. Each
  adventurer holds one (`Adventurer.Held`). A kill drops one `PowerUpDropChance` of the time (a boss
  always) and every `PowerUpStreak` right answers in a row earn one, only with empty hands
  (`DungeonRun.FindPowerUp`; it's recorded on the hit as `DealtHit.Found`). The action button (the fifth
  button, `ButtonPress.Action`) uses it: `MatchEngine.HandlePress` hands it to `DungeonRun.UsePowerUp`. A
  Potion goes to whoever in the party needs it most (a downed teammate gets straight up); a Fireball
  multiplies the next right answer (`FireballMultiplier`); a Freeze holds the hourglass (`FreezeSeconds`,
  then it starts over); a 50/50 hides two wrong answers of the current question, which the snapshot
  sends as `QuestionView.Hidden` (wrong ones only) and presses on them are ignored; a Shield blocks the
  next hit (`TakenHit.Blocked`, no damage, the streak survives); a Time Crystal adds `TimeCrystalSeconds`
  to the clock. The screens show the slot, the armed effects and a pop per use (`js/dungeon-powerups.js`).
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
  screen saw it and never holds up your question. `/dev/battle3d?team=pugilist,wizard` and `?cameo` preview it.
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
  fight (`DungeonRun.BeatSecondsAfter`: `HitBeatSeconds`, `HurtBeatSeconds`, `KillBeatSeconds`, `RoomBeatSeconds`,
  `FloorBeatSeconds`). It's simply the next question's `CurrentShownAt` moved later: presses before it
  are ignored like any press before a question appears, and the hourglass starts when it shows. The
  clocks keep running (time per kill and the round length allow for it). `YouView.Beat` tells the
  station what to play (`hit`, `kill`, `floor`, `boss`, `hurt`, `downed`) and from when to when; hits
  carry what the scene needs to draw them (`DealtHit.MonsterSprite`, `MonsterHpLeft`, `TakenHit.HpLeft`).
  In a party every kill is played on every screen together: `DungeonRun.PartyKilled` gives each standing teammate
  not away the killer's kill beat from the same moment (`Adventurer.WatchedKill`), and their question comes back
  after it, fresh, for the monster that's up now. Their snapshot's `BeatView` carries the killing `Hit` and who
  landed it (`By`, a name; or `ByPoison`). Without it the next monster swapped in under a teammate's question
  while the killer's screen was still playing the death. A kill beat ends by announcing what's next
  (`DungeonBattle.upNext`): NEXT UP and the next monster's bar, or ROOM 3 OF 4 when the kill emptied the room.
  That kill (`DealtHit.RoomCleared`, with no boss or mini game ahead) gets the longer `RoomBeatSeconds` (3 s): the
  walk (`DungeonBattle.walks`), where the hero runs on and the dungeon scrolls past until the next room's monsters
  are in front, so the question comes back with the new fight already there.

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

**Station screens are portrait only, 9:16** (a standard monitor stood on its side, 1080 x 1920).
`/display/player/{n}` serves `screens/portrait-frame.html`, which shows the real page
(`/display/player/{n}/screen`, same query) in an iframe of exactly that shape, as big as the window
allows, with black bars around it (`js/portrait-frame.js`). Inside the frame `vw`/`vh`/`vmin` and
`window.innerWidth` are the frame's, so the station CSS and the 3D stage work unchanged and always lay out
as they will at the venue, whatever monitor a developer tests on. That's why it's a frame and not CSS: the
station styles use viewport units throughout, and those always measure the window. `/dev/battle` and
`/dev/battle3d` use the same frame. Two things follow: a link that should leave the station page needs
`target="_top"`, and the https switch for cameras moves the whole page (`HeroCamera.goSecure`), since a
framed page only counts as secure when the page around it is. Write station CSS for portrait only.

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

Packs and hero classes on the 2D screens (all drawn from the snapshot, no rules): a pack's waiting monsters
(`Delve.Foes` after the first) stand small behind the one in front in a `.dg-pack` (the 3D scene hides it and
draws its own), and their strip of mini HP bars (`DungeonView.packStrip`) shows in both versions, under the
nameplate on the station and in the corner of a main-screen lane. The trail groups a pack's monsters as one
room and greys out the room's first (room's monsters - `foes.length`) slots. Poisoned monsters get a green
skull badge and a green HP bar everywhere. In the beat, each class's extra is a note in a fixed spot under the
damage number, so it reads over the 3D scene too (`DungeonBattle.notes`: a Wizard's SPLASH, a Healer's heal or
POISONED, a Warlock's patron's price, a Tank's ARMOUR); a heal also flashes the healed member's HP bar, and
reaches a healed teammate's screen as a feed caption. The Pugilist's stamp counts `ComboCount`. A Warlock's
revealed answer glows purple and the Host's line becomes the patron's whisper. A Ranger's next question slides
in over the end of the beat from `PeekFromMs` (`DungeonBattle.peek`, timed by `--p0`; `player-screen.js` also
redraws then), greyed until `ShownAtMs`. The plate shows the player's class (`DungeonView.classBadge`).

The fight in 3D (the default; `?view=2d` on a station, or a PC without WebGL 2, gets the CSS version; preview at
`/dev/battle3d`): the art direction is
3D models that read like 2D paintings (Hearthstone-like). `js/battle3d/` draws the room, the fighters and
the effects with three.js (vendored in `lib/three`, found through an import map) on a canvas behind the
station's HTML. The HTML marks where it goes with `data-stage`: `fight` on the beat's `.dg-battle`,
`portrait` on the encounter's `.dg-stage` while a question is up (the monster turns to face you). Both fill
the station's `.dg-world`, everything above the player plate, edge to edge, and the station's words float over
the scene (the encounter's `.dg-hud`; the beat's top line). The element marked `data-focus` is the gap they leave
(`.dg-window` between the question scroll and the answers; the beat's `.dg-arena`): `Stage.focus()` measures it and
`aim()` shifts the camera so the monster, or the fight, lands there.
The words, damage number, flash, HP bar, the Host's line and the floor curtain stay HTML, so both versions
share them; `css/battle3d.css` hides what the 3D replaces. One file per job: `models.js` (which model and
clips each Sprite key uses; a key with no model stands in the room as its flat picture), `stage.js`
(renderer, camera, lights, room), `actor.js` (one fighter), `pack.js` (the monsters waiting in the room:
their spots, gliding up one when one ahead leaves, the next stepping up to the front after a kill),
`effects.js`, `beats.js` (the timelines) and `battle3d.js` (the entry point). Like the CSS version, every frame
is worked out from the time since the beat's server start (clips are set to a time, never left playing), so a
redraw can't put it out of step; `beats.js` keeps its moments in step with the table at the top of
`css/dungeon-battle.css`. During a beat the snapshot is already after the hit, so `packView` (battle3d.js)
works the pack back to how it stood when the blow landed. The hero classes show in 3D too: a Wizard's
splash sends bolts to every waiting monster (landing at the 190 ms impact), poisoned monsters glow green with
bubbles by their stacks, a heal sparkles on whoever it healed (you, or a teammate in `others.js`), the
Warlock's price drains purple from the hero, a Tank blocks (`Block_Hit`) instead of reeling, and the
Pugilist's swing grows with `comboCount`; a poison kill's beat (`byPoison`) has no blow: the monster chokes green and bursts
(`poisonKills`); a teammate's kill (`beat.by`) is the usual kill with your knight staying home to cheer while
theirs strikes (`others.js`). The walk: the room is a corridor of one stretch repeated (`ROOM_LENGTH`,
`models.js`: three stretches, in one group, `Stage.scrollRoom`). From 1150 to 2550 ms the corridor scrolls left by
exactly one stretch while the heroes run in place (`walkPose`, their `run` clip). The next room's monster
(`Battle.arrivalOf`, loaded at the beat's start) and its pack stand in their spots, still as far off as the
corridor has left to go. At the end the corridor goes back to 0, which looks the same, and that monster is handed
over as the one in front. Moments outside a beat (a poison kill on a screen in a longer beat, a teammate's heal) are
timed from when the screen saw them, as `others.js` does. The models are CC0 placeholders
(`media/models/CREDITS.md`) until the real art exists.

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
