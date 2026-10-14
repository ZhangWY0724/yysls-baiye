namespace Yanyun.Api.Modules.Business;

// 十个固定座位：前 NeedTank 个为承伤，随后 NeedHealer 个为奶妈，其余为输出。
public static class Seats
{
    public static int Total(RaidEvent raid) => raid.NeedTank + raid.NeedHealer + raid.NeedDps;
    public static string RoleOf(RaidEvent raid, int index) =>
        index <= raid.NeedTank ? "tank" : index <= raid.NeedTank + raid.NeedHealer ? "healer" : "dps";
    public static int? FirstFree(RaidEvent raid, string role, IEnumerable<int?> taken)
    {
        var used = taken.Where(index => index is not null).Select(index => index!.Value).ToHashSet();
        for (var index = 1; index <= Total(raid); index++)
            if (RoleOf(raid, index) == role && !used.Contains(index)) return index;
        return null;
    }
}
