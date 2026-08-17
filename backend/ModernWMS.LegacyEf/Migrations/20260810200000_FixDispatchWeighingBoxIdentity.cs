using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ModernWMS.Core.DBContext;

#nullable disable

namespace ModernWMS.Migrations;

/// <summary>
/// Repairs the box-weighing primary key created without AUTO_INCREMENT by the MySQL provider.
/// </summary>
[DbContext(typeof(SqlDBContext))]
[Migration("20260810200000_FixDispatchWeighingBoxIdentity")]
public class FixDispatchWeighingBoxIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE `wms_dispatch_weighing_box` MODIFY COLUMN `id` INT NOT NULL AUTO_INCREMENT;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new System.NotSupportedException(
            "Cannot remove AUTO_INCREMENT because doing so makes box weighing inserts unusable.");
    }
}
