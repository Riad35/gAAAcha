using System.Globalization;
using System.Text;

/// <summary>
/// Sends inputs only — server owns position and combat.
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

    public void Cast(
        string skillId,
        string targetId = null,
        float? aimDx = null,
        float? aimDy = null,
        float? aimX = null,
        float? aimY = null)
    {
        var tid = targetId ?? _targetId ?? "";
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(128);
        sb.Append("{\"type\":\"cast_skill\",\"skillId\":\"").Append(skillId)
            .Append("\",\"targetId\":\"").Append(tid).Append('"');
        if (aimDx.HasValue)
        {
            sb.Append(",\"aimDx\":").Append(aimDx.Value.ToString(inv));
        }

        if (aimDy.HasValue)
        {
            sb.Append(",\"aimDy\":").Append(aimDy.Value.ToString(inv));
        }

        if (aimX.HasValue)
        {
            sb.Append(",\"aimX\":").Append(aimX.Value.ToString(inv));
        }

        if (aimY.HasValue)
        {
            sb.Append(",\"aimY\":").Append(aimY.Value.ToString(inv));
        }

        sb.Append('}');
        _ = _net.SendRawAsync(sb.ToString());
    }

    public void CastAuto() => Cast("auto_attack");
    public void CastSlash() => Cast("slash");
    public void CastShot() => Cast("shot");
    public void CastMend() => Cast("mend");
    public void CastDash() => Cast("dash");
    public void CastStun() => Cast("stun_bolt");
    public void CastDot() => Cast("ember_dot");
    public void CastBuff() => Cast("war_cry");
    public void CastShove() => Cast("shove");
    public void CastPull() => Cast("pull");
    public void CastBlind() => Cast("blind_dust");
    public void CastIronStance() => Cast("iron_stance");
    public void CastShockwave() => Cast("shockwave");
    public void CastPowerChant() => Cast("power_chant");
    public void CastHaste() => Cast("haste");
    public void CastBarrier() => Cast("barrier");
    public void CastWard() => Cast("ward");
    public void CastElementalFocus() => Cast("elemental_focus");

    public void RequestGacha(int count = 1)
    {
        _ = _net.SendGachaAsync("starter", count);
    }

    public void EquipWeapon(string weaponId)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_equip\",\"weaponId\":\"" + weaponId + "\"}");
    }

    public void EquipSpirit(string spiritIdOrNull)
    {
        if (string.IsNullOrEmpty(spiritIdOrNull))
        {
            _ = _net.SendRawAsync("{\"type\":\"request_equip\",\"spiritId\":null}");
            return;
        }

        _ = _net.SendRawAsync("{\"type\":\"request_equip\",\"spiritId\":\"" + spiritIdOrNull + "\"}");
    }

    public void RequestInspect(string targetId)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_inspect\",\"targetId\":\"" + targetId + "\"}");
    }

    public void RequestChat(string channel, string text, string targetName = null)
    {
        var escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        if (string.IsNullOrEmpty(targetName))
        {
            _ = _net.SendRawAsync(
                "{\"type\":\"request_chat\",\"channel\":\"" + channel + "\",\"text\":\"" + escaped + "\"}");
            return;
        }

        var tn = targetName.Replace("\\", "\\\\").Replace("\"", "\\\"");
        _ = _net.SendRawAsync(
            "{\"type\":\"request_chat\",\"channel\":\"" + channel + "\",\"text\":\"" + escaped +
            "\",\"targetName\":\"" + tn + "\"}");
    }

    public void RequestPartyInvite(string targetId)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_party_invite\",\"targetId\":\"" + targetId + "\"}");
    }

    /// <summary>Legacy alias.</summary>
    public void Equip(string weaponId) => EquipWeapon(weaponId);
}
