using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IMCAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddItemArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemsArchive",
                columns: table => new
                {
                    ArchiveId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    Attributes = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedById = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    DeletedBy = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    AttachmentsJson = table.Column<string>(type: "TEXT", nullable: true),
                    RestoredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RestoredById = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemsArchive", x => x.ArchiveId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemsArchive_DeletedAtUtc",
                table: "ItemsArchive",
                column: "DeletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ItemsArchive_DeletedById",
                table: "ItemsArchive",
                column: "DeletedById");

            migrationBuilder.CreateIndex(
                name: "IX_ItemsArchive_ItemId",
                table: "ItemsArchive",
                column: "ItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemsArchive");
        }
    }
}
