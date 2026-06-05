internal sealed class AdminProcessorEndpointCheckLimiter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _lastChecks = new(StringComparer.Ordinal);

    public bool TryBegin(string endpoint, DateTimeOffset now, TimeSpan cooldown, out DateTimeOffset? nextAllowedAt)
    {
        if (cooldown <= TimeSpan.Zero)
        {
            nextAllowedAt = null;
            return true;
        }

        lock (_gate)
        {
            if (_lastChecks.TryGetValue(endpoint, out var lastCheck))
            {
                var allowedAt = lastCheck.Add(cooldown);
                if (allowedAt > now)
                {
                    nextAllowedAt = allowedAt;
                    return false;
                }
            }

            _lastChecks[endpoint] = now;
            nextAllowedAt = now.Add(cooldown);
            return true;
        }
    }
}
