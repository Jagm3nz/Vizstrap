using System.Net.Http;
using Vizstrap.Core;
using Vizstrap.Core.Install;
using Vizstrap.Core.Roblox;
using Vizstrap.Localization;

namespace Vizstrap;

internal static class ErrorMessages
{
    /// <summary>A short, translated explanation of what went wrong; details go in the expandable section.</summary>
    public static string For(Exception exception) => exception switch
    {
        RobloxConnectionException => Strings.Error_NoConnection,
        NotEnoughDiskSpaceException space => string.Format(Strings.Error_DiskSpace,
            ByteSize.Format(space.RequiredBytes), ByteSize.Format(space.AvailableBytes)),
        PackageDownloadException or HttpRequestException => Strings.Error_Download,
        _ => Strings.Error_Unexpected,
    };
}
