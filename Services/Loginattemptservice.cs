using System;
using System.Collections.Generic;

namespace OmsCafe.Services;

public readonly record struct AttemptResult(bool IsLockedOut, int AttemptsLeft, TimeSpan LockedFor);

/// <summary>
/// Tracks failed sign-ins. After MaxAttempts failures the key is locked for LockoutDuration.
/// Register as a SINGLETON. State is in memory (resets on app restart / not shared across servers).
/// </summary>
public sealed class LoginAttemptService
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(30);

    private sealed class Entry
    {
        public int Failed;
        public DateTimeOffset LastFailure;
        public DateTimeOffset? LockedUntil;
    }

    private readonly Dictionary<string, Entry> _entries = new();
    private readonly object _gate = new();
    private DateTimeOffset _lastSweep = DateTimeOffset.UtcNow;

    /// <summary>True while the key is locked. 'remaining' is the time left.</summary>
    public bool IsLockedOut(string key, out TimeSpan remaining)
    {
        lock (_gate)
        {
            Sweep();
            remaining = TimeSpan.Zero;

            if (!_entries.TryGetValue(key, out var e) || e.LockedUntil is null)
                return false;

            var now = DateTimeOffset.UtcNow;
            if (e.LockedUntil > now)
            {
                remaining = e.LockedUntil.Value - now;
                return true;
            }

            // Lock expired -> fresh start
            _entries.Remove(key);
            return false;
        }
    }

    /// <summary>Call after a wrong password.</summary>
    public AttemptResult RegisterFailure(string key)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;

            if (!_entries.TryGetValue(key, out var e))
                _entries[key] = e = new Entry();

            // Already locked (e.g. parallel requests)
            if (e.LockedUntil is { } until && until > now)
                return new AttemptResult(true, 0, until - now);

            // Old failures expire after the same window
            if (now - e.LastFailure > LockoutDuration)
                e.Failed = 0;

            e.Failed++;
            e.LastFailure = now;
            e.LockedUntil = null;

            if (e.Failed >= MaxAttempts)
            {
                e.LockedUntil = now + LockoutDuration;
                return new AttemptResult(true, 0, LockoutDuration);
            }

            return new AttemptResult(false, MaxAttempts - e.Failed, TimeSpan.Zero);
        }
    }

    /// <summary>Call after a successful sign-in.</summary>
    public void Reset(string key)
    {
        lock (_gate) { _entries.Remove(key); }
    }

    // Remove stale entries every 10 minutes so memory doesn't grow forever.
    private void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastSweep < TimeSpan.FromMinutes(10)) return;
        _lastSweep = now;

        var stale = new List<string>();
        foreach (var (key, e) in _entries)
        {
            var expiry = e.LockedUntil ?? e.LastFailure + LockoutDuration;
            if (expiry < now) stale.Add(key);
        }
        foreach (var key in stale) _entries.Remove(key);
    }
}