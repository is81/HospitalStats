using HospitalStats.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HospitalStats.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<DataSource> DataSources => Set<DataSource>();
    public DbSet<User> Users => Set<User>();
    public DbSet<BizDomain> BizDomains => Set<BizDomain>();
    public DbSet<MetaTable> MetaTables => Set<MetaTable>();
    public DbSet<MetaColumn> MetaColumns => Set<MetaColumn>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<QueryConfig> QueryConfigs => Set<QueryConfig>();
    public DbSet<QueryField> QueryFields => Set<QueryField>();
    public DbSet<QueryFilter> QueryFilters => Set<QueryFilter>();
    public DbSet<QueryJoin> QueryJoins => Set<QueryJoin>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RoleMenu> RoleMenus => Set<RoleMenu>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<DashboardCard> DashboardCards => Set<DashboardCard>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataSource>(e =>
        {
            e.HasIndex(d => d.Name).IsUnique();
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
        });

        modelBuilder.Entity<BizDomain>(e =>
        {
            e.HasIndex(d => d.Name).IsUnique();
        });

        modelBuilder.Entity<MetaTable>(e =>
        {
            e.HasIndex(t => new { t.DataSourceId, t.SchemaName, t.TableName }).IsUnique();
            e.HasOne(t => t.DataSource).WithMany().HasForeignKey(t => t.DataSourceId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.BizDomain).WithMany(d => d.Tables).HasForeignKey(t => t.BizDomainId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MetaColumn>(e =>
        {
            e.HasIndex(c => new { c.MetaTableId, c.ColumnName }).IsUnique();
            e.HasOne(c => c.MetaTable).WithMany(t => t.Columns).HasForeignKey(c => c.MetaTableId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Menu>(e =>
        {
            e.HasOne(m => m.Parent).WithMany(m => m.Children).HasForeignKey(m => m.ParentId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(m => m.QueryConfig).WithMany().HasForeignKey(m => m.QueryConfigId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<QueryConfig>(e =>
        {
            e.HasOne(q => q.MainTable).WithMany().HasForeignKey(q => q.MainTableId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QueryField>(e =>
        {
            e.HasOne(f => f.QueryConfig).WithMany(q => q.Fields)
                .HasForeignKey(f => f.QueryConfigId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(f => f.MetaColumn).WithMany().HasForeignKey(f => f.MetaColumnId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QueryFilter>(e =>
        {
            e.HasOne(f => f.QueryConfig).WithMany(q => q.Filters)
                .HasForeignKey(f => f.QueryConfigId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(f => f.MetaColumn).WithMany().HasForeignKey(f => f.MetaColumnId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QueryJoin>(e =>
        {
            e.HasOne(j => j.QueryConfig).WithMany(q => q.Joins)
                .HasForeignKey(j => j.QueryConfigId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(j => j.JoinTable).WithMany().HasForeignKey(j => j.JoinTableId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(j => j.LeftMetaColumn).WithMany().HasForeignKey(j => j.LeftMetaColumnId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(j => j.RightMetaColumn).WithMany().HasForeignKey(j => j.RightMetaColumnId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.HasIndex(r => r.Name).IsUnique();
        });

        modelBuilder.Entity<RoleMenu>(e =>
        {
            e.HasIndex(rm => new { rm.RoleId, rm.MenuId }).IsUnique();
            e.HasOne(rm => rm.Role).WithMany(r => r.RoleMenus)
                .HasForeignKey(rm => rm.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(rm => rm.Menu).WithMany().HasForeignKey(rm => rm.MenuId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserRole>(e =>
        {
            e.HasIndex(ur => new { ur.UserId, ur.RoleId }).IsUnique();
            e.HasOne(ur => ur.User).WithMany(u => u.UserRoles).HasForeignKey(ur => ur.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ur => ur.Role).WithMany(r => r.UserRoles)
                .HasForeignKey(ur => ur.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DashboardCard>(e =>
        {
            e.HasOne(d => d.QueryConfig).WithMany(q => q.DashboardCards)
                .HasForeignKey(d => d.QueryConfigId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
