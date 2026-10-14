namespace Yanyun.Api.Modules.Profiles;

public sealed class ProfileStyleOption
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
