import { startServer } from "./net.js";

startServer(Number(process.env.PORT) || 7777);
