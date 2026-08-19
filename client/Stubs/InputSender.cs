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

    public void RequestPartyRespond(string inviteId, bool accept)
    {
        var a = accept ? "true" : "false";
        _ = _net.SendRawAsync(
            "{\"type\":\"request_party_respond\",\"inviteId\":\"" + inviteId + "\",\"accept\":" + a + "}");
    }

    public void RequestPartyLeave()
    {
        _ = _net.SendRawAsync("{\"type\":\"request_party_leave\"}");
    }

    public void RequestCharCreate(string name, string classId)
    {
        var n = name.Replace("\\", "\\\\").Replace("\"", "\\\"");
        _ = _net.SendRawAsync(
            "{\"type\":\"request_char_create\",\"name\":\"" + n + "\",\"classId\":\"" + classId + "\"}");
    }

    public void RequestServerList()
    {
        _ = _net.SendRawAsync("{\"type\":\"request_server_list\"}");
    }

    public void RequestCharList()
    {
        _ = _net.SendRawAsync("{\"type\":\"request_char_list\"}");
    }

    public void RequestCharSelect(int slotIndex)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_char_select\",\"slotIndex\":" + slotIndex + "}");
    }

    public void RequestCharCreateSlot(int slotIndex, string name)
    {
        var n = name.Replace("\\", "\\\\").Replace("\"", "\\\"");
        _ = _net.SendRawAsync(
            "{\"type\":\"request_char_create_slot\",\"slotIndex\":" + slotIndex +
            ",\"name\":\"" + n + "\"}");
    }

    public void RequestCharDelete(int slotIndex)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_char_delete\",\"slotIndex\":" + slotIndex + "}");
    }

    public void RequestWeaponSwap()
    {
        _ = _net.SendRawAsync("{\"type\":\"request_weapon_swap\"}");
    }

    public void RequestPortal(string portalId)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_portal\",\"portalId\":\"" + portalId + "\"}");
    }

    public void RequestInteract(string targetId)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_interact\",\"targetId\":\"" + targetId + "\"}");
    }

    public void RequestShopBuy(string shopId, string itemId)
    {
        _ = _net.SendRawAsync(
            "{\"type\":\"request_shop_buy\",\"shopId\":\"" + shopId + "\",\"itemId\":\"" + itemId + "\",\"quantity\":1}");
    }

    public void RequestShopSell(string shopId, string itemId)
    {
        _ = _net.SendRawAsync(
            "{\"type\":\"request_shop_sell\",\"shopId\":\"" + shopId + "\",\"itemId\":\"" + itemId + "\",\"quantity\":1}");
    }

    public void RequestUseItem(int slotIndex)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_use_item\",\"slotIndex\":" + slotIndex + "}");
    }

    public void RequestHomestone(string action)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_homestone\",\"action\":\"" + action + "\"}");
    }

    public void RequestQuestAccept(string questId)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_quest_accept\",\"questId\":\"" + questId + "\"}");
    }

    public void RequestQuestTurnIn(string questId)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_quest_turnin\",\"questId\":\"" + questId + "\"}");
    }

    public void RequestRegister(string username, string password)
    {
        var u = username.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var p = password.Replace("\\", "\\\\").Replace("\"", "\\\"");
        _ = _net.SendRawAsync(
            "{\"type\":\"request_register\",\"username\":\"" + u + "\",\"password\":\"" + p + "\"}");
    }

    public void RequestLogin(string username, string password)
    {
        var u = username.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var p = password.Replace("\\", "\\\\").Replace("\"", "\\\"");
        _ = _net.SendRawAsync(
            "{\"type\":\"request_login\",\"username\":\"" + u + "\",\"password\":\"" + p + "\"}");
    }

    public void RequestEquipGear(string slot, string itemIdOrNull)
    {
        if (string.IsNullOrEmpty(itemIdOrNull))
        {
            _ = _net.SendRawAsync(
                "{\"type\":\"request_equip_gear\",\"slot\":\"" + slot + "\",\"itemId\":null}");
            return;
        }

        _ = _net.SendRawAsync(
            "{\"type\":\"request_equip_gear\",\"slot\":\"" + slot + "\",\"itemId\":\"" + itemIdOrNull + "\"}");
    }

    public void RequestTradeInvite(string targetId)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_trade_invite\",\"targetId\":\"" + targetId + "\"}");
    }

    public void RequestTradeRespond(string inviteId, bool accept)
    {
        var a = accept ? "true" : "false";
        _ = _net.SendRawAsync(
            "{\"type\":\"request_trade_respond\",\"inviteId\":\"" + inviteId + "\",\"accept\":" + a + "}");
    }

    public void RequestTradeOffer(int gold, int slotIndex = -1, int quantity = 0)
    {
        var offers = slotIndex >= 0 && quantity > 0
            ? "[{\"slotIndex\":" + slotIndex + ",\"quantity\":" + quantity + "}]"
            : "[]";
        RequestTradeOfferRaw(gold, offers);
    }

    public void RequestTradeOfferRaw(int gold, string offersJson)
    {
        _ = _net.SendRawAsync(
            "{\"type\":\"request_trade_offer\",\"gold\":" + gold + ",\"offers\":" + offersJson + "}");
    }

    public void RequestTradeConfirm()
    {
        _ = _net.SendRawAsync("{\"type\":\"request_trade_confirm\"}");
    }

    public void RequestTradeCancel()
    {
        _ = _net.SendRawAsync("{\"type\":\"request_trade_cancel\"}");
    }

    public void RequestFriendAdd(string targetId)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_friend_add\",\"targetId\":\"" + targetId + "\"}");
    }

    public void RequestFriendRemove(string guestToken)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_friend_remove\",\"guestToken\":\"" + guestToken + "\"}");
    }

    public void RequestGuildCreate(string name)
    {
        var n = name.Replace("\\", "\\\\").Replace("\"", "\\\"");
        _ = _net.SendRawAsync("{\"type\":\"request_guild_create\",\"name\":\"" + n + "\"}");
    }

    public void RequestGuildInvite(string targetId)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_guild_invite\",\"targetId\":\"" + targetId + "\"}");
    }

    public void RequestGuildRespond(string inviteId, bool accept)
    {
        var a = accept ? "true" : "false";
        _ = _net.SendRawAsync(
            "{\"type\":\"request_guild_respond\",\"inviteId\":\"" + inviteId + "\",\"accept\":" + a + "}");
    }

    public void RequestGuildLeave()
    {
        _ = _net.SendRawAsync("{\"type\":\"request_guild_leave\"}");
    }

    public void RequestSkillUnlock(string skillId)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_skill_unlock\",\"skillId\":\"" + skillId + "\"}");
    }

    public void RequestAuctionList()
    {
        _ = _net.SendRawAsync("{\"type\":\"request_auction_list\"}");
    }

    public void RequestAuctionSell(string itemId, int quantity, int price)
    {
        _ = _net.SendRawAsync(
            "{\"type\":\"request_auction_sell\",\"itemId\":\"" + itemId +
            "\",\"quantity\":" + quantity + ",\"price\":" + price + "}");
    }

    public void RequestAuctionBuy(string listingId)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_auction_buy\",\"listingId\":\"" + listingId + "\"}");
    }

    public void RequestAuctionCancel(string listingId)
    {
        _ = _net.SendRawAsync("{\"type\":\"request_auction_cancel\",\"listingId\":\"" + listingId + "\"}");
    }

    /// <summary>Legacy alias.</summary>
    public void Equip(string weaponId) => EquipWeapon(weaponId);
}
