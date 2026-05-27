using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalStats.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixDashboardCardFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BizDomains",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BizDomains", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DbType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ConnectionString = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Schema = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CharSetOverride = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CharSetInfo = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetaTables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DataSourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    BizDomainId = table.Column<int>(type: "INTEGER", nullable: true),
                    TableName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SchemaName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Alias = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsView = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaTables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetaTables_BizDomains_BizDomainId",
                        column: x => x.BizDomainId,
                        principalTable: "BizDomains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MetaTables_DataSources_DataSourceId",
                        column: x => x.DataSourceId,
                        principalTable: "DataSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    RoleId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId1 = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId1",
                        column: x => x.UserId1,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MetaColumns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MetaTableId = table.Column<int>(type: "INTEGER", nullable: false),
                    ColumnName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DataType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    DataLength = table.Column<int>(type: "INTEGER", nullable: true),
                    DataPrecision = table.Column<int>(type: "INTEGER", nullable: true),
                    DataScale = table.Column<int>(type: "INTEGER", nullable: true),
                    Nullable = table.Column<bool>(type: "INTEGER", nullable: false),
                    Alias = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsQueryField = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsFilterField = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDisplayField = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaColumns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetaColumns_MetaTables_MetaTableId",
                        column: x => x.MetaTableId,
                        principalTable: "MetaTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QueryConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    MainTableId = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    AggregateType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    AggregateColumn = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    GroupByColumn = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SortColumn = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SortDirection = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    PageSize = table.Column<int>(type: "INTEGER", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueryConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueryConfigs_MetaTables_MainTableId",
                        column: x => x.MainTableId,
                        principalTable: "MetaTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DashboardCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    QueryConfigId = table.Column<int>(type: "INTEGER", nullable: true),
                    DisplayType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Color = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DashboardCards_QueryConfigs_QueryConfigId",
                        column: x => x.QueryConfigId,
                        principalTable: "QueryConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Menus",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    QueryConfigId = table.Column<int>(type: "INTEGER", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Menus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Menus_Menus_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Menus_QueryConfigs_QueryConfigId",
                        column: x => x.QueryConfigId,
                        principalTable: "QueryConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "QueryFields",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QueryConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    MetaColumnId = table.Column<int>(type: "INTEGER", nullable: false),
                    Alias = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    AggregateFunc = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueryFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueryFields_MetaColumns_MetaColumnId",
                        column: x => x.MetaColumnId,
                        principalTable: "MetaColumns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueryFields_QueryConfigs_QueryConfigId",
                        column: x => x.QueryConfigId,
                        principalTable: "QueryConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QueryFilters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QueryConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    MetaColumnId = table.Column<int>(type: "INTEGER", nullable: false),
                    Operator = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DefaultValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    ControlType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueryFilters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueryFilters_MetaColumns_MetaColumnId",
                        column: x => x.MetaColumnId,
                        principalTable: "MetaColumns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueryFilters_QueryConfigs_QueryConfigId",
                        column: x => x.QueryConfigId,
                        principalTable: "QueryConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QueryJoins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QueryConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    JoinTableId = table.Column<int>(type: "INTEGER", nullable: false),
                    JoinType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    LeftMetaColumnId = table.Column<int>(type: "INTEGER", nullable: false),
                    RightMetaColumnId = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueryJoins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueryJoins_MetaColumns_LeftMetaColumnId",
                        column: x => x.LeftMetaColumnId,
                        principalTable: "MetaColumns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueryJoins_MetaColumns_RightMetaColumnId",
                        column: x => x.RightMetaColumnId,
                        principalTable: "MetaColumns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueryJoins_MetaTables_JoinTableId",
                        column: x => x.JoinTableId,
                        principalTable: "MetaTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueryJoins_QueryConfigs_QueryConfigId",
                        column: x => x.QueryConfigId,
                        principalTable: "QueryConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoleMenus",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<int>(type: "INTEGER", nullable: false),
                    MenuId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleMenus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleMenus_Menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "Menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoleMenus_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BizDomains_Name",
                table: "BizDomains",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DashboardCards_QueryConfigId",
                table: "DashboardCards",
                column: "QueryConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_DataSources_Name",
                table: "DataSources",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Menus_ParentId",
                table: "Menus",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Menus_QueryConfigId",
                table: "Menus",
                column: "QueryConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_MetaColumns_MetaTableId_ColumnName",
                table: "MetaColumns",
                columns: new[] { "MetaTableId", "ColumnName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetaTables_BizDomainId",
                table: "MetaTables",
                column: "BizDomainId");

            migrationBuilder.CreateIndex(
                name: "IX_MetaTables_DataSourceId_SchemaName_TableName",
                table: "MetaTables",
                columns: new[] { "DataSourceId", "SchemaName", "TableName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueryConfigs_MainTableId",
                table: "QueryConfigs",
                column: "MainTableId");

            migrationBuilder.CreateIndex(
                name: "IX_QueryFields_MetaColumnId",
                table: "QueryFields",
                column: "MetaColumnId");

            migrationBuilder.CreateIndex(
                name: "IX_QueryFields_QueryConfigId",
                table: "QueryFields",
                column: "QueryConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_QueryFilters_MetaColumnId",
                table: "QueryFilters",
                column: "MetaColumnId");

            migrationBuilder.CreateIndex(
                name: "IX_QueryFilters_QueryConfigId",
                table: "QueryFilters",
                column: "QueryConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_QueryJoins_JoinTableId",
                table: "QueryJoins",
                column: "JoinTableId");

            migrationBuilder.CreateIndex(
                name: "IX_QueryJoins_LeftMetaColumnId",
                table: "QueryJoins",
                column: "LeftMetaColumnId");

            migrationBuilder.CreateIndex(
                name: "IX_QueryJoins_QueryConfigId",
                table: "QueryJoins",
                column: "QueryConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_QueryJoins_RightMetaColumnId",
                table: "QueryJoins",
                column: "RightMetaColumnId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleMenus_MenuId",
                table: "RoleMenus",
                column: "MenuId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleMenus_RoleId_MenuId",
                table: "RoleMenus",
                columns: new[] { "RoleId", "MenuId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId_RoleId",
                table: "UserRoles",
                columns: new[] { "UserId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId1",
                table: "UserRoles",
                column: "UserId1");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DashboardCards");

            migrationBuilder.DropTable(
                name: "QueryFields");

            migrationBuilder.DropTable(
                name: "QueryFilters");

            migrationBuilder.DropTable(
                name: "QueryJoins");

            migrationBuilder.DropTable(
                name: "RoleMenus");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "MetaColumns");

            migrationBuilder.DropTable(
                name: "Menus");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "QueryConfigs");

            migrationBuilder.DropTable(
                name: "MetaTables");

            migrationBuilder.DropTable(
                name: "BizDomains");

            migrationBuilder.DropTable(
                name: "DataSources");
        }
    }
}
