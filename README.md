# gAAAcha

Gray-box slice: Node WebSocket server is the source of truth. Unity client comes after.

## Quick start

```bash
cd server
npm install
npm run dev
```

In a second terminal:

```bash
cd server
npm run test:client
```

Server listens on `ws://127.0.0.1:7777`.

## Layout

- `server/` — TypeScript game server (`request_*` / `sync_*`)
- `client/` — Unity 6.3 notes and C# stubs (no Editor project yet)

Do not push until you ask for it. Rules/memory-bank stay in the `gatcha` / Cursor_7rules repo.
