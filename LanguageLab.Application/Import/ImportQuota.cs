using System.Collections.Concurrent;
using LanguageLab.Domain.Entities;

namespace LanguageLab.Application.Import;

/// <summary>
/// The 1-import/day cap, counted only against a *successful* import (2026-09-29 roadmap decision):
/// a wrong file, a non-English book or DRM must not spend the day's slot. A limiter the service
/// asks itself, in memory — a restart forgives everybody — same style as SentenceQuota
/// (LanguageLab.Api/SentenceQuota.cs), but keyed on outcome rather than on every request, which
/// the declarative rate-limiter middleware cannot express.
///
/// TryReserve/ReleaseReservation, not RetryAfter alone, is what BookFileImportService gates on:
/// checking and marking the slot spent must be one atomic step, or two concurrent imports for the
/// same user can both pass the check before either finishes (final review finding, C4).
/// </summary>
public sealed class ImportQuota
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(1);

    private readonly ConcurrentDictionary<long, DateTimeOffset> _lastSuccess = new();
    private readonly TimeProvider _time;

    public ImportQuota(TimeProvider time) => _time = time;

    /// <summary>A read-only peek, for reporting the wait without spending anything — ReaderCapabilities.</summary>
    public TimeSpan? RetryAfter(long userId, UserRole role)
    {
        if (role == UserRole.Admin || !_lastSuccess.TryGetValue(userId, out var last))
        {
            return null;
        }

        var remaining = Window - (_time.GetUtcNow() - last);
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    /// <summary>
    /// Checks and reserves the slot in one atomic step. null = reserved, go ahead; otherwise the
    /// remaining wait, and nothing was reserved. A caller that reserves and then fails must call
    /// <see cref="ReleaseReservation"/>, or the day stays spent for an import that never happened.
    /// </summary>
    public TimeSpan? TryReserve(long userId, UserRole role)
    {
        if (role == UserRole.Admin)
        {
            return null;
        }

        while (true)
        {
            var now = _time.GetUtcNow();

            if (_lastSuccess.TryGetValue(userId, out var last))
            {
                var remaining = Window - (now - last);

                if (remaining > TimeSpan.Zero)
                {
                    return remaining;
                }

                if (_lastSuccess.TryUpdate(userId, now, last))
                {
                    return null;
                }
            }
            else if (_lastSuccess.TryAdd(userId, now))
            {
                return null;
            }
        }
    }

    /// <summary>Undoes a reservation that did not end in a real import.</summary>
    public void ReleaseReservation(long userId) => _lastSuccess.TryRemove(userId, out _);
}
