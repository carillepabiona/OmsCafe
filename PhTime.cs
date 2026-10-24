using System;

namespace OmsCafe;

public static class PhTime
{
    // The Philippines is UTC+8 with no daylight saving.
    public static DateTime Now =>
        DateTime.SpecifyKind(DateTime.UtcNow.AddHours(8), DateTimeKind.Unspecified);

    public static DateTime Today => Now.Date;
}