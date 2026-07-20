namespace WmsMes.Web.Services;

public static class BusinessTimeZoneResolver
{
    public static TimeZoneInfo Resolve(string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            var hasAlternateId = TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var alternateId)
                || TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out alternateId);
            if (hasAlternateId && alternateId is not null)
            {
                return TimeZoneInfo.FindSystemTimeZoneById(alternateId);
            }

            throw;
        }
    }
}
