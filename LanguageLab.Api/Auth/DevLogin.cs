#if DEBUG
using LanguageLab.Application.Services;

namespace LanguageLab.Api.Auth;

/// <summary>
/// A local sign-in that skips the Telegram handshake, so the app can be run and worked on
/// without @BotFather credentials. It is a shortcut past the OIDC round trip only: the
/// session it issues is an ordinary one, built by <see cref="PrincipalFactory"/> from an
/// account created by the ordinary <see cref="UserLoginService"/>, so bans and roles apply
/// to it exactly as they do to a real login.
///
/// It is fenced off from production three times over, and each fence holds on its own:
///
///   1. This file is inside <c>#if DEBUG</c>, and the image publishes <c>-c Release</c>
///      (LanguageLab.Api/Dockerfile) — the endpoint is absent from the deployed binary, so
///      no environment variable or configuration mistake can switch it back on.
///   2. <see cref="IsEnabled"/> additionally demands the Development environment, which
///      covers a Debug build pointed at a real database. Containers get Production by
///      default; compose.yaml sets no ASPNETCORE_ENVIRONMENT.
///   3. The button in the SPA sits behind <c>import.meta.env.DEV</c>, which Vite folds to a
///      literal <c>false</c> in <c>npm run build</c>.
///
/// Weakening any one of these is a security change, not a cleanup.
/// </summary>
internal static class DevLogin
{
    /// <summary>
    /// Telegram ids are nine or ten digits, so 1 belongs to nobody: the dev account can
    /// never collide with an account someone signs into for real. On an empty database the
    /// row it creates takes internal id 1 and, by the first-login rule, the admin role.
    /// </summary>
    public static TelegramIdentity Identity { get; } = new(
        TelegramUserId: 1,
        FirstName: "Local",
        LastName: "Developer",
        Username: "localdev",
        PhotoUrl: null);

    public static bool IsEnabled(IHostEnvironment environment) => environment.IsDevelopment();
}
#endif
