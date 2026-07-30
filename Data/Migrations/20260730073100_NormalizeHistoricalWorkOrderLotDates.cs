using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WmsMes.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeHistoricalWorkOrderLotDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- SQL Server names the Asia/Saigon (UTC+07:00) zone "SE Asia Standard Time".
                -- Work-order lots created before this migration stored UTC instants in datetime2.
                UPDATE [Lots]
                SET [ManufactureDate] = CONVERT(date,
                    [ManufactureDate] AT TIME ZONE 'UTC' AT TIME ZONE 'SE Asia Standard Time')
                WHERE [WorkOrderId] IS NOT NULL
                  AND [ManufactureDate] IS NOT NULL
                  AND [ManufactureDate] <> CONVERT(date,
                      [ManufactureDate] AT TIME ZONE 'UTC' AT TIME ZONE 'SE Asia Standard Time');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This data correction is irreversible because converting the historical UTC
            // timestamp to a business calendar date intentionally discards its time-of-day.
            // Down is intentionally a no-op rather than fabricating an original timestamp.
        }
    }
}
