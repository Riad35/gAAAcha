/** Wall cubes are 1×1 centered on integer tiles. A point is solid only inside that cube, not on the face. */

const HALF = 0.5;
const EPS = 1e-4;

export type CollisionMap = {
  width: number;
  height: number;
  blocked: { x: number; y: number }[];
};

function isWallCell(tx: number, ty: number, map: CollisionMap): boolean {
  if (tx < 0 || ty < 0 || tx >= map.width || ty >= map.height) {
    return true;
  }
  return map.blocked.some((tile) => tile.x === tx && tile.y === ty);
}

/** True if (x,y) is strictly inside a wall cube or the out-of-bounds ring. */
export function isSolidAt(x: number, y: number, map: CollisionMap): boolean {
  const fx = Math.floor(x);
  const fy = Math.floor(y);
  const inset = HALF - EPS;
  for (let dy = 0; dy <= 1; dy += 1) {
    for (let dx = 0; dx <= 1; dx += 1) {
      const tx = fx + dx;
      const ty = fy + dy;
      if (Math.abs(x - tx) >= inset || Math.abs(y - ty) >= inset) {
        continue;
      }
      if (isWallCell(tx, ty, map)) {
        return true;
      }
    }
  }
  return false;
}

/** Push a point out of overlapping wall cubes onto the nearest face. */
export function depenetrate(x: number, y: number, map: CollisionMap): { x: number; y: number } {
  const face = HALF;
  for (let n = 0; n < 4; n += 1) {
    let moved = false;
    const fx = Math.floor(x);
    const fy = Math.floor(y);
    for (let dy = 0; dy <= 1; dy += 1) {
      for (let dx = 0; dx <= 1; dx += 1) {
        const tx = fx + dx;
        const ty = fy + dy;
        if (!isWallCell(tx, ty, map)) {
          continue;
        }
        const ox = x - tx;
        const oy = y - ty;
        if (Math.abs(ox) >= face - EPS || Math.abs(oy) >= face - EPS) {
          continue;
        }
        if (Math.abs(ox) >= Math.abs(oy)) {
          x = tx + Math.sign(ox || 1) * face;
        } else {
          y = ty + Math.sign(oy || 1) * face;
        }
        moved = true;
      }
    }
    if (!moved) {
      break;
    }
  }
  return { x, y };
}

/**
 * Walking into a wall slides on one axis, otherwise stops on the last open point of the step.
 */
export function resolveWalk(
  fromX: number,
  fromY: number,
  toX: number,
  toY: number,
  map: CollisionMap,
): { x: number; y: number } {
  if (!isSolidAt(toX, toY, map)) {
    return { x: toX, y: toY };
  }
  if (toX !== fromX && !isSolidAt(toX, fromY, map)) {
    return { x: toX, y: fromY };
  }
  if (toY !== fromY && !isSolidAt(fromX, toY, map)) {
    return { x: fromX, y: toY };
  }

  let ax = fromX;
  let ay = fromY;
  let bx = toX;
  let by = toY;
  if (isSolidAt(ax, ay, map)) {
    const pushed = depenetrate(ax, ay, map);
    ax = pushed.x;
    ay = pushed.y;
  }
  for (let i = 0; i < 14; i += 1) {
    const mx = (ax + bx) * 0.5;
    const my = (ay + by) * 0.5;
    if (isSolidAt(mx, my, map)) {
      bx = mx;
      by = my;
    } else {
      ax = mx;
      ay = my;
    }
  }
  return { x: ax, y: ay };
}
