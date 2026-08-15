/// <summary>
/// Call from a Unity MonoBehaviour. Sends inputs only — server owns position and combat.
/// </summary>
public sealed class InputSender
{
    private readonly NetClient _net;
    private string _targetId = "monster_slime_1";

    public InputSender(NetClient net)
    {
        _net = net;
    }

    public void SetTarget(string targetId)
    {
        _targetId = targetId;
    }

    public void RequestMove(float x, float y)
    {
        _ = _net.SendMoveAsync(x, y);
    }

    public void CastSlash()
    {
        _ = _net.SendCastAsync("slash", _targetId);
    }

    public void CastShot()
    {
        _ = _net.SendCastAsync("shot", _targetId);
    }

    public void CastMend()
    {
        _ = _net.SendCastAsync("mend", _targetId);
    }

    public void RequestGacha(int count = 1)
    {
        _ = _net.SendGachaAsync("starter", count);
    }
}
