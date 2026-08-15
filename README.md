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
npm test
npm run test:client
```

Server listens on `ws://127.0.0.1:7777`.

## Layout

- `server/` — TypeScript game server (`request_*` / `sync_*`)
- `server/db/schema.sql` — PostgreSQL 17 contract
- `server/.runtime/saves/` — guest file persist (pity/inventory/position) until Postgres is wired
- `client/Unity/gatcha1/` — Unity 6 gray-box client
- `client/Stubs/` — source copies of network scripts

Do not push until you ask for it. Rules/memory-bank stay in the `gatcha` / Cursor_7rules repo.
