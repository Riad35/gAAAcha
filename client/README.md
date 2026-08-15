# Unity client (not created yet)

Gray-box server is already running the protocol. Create the Unity project locally, then drop the stubs in.

## Create the project

1. Unity Hub → Unity **6.3 LTS**
2. New project → **2D** (side view for characters/enemies)
3. Put the project under `gAAAcha/client/Unity/` or open it from Hub and copy `Stubs/` into `Assets/Scripts/Network/`

Dev default: **PC + keyboard**. Platform (mobile/PC) is still an open P0 in the memory bank.

## Connect

- URL: `ws://127.0.0.1:7777`
- Use `System.Net.WebSockets.ClientWebSocket` (stub) or a Unity WebSocket package later
- Client sends **inputs only**. Never send HP, inventory, or "I am at x,y" as truth.

```
Client → { "type": "request_move", "x": 3, "y": 6 }
Client → { "type": "cast_skill", "skillId": "slash", "targetId": "monster_slime_1" }

Server → { "type": "sync_state", "you": {...}, "players": [...], "monsters": [...] }
Server → { "type": "sync_move", "entityId": "...", "x": 3, "y": 6 }
Server → { "type": "sync_skill", "casterId": "...", "targetId": "...", "skillId": "shot", "damage": 20, "hpAfter": 20, "mpAfter": 42 }
Server → { "type": "error", "code": "out_of_range|on_cooldown|not_enough_mana|too_fast|blocked|rate_limited", "message": "..." }
```

Skills in the slice: `slash`, `shot`, `mend`.

## Suggested input (PC)

- WASD / arrows → `request_move` toward the next tile
- 1 / 2 / 3 → `cast_skill` on current lock-on target
- Click enemy → set `targetId`

Predict the walk locally, then snap if `sync_move` disagrees.
