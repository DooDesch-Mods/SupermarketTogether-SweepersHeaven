using System;
using System.Collections.Generic;

namespace SweepersHeaven;

internal sealed class RequestGate
{
    private uint lastSequence;
    private bool hasSequence;
    private double nextAllowed;

    internal bool Accept(uint sequence, double now, double cooldown)
    {
        if (hasSequence && unchecked((int)(sequence - lastSequence)) <= 0) return false;
        lastSequence = sequence;
        hasSequence = true;
        if (double.IsNaN(now) || double.IsInfinity(now) || now < nextAllowed) return false;
        nextAllowed = now + Math.Max(0.15, cooldown);
        return true;
    }
}

internal sealed class RecoveryLedger
{
    private readonly HashSet<uint> recovered = new HashSet<uint>();
    internal bool Claim(uint id) { return id != 0 && recovered.Add(id); }
    internal bool Contains(uint id) { return recovered.Contains(id); }
    internal void Clear() { recovered.Clear(); }
}
