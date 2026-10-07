# Themes: how the screens look and what they say

Every screen (main, players, kiosk, leaderboard) has the same layout in every theme. A theme
changes only two things:

| File | Controls | Who edits it |
|---|---|---|
| `wwwroot/themes/<name>/theme.css` | Colours, fonts, corners, shadows, backgrounds, decorations and animations | Designers / front-end |
| `wwwroot/themes/<name>/copy.json` | Every line of text on every screen | Writers |

There's no build step. Edit a file, refresh the screen, and you'll see it.

Current themes:

| Theme | Look | Voice |
|---|---|---|
| `neon` (default) | The room photo: dark, glowing station colours | Plain and friendly. **This is the base copy; every key lives here.** |
| `showcase` | 70s TV game show: sunburst, marquee bulbs, price-tag answers | Cheesy, delighted daytime host |
| `dungeon` | Dungeon game show: stone, torchlight, green "system message" boxes | A sarcastic system-AI host who breaks the fourth wall |

The themes are *inspired by* their genres. Keep real shows' names, logos, catchphrases and
characters out of them, both for trademark/copyright reasons and so the venue owns its own voice.

## Switching and previewing

- **Switch every screen:** Admin → Live → Theme → *Use on all screens*. Screens change instantly, even mid-match.
- **Preview one screen without changing the room:** add `?theme=<name>` to its URL, e.g.
  `/display/main?theme=dungeon`. The admin page has preview links.
- The default for a fresh install is `Display:DefaultTheme` in `appsettings.json`.

## Making a new theme

1. Copy `wwwroot/themes/showcase/` to `wwwroot/themes/<your-name>/` (lowercase, no spaces).
2. In `theme.css`, replace every `showcase` with your theme name. Every rule must be scoped to
   `[data-theme="<your-name>"]`; a test checks this.
3. Change the tokens (below), then add decorations if you like.
4. In `copy.json`, keep only the lines you want to change. Anything you leave out falls back to
   `themes/neon/copy.json`.
5. It appears in the admin theme list automatically.

`dotnet test` checks that every theme's `copy.json` only uses keys that exist.

## theme.css: tokens

The base layout (`wwwroot/css/screens.css`) only ever uses these variables, so changing them
restyles everything:

| Token | What it is |
|---|---|
| `--bg`, `--bg-image` | Page colour, and an optional gradient/image on top of it |
| `--panel`, `--panel-light` | Cards: answer boxes, chips, score rows, banners |
| `--panel-border`, `--panel-shadow` | Frame and shadow on every card (e.g. `0.5vmin solid gold`) |
| `--text`, `--text-dim` | Main and secondary text |
| `--accent`, `--accent-text` | Highlight colour (titles, scores, buttons) and the text on top of it |
| `--correct`, `--wrong` | Right/wrong feedback |
| `--answer-a` … `--answer-d`, `--answer-text` | Answer colours (match the physical buttons) and the A–D letter colour |
| `--font-display`, `--font-body` | Title font and body font |
| `--title-color`, `--title-shadow`, `--title-transform` | Big headings (glow, 3D drop, uppercase…) |
| `--radius`, `--radius-small` | Corner rounding |

Use `vmin`/`vh` units rather than `px`, so the same theme fits a 4K TV and a small portrait monitor.

**Decorations.** Every screen has an empty `<div class="theme-backdrop">` behind the content, for
things like marquee bulbs or torchlight (see `showcase` and `dungeon`). You can also restyle any
class, e.g. `[data-theme="x"] .banner { ... }`.

Classes worth knowing:
- `.big-title`, `.subtitle` (headings)
- `.answer`, `.answer-letter`, `.is-chosen`, `.is-correct`, `.is-dimmed` (answers)
- `.banner`, `.banner.correct`, `.banner.wrong` (the player screen message)
- `.chip`, `.score-row`, `.team-card`, `.high-score`

**Fonts.** Screens may run offline, so don't link web fonts. Put font files in the theme folder and
use `@font-face` with a relative `url()`.

## copy.json: what the screens say

Keys are grouped by the moment they belong to. Open `themes/neon/copy.json` for the full list:

| Group | Moments |
|---|---|
| `brand`, `lobby` | Attract screen, leaderboard titles |
| `modes`, `teams` | Mode names/descriptions, team names (saved with the match) |
| `intro`, `leadIn`, `question` | Match start, "question N", time's up |
| `status` | Main-screen chips: locked in / wrong / no answer |
| `player` | Player screen: press now, locked in, correct, wrong, time's up |
| `standings`, `results` | Scores between questions, winners, high scores |
| `kiosk` | Every prompt and button at the kiosk |

**Variants.** Give a list instead of a string, and one line is picked at random for each moment:
```json
"wrong": ["Ouch.", "Yikes.", "Oof."]
```
The pick stays the same while that moment is on screen, so text doesn't flicker on redraws. Each
player screen gets its own pick.

**Placeholders.** Words in `{braces}` are filled in by the screens, e.g. `{name}`, `{points}`,
`{team}`, `{n}`, `{total}`, `{rank}`. Use only the ones the base copy uses for that key.

Text is always shown as plain text (HTML in copy is displayed literally), so a typo can't break a screen.

## Voice-over (future)

The copy keys are the moments a voice-over would speak at: `intro.title`, `player.correct`,
`results.winner`, and so on. When the AI voice is added, it can use the same keys and the same
`{placeholders}`, with a voice line chosen per key, just like text variants.

The `dungeon` copy is written in the intended voice: tongue-in-cheek, aware it's a host on a
screen in a venue, roasting gently and never punching down. Keep it PG-13 for a family venue.
