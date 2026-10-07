# PLC protocol (Trivia Battle ⇄ Productivity PLC)

This is what the PLC program has to do, and what the game server does on its side. It's written
for the PLC programmer. The register numbers are **placeholders**: they live in
`src/TriviaBattle.Server/config/plc.json` and can be changed there without touching any code.

- Transport: Modbus TCP, PLC is the server, game server is the client. Default port 502, unit id 1.
- All values are **16-bit holding registers**. Addresses below are 0-based offsets (offset 0 = 400001).
- Every number that counts up **wraps from 65535 to 0**. Both sides use wrap-safe subtraction.

## Register map (placeholders)

| Offset | Direction | Name | Meaning |
|---|---|---|---|
| 0 | PLC → server | Event counter | Total presses latched since power-up (wraps). |
| 1 | PLC → server | PLC clock | Free-running tick counter (wraps). 1 tick = `ClockTickMs` (default 1 ms). |
| 10–73 | PLC → server | Event ring buffer | 32 slots × 2 registers: `[button code, timestamp]`. |
| 100 | server → PLC | Ack | The event counter value the server has processed up to. |
| 101 | server → PLC | Heartbeat | Server adds 1 every second. If it stops changing, the server is down. |
| 200–215 | server → PLC | Button LEDs | One per button (button ids 10–25). `0` off, `1` on, `2` blink. |
| 220 | server → PLC | Lighting cue, room A | `0` Idle, `1` Intro, `2` Question, `3` Reveal, `4` Standings, `5` Results. |

The event counter and PLC clock **must be adjacent** (clock = counter + 1), so one read gets both
from the same scan.

## Buttons

Each physical button has a fixed **button id** (`config/buttons.json`):
`buttonId = 10 + (player − 1) × 4 + answer` (answer A=0 … D=3), so Player 1 is 10–13, Player 2 is
14–17, Player 3 is 18–21 and Player 4 is 22–25.

In an event slot the PLC writes a **button code**. `Plc:ButtonCodes` maps button id → code, and
for now code = button id. If the PLC numbers its inputs differently, change the codes in config,
not the PLC logic.

## What the PLC must do

### On every button press (rising edge, debounced)

1. Read the event counter `C` and the ack `A`.
2. If `(C − A) mod 65536 ≥ 32` (the slot count), the buffer is full: **drop the press**. Never
   overwrite a slot the server hasn't acked.
3. Otherwise:
   - `N = C + 1` (wrapping)
   - `slot = (N − 1) mod 32`
   - write the button code to `10 + slot×2`
   - write the current PLC clock to `10 + slot×2 + 1`
   - **then** write `N` to the event counter. Write the counter last, so the server never reads a
     half-written slot.
4. If several buttons go down in the same scan, latch them one after another in a consistent order.
   **Latch order is the official press order**: it decides who buzzed first.

The slot count must be a power of two (32 by default). That keeps `(N − 1) mod 32` continuous
when the counter wraps.

### Every scan

- Update the PLC clock register.
- Drive each button's LED from its LED register (0 off, 1 on, 2 blink at whatever rate looks
  good).
- Apply the room's lighting scene from the lighting cue register. What each scene looks like is up
  to the lighting design.
- Optional: if the heartbeat hasn't changed for ~5 seconds, go to a safe "attract" scene and turn
  the button LEDs off.

## What the server does

`src/TriviaBattle.Hardware/Plc/PlcPollingService.cs` runs one loop, about every 10 ms:

1. Read registers 0–1: the event counter and the PLC clock.
2. On the first read after connecting, write the counter to Ack and ignore older presses.
3. If the counter moved, read the whole ring and take the new slots, oldest first:
   - Each slot's code is translated to a button id.
   - The press time is `now − (PLC clock − slot timestamp) × ClockTickMs`.
4. Write the counter to **Ack**.
5. Write any LED / lighting registers that changed. After a reconnect it writes all of them.
6. Increment **Heartbeat** once a second.

On any error the server disconnects, waits `ReconnectDelayMs` (2 s) and tries again, forever.
`GET /api/hardware/status` shows whether the PLC is connected and when the last press arrived.

Fairness comes from the PLC: the order and timestamps of presses are set at latch time, so the
server's polling rate and network delays don't affect who was first.

## Testing without the PLC

`tools/PlcSimulator` behaves exactly as described above. It's also a working reference
implementation (see `SimulatedPlc.Press`).

```powershell
dotnet run --project tools/PlcSimulator                 # terminal 1: simulated PLC on port 5020
cd src/TriviaBattle.Server
$env:Plc__Enabled="true"; $env:Plc__Host="127.0.0.1"; $env:Plc__Port="5020"
dotnet run                                              # terminal 2: game server using the PLC
```

Press `1234` / `QWER` / `ASDF` / `ZXCV` in the simulator window to press buttons, and watch the
LEDs and lighting cue change as the game runs.

## Open items for the real register map

- Final register offsets, and whether the event counter and clock really are 16-bit.
  Productivity tags are often 32-bit, and each one would then take two registers.
- The PLC clock tick length (`ClockTickMs`).
- Button input numbering (`ButtonCodes`) and LED output addresses (`LedRegisters`).
- Lighting scenes: what each cue number should look like.
