using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yanyun.Api.Modules.Profiles;

namespace Yanyun.Api.Infrastructure.Persistence;

public sealed class ProfileStyleOptionConfiguration : IEntityTypeConfiguration<ProfileStyleOption>
{
    public void Configure(EntityTypeBuilder<ProfileStyleOption> builder)
    {
        builder.ToTable("profile_style_options");
        builder.HasKey(option => option.Code);
        builder.Property(option => option.Code).HasColumnName("code").HasMaxLength(64);
        builder.Property(option => option.Label).HasColumnName("label").HasMaxLength(64).IsRequired();
        builder.Property(option => option.SortOrder).HasColumnName("sort_order");
        builder.Property(option => option.Enabled).HasColumnName("enabled");
        builder.Property(option => option.UpdatedAt).HasColumnName("updated_at");
    }
}
