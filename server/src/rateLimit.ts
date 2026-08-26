/** Move/cast have their own buckets. Everything else shares this meta limiter. */
export const META_RPC_PER_SEC = 8;

export const META_RPC_EXEMPT = new Set<string>(["request_move", "cast_skill", "request_ping"]);

export function metaRpcLimited(
  session: { rpcTimes: number[] },
  type: string,
  now: number,
  maxPerSec = META_RPC_PER_SEC,
): boolean {
  if (META_RPC_EXEMPT.has(type)) {
    return false;
  }
  session.rpcTimes = session.rpcTimes.filter((t) => now - t < 1000);
  if (session.rpcTimes.length >= maxPerSec) {
    return true;
  }
  session.rpcTimes.push(now);
  return false;
}
