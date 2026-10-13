using System.Security.Claims;

namespace LogPilot.Web.Security;

public static class ClaimsPrincipalExtensions
{
    public static bool TryGetUserId(this ClaimsPrincipal user, out long userId)
    {
        var raw = user.FindFirstValue(LogPilotClaimTypes.UserId)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(raw, out userId);
    }

    public static bool IsSystemAdmin(this ClaimsPrincipal user)
        => string.Equals(user.FindFirstValue(LogPilotClaimTypes.IsSystemAdmin), "1", StringComparison.Ordinal);
}
