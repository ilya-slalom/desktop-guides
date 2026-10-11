using DesktopGuides.Core.Packaging;

namespace DesktopGuides.Production;

// The lane this build was packaged for (T17.1). Preview is the default.
internal static class AppLane
{
#if PUBLIC_LANE
    public static AppLaneNames Current => AppLaneNames.Public;
#else
    public static AppLaneNames Current => AppLaneNames.Preview;
#endif
}
