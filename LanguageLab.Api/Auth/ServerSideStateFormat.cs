using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;

namespace LanguageLab.Api.Auth;

/// <summary>
/// Keeps the handshake's <see cref="AuthenticationProperties"/> on the server and sends Telegram
/// nothing but a random handle in `state`.
///
/// The handler's default format protects the whole bag — return url, correlation id, PKCE
/// verifier — into a ~410-character blob, and https://oauth.telegram.org/auth answers
/// "state too long" to anything past 256 characters (measured: 256 renders the consent screen,
/// 257 does not), so every login died on the way in. A handle is 43 characters.
///
/// The store is per-process, which suits a single container: a restart mid-handshake costs the
/// user one retry, and nothing worse.
/// </summary>
public sealed class ServerSideStateFormat(TimeProvider? time = null) : ISecureDataFormat<AuthenticationProperties>
{
    /// <summary>
    /// How long a handle stays redeemable — the framework's own RemoteAuthenticationTimeout, i.e.
    /// how long the correlation cookie gives the user to authorise at Telegram.
    /// </summary>
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Handshakes in flight are anonymous — anyone can start one — so the store is capped. Real
    /// traffic never comes near it; at the cap /api/auth/telegram/start answers 503 rather than
    /// letting a flood grow memory without bound.
    /// </summary>
    internal const int MaxPending = 10_000;

    /// <summary>A sweep walks every entry, so it runs at most this often, not on every handshake.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, PendingState> _pending = new(StringComparer.Ordinal);

    private long _lastSweepTicks;

    internal int PendingCount => _pending.Count;

    /// <summary>True when no new handshake should start. Sweeps first, so expired handles never block one.</summary>
    internal bool IsFull
    {
        get
        {
            SweepExpired();
            return _pending.Count >= MaxPending;
        }
    }

    public string Protect(AuthenticationProperties data) => Protect(data, purpose: null);

    /// <summary>
    /// The purpose is ignored: the handler never passes one, and a handle means nothing outside
    /// the instance that issued it.
    /// </summary>
    public string Protect(AuthenticationProperties data, string? purpose)
    {
        // Handshakes that are started and never finished are the normal case (a user who closes
        // Telegram's page), so the abandoned ones are dropped as new ones begin.
        SweepExpired();

        var handle = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        _pending[handle] = new PendingState(data, _time.GetUtcNow() + Lifetime);

        return handle;
    }

    public AuthenticationProperties? Unprotect(string? protectedText) => Unprotect(protectedText, purpose: null);

    public AuthenticationProperties? Unprotect(string? protectedText, string? purpose)
    {
        // Removed on the way out: one callback per handshake, and a replayed one finds nothing.
        if (string.IsNullOrEmpty(protectedText) || !_pending.TryRemove(protectedText, out var pending))
        {
            return null;
        }

        // A handle that outlived its window but not the next sweep is still refused.
        return pending.ExpiresAt <= _time.GetUtcNow() ? null : pending.Properties;
    }

    private void SweepExpired()
    {
        var now = _time.GetUtcNow();
        var last = Interlocked.Read(ref _lastSweepTicks);

        // One sweep per interval, by whichever caller wins the swap — a flood of handshakes must
        // not turn into a flood of full scans.
        if (now.UtcTicks - last < SweepInterval.Ticks ||
            Interlocked.CompareExchange(ref _lastSweepTicks, now.UtcTicks, last) != last)
        {
            return;
        }

        foreach (var (handle, pending) in _pending)
        {
            if (pending.ExpiresAt <= now)
            {
                _pending.TryRemove(handle, out _);
            }
        }
    }

    private sealed record PendingState(AuthenticationProperties Properties, DateTimeOffset ExpiresAt);
}
