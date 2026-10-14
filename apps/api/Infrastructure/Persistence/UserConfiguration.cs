using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yanyun.Api.Modules.Users;

namespace Yanyun.Api.Infrastructure.Persistence;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", table => table.HasCheckConstraint(
            "ck_users_status", "status IN ('active', 'banned')"));
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(user => user.Nickname).HasColumnName("nickname").HasMaxLength(32).IsRequired();
        builder.Property(user => user.Email).HasColumnName("email").HasMaxLength(254);
        builder.Property(user => user.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(254);
        builder.Property(user => user.EmailVerifiedAt).HasColumnName("email_verified_at");
        builder.Property(user => user.PasswordHash).HasColumnName("password_hash");
        builder.Property(user => user.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(user => user.DefaultProfileId).HasColumnName("default_profile_id");
        builder.HasOne<Yanyun.Api.Modules.Business.GameProfile>().WithMany()
            .HasForeignKey(user => new { user.DefaultProfileId, user.Id })
            .HasPrincipalKey(profile => new { profile.Id, profile.UserId }).OnDelete(DeleteBehavior.NoAction);
        builder.Property(user => user.CreatedAt).HasColumnName("created_at");
        builder.Property(user => user.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(user => user.NormalizedEmail).IsUnique()
            .HasFilter("normalized_email IS NOT NULL").HasDatabaseName("ux_users_normalized_email");
    }
}
