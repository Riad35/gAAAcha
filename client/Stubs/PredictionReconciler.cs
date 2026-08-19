using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Isolated client prediction / reconciliation.
/// Buffer predicted actions by request id; confirm or correct when server state arrives.
/// Do not scatter correction logic through gameplay code — route through this module.
/// </summary>
public sealed class PredictionReconciler
{
    public struct PredictedAction
    {
        public string RequestId;
        public string Kind;
        public string TargetId;
        public int PredictedHpAfter;
        public float PredictedX;
        public float PredictedY;
        public float IssuedAt;
    }

    public struct Correction
    {
        public string EntityId;
        public int? Hp;
        public int? MaxHp;
        public float? X;
        public float? Y;
        public bool Hard;
    }

    private const float SoftPosThreshold = 0.75f;
    private const float HistorySeconds = 2.5f;
    private readonly List<PredictedAction> _history = new List<PredictedAction>();
    private int _seq;

    public string NextRequestId(string prefix = "p")
    {
        _seq += 1;
        return prefix + "_" + _seq;
    }

    public void Predict(PredictedAction action)
    {
        action.IssuedAt = Time.realtimeSinceStartup;
        if (string.IsNullOrEmpty(action.RequestId))
        {
            action.RequestId = NextRequestId();
        }

        _history.Add(action);
        Prune();
    }

    /// <summary>
    /// Match server outcome to a prediction. Returns null if no correction needed.
    /// </summary>
    public Correction? ReconcileSkill(
        string requestId,
        string targetId,
        int hpAfter,
        float? x = null,
        float? y = null)
    {
        Prune();
        var idx = FindIndex(requestId);
        if (idx < 0)
        {
            // Unmatched server truth — soft-correct if we have a target id.
            if (string.IsNullOrEmpty(targetId))
            {
                return null;
            }

            return new Correction
            {
                EntityId = targetId,
                Hp = hpAfter,
                X = x,
                Y = y,
                Hard = false,
            };
        }

        var pred = _history[idx];
        _history.RemoveAt(idx);

        var hpMatch = pred.PredictedHpAfter <= 0
            ? hpAfter <= 0
            : Mathf.Abs(pred.PredictedHpAfter - hpAfter) <= 1;
        var posMatch = true;
        if (x.HasValue && y.HasValue &&
            (Mathf.Abs(pred.PredictedX) > 1e-4f || Mathf.Abs(pred.PredictedY) > 1e-4f))
        {
            var d = Vector2.Distance(
                new Vector2(pred.PredictedX, pred.PredictedY),
                new Vector2(x.Value, y.Value));
            posMatch = d <= SoftPosThreshold;
        }

        if (hpMatch && posMatch)
        {
            return null; // confirmed — no visible correction
        }

        var hard = (pred.PredictedHpAfter > 0 && hpAfter <= 0)
            || (pred.PredictedHpAfter <= 0 && hpAfter > 0);
        return new Correction
        {
            EntityId = string.IsNullOrEmpty(targetId) ? pred.TargetId : targetId,
            Hp = hpAfter,
            X = x,
            Y = y,
            Hard = hard,
        };
    }

    public Correction? ReconcileMove(string entityId, float serverX, float serverY, float clientX, float clientY)
    {
        var d = Vector2.Distance(new Vector2(clientX, clientY), new Vector2(serverX, serverY));
        if (d < 0.12f)
        {
            return null;
        }

        return new Correction
        {
            EntityId = entityId,
            X = serverX,
            Y = serverY,
            Hard = d > SoftPosThreshold * 2f,
        };
    }

    private int FindIndex(string requestId)
    {
        if (string.IsNullOrEmpty(requestId))
        {
            return -1;
        }

        for (var i = _history.Count - 1; i >= 0; i--)
        {
            if (_history[i].RequestId == requestId)
            {
                return i;
            }
        }

        return -1;
    }

    private void Prune()
    {
        var cutoff = Time.realtimeSinceStartup - HistorySeconds;
        for (var i = _history.Count - 1; i >= 0; i--)
        {
            if (_history[i].IssuedAt < cutoff)
            {
                _history.RemoveAt(i);
            }
        }
    }
}
