using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WmsMes.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUniqueBomComponents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(
                    """
                    ;WITH DuplicateGroups AS
                    (
                        SELECT
                            [BomId],
                            [ComponentProductId],
                            MIN([Id]) AS [KeeperId],
                            SUM([QtyPer]) AS [TotalQtyPer],
                            COALESCE(
                                SUM([QtyPer] * [ScrapPercent])
                                    / NULLIF(SUM([QtyPer]), 0),
                                0) AS [WeightedScrapPercent]
                        FROM [BOMItems]
                        GROUP BY [BomId], [ComponentProductId]
                        HAVING COUNT(*) > 1
                    )
                    UPDATE [keeper]
                    SET
                        [keeper].[QtyPer] = [duplicates].[TotalQtyPer],
                        [keeper].[ScrapPercent] = [duplicates].[WeightedScrapPercent]
                    FROM [BOMItems] AS [keeper]
                    INNER JOIN [DuplicateGroups] AS [duplicates]
                        ON [duplicates].[KeeperId] = [keeper].[Id];

                    ;WITH RankedItems AS
                    (
                        SELECT
                            [Id],
                            ROW_NUMBER() OVER (
                                PARTITION BY [BomId], [ComponentProductId]
                                ORDER BY [Id]) AS [DuplicateRank]
                        FROM [BOMItems]
                    )
                    DELETE FROM [RankedItems]
                    WHERE [DuplicateRank] > 1;
                    """);
            }
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.Sql(
                    """
                    UPDATE "BOMItems" AS "keeper"
                    SET
                        "QtyPer" =
                        (
                            SELECT SUM("duplicate"."QtyPer")
                            FROM "BOMItems" AS "duplicate"
                            WHERE
                                "duplicate"."BomId" = "keeper"."BomId"
                                AND "duplicate"."ComponentProductId" =
                                    "keeper"."ComponentProductId"
                        ),
                        "ScrapPercent" = COALESCE(
                        (
                            SELECT
                                CAST(
                                    SUM("duplicate"."QtyPer" * "duplicate"."ScrapPercent")
                                    AS REAL)
                                    / NULLIF(SUM("duplicate"."QtyPer"), 0)
                            FROM "BOMItems" AS "duplicate"
                            WHERE
                                "duplicate"."BomId" = "keeper"."BomId"
                                AND "duplicate"."ComponentProductId" =
                                    "keeper"."ComponentProductId"
                        ),
                        0)
                    WHERE "keeper"."Id" IN
                    (
                        SELECT MIN("duplicate"."Id")
                        FROM "BOMItems" AS "duplicate"
                        GROUP BY
                            "duplicate"."BomId",
                            "duplicate"."ComponentProductId"
                        HAVING COUNT(*) > 1
                    );
                    """);
                migrationBuilder.Sql(
                    """
                    DELETE FROM "BOMItems"
                    WHERE "Id" NOT IN
                    (
                        SELECT MIN("keeper"."Id")
                        FROM "BOMItems" AS "keeper"
                        GROUP BY
                            "keeper"."BomId",
                            "keeper"."ComponentProductId"
                    );
                    """);
            }

            migrationBuilder.DropIndex(
                name: "IX_BOMItems_BomId",
                table: "BOMItems");

            migrationBuilder.CreateIndex(
                name: "UX_BOMItems_BomId_ComponentProductId",
                table: "BOMItems",
                columns: new[] { "BomId", "ComponentProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_BOMItems_BomId_ComponentProductId",
                table: "BOMItems");

            migrationBuilder.CreateIndex(
                name: "IX_BOMItems_BomId",
                table: "BOMItems",
                column: "BomId");
        }
    }
}
