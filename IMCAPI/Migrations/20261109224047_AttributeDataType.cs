using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IMCAPI.Migrations
{
    /// <inheritdoc />
    public partial class AttributeDataType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DataType",
                table: "Constants",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataType",
                table: "Constants");
        }
    }
}
