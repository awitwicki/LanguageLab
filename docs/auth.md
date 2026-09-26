# Authentication and roles

Sign-in is Telegram, in three flavours — OpenID Connect in a browser, the Mini App handshake
inside Telegram, and a development-only shortcut — all of which end in the same `ll_session`
cookie.

## The session cookie

Auth is Telegram over OpenID Connect, resulting in an HttpOnly `ll_session` cookie that holds an
internal user id and a role. `ICurrentUser` reads those claims; `ICurrentUserContext` adds the
role. The first successful login becomes the admin.

Bans take effect on the next request, through the cookie's `OnValidatePrincipal`.

The cookie is persistent — 30 days, sliding, with `OnSigningIn` setting `IsPersistent` — and the
Data Protection keys that encrypt it live in the `DataProtectionKeys` table, so a redeploy does not
sign everyone out.

## Development sign-in

Development can use the same real flow, since `http://localhost:5173/...` is a registered Allowed
URL in @BotFather. The shortcut is `GET /api/auth/dev-login`, a local sign-in as a dedicated
account (Telegram id 1) that skips the handshake.

It is fenced off from production three times over — `#if DEBUG` plus a Release publish,
`IsDevelopment()`, and `import.meta.env.DEV` — in `LanguageLab.Api/Auth/DevLogin.cs`. **Weakening
any of those fences is a security change**, not a convenience tweak.

Telegram credentials are therefore optional in Development and required everywhere else.

## Mini App sign-in

Opened inside Telegram, the SPA posts `window.Telegram.WebApp.initData` to
`POST /api/auth/telegram/webapp`. `LanguageLab.Api/Auth/WebAppInitData.cs` checks the HMAC-SHA256
against `Telegram:BotToken` — required outside Development, like the OIDC credentials — refuses
launches older than 24 hours, and issues the same `ll_session` cookie via `UserLoginService`.

The SPA asks `/api/auth/me` first and posts only on a 401 (`web/src/auth/useAuth.ts`;
`web/src/auth/telegram.ts` is the only file that touches `window.Telegram`).

Telegram Web — the browser iframe — is unsupported, because the cookie is `SameSite=Lax`.

### Full-screen Mini App

Opened from a home-screen icon, the page owns the whole screen, with the phone's status bar and
Telegram's own floating Close and menu buttons drawn over the top of it.

`watchTelegramFullscreen` marks `<html data-tg-fullscreen>`, and `index.css` turns Telegram's
`--tg-safe-area-inset-*` and `--tg-content-safe-area-inset-*` into `--safe-top` and
`--safe-bottom` — zero in every other mode — which the top bar, `.content`, the reader's header and
the two bottom sheets pad themselves by.

Telegram reports those insets a moment after the first paint, so the reader re-measures its header
with a `ResizeObserver`.

## Roles

`UserRole` is `User` or `Admin`, stored as an int.

The `Importer` policy is gone, and so is the `Uploader` role — dropped by the `DropUploaderRole`
migration, which moves any account still holding it back to `User`. Importing a book takes no role
at all: any signed-in user may, and the publication queue rather than a role is what keeps an
unreviewed import out of other people's way.

`Admin` is the only named policy left (`LanguageLab.Api/Auth/AuthPolicies.cs`).

`UserRoles.CanPublishDirectly(role)` (`LanguageLab.Domain/Entities/UserRoles.cs`), mirrored by
`canPublishDirectly` in `web/src/auth/roles.ts`, is true for `Admin` alone. It stays a named
predicate because the question at its call sites is whether an import skips review, not whether the
importer curates.

Admins set a user's role from the admin screen's picker.
