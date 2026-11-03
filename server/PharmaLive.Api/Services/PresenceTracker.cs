namespace PharmaLive.Api.Services;

/// <summary>
/// Tracks which pharmacists are online per branch. A user can have several connections
/// (tabs/devices), so we count connections per user and only announce a change when the
/// first connection arrives or the last one leaves.
/// </summary>
/// <remarks>In-memory = single server. With several servers this has to move to Redis.</remarks>
public sealed class PresenceTracker
{
    private sealed class ConnectionState(string user)
    {
        public string User { get; } = user;
        public HashSet<string> Branches { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly object _gate = new();

    // branch -> user -> connection ids
    private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _byBranch =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ConnectionState> _connections = new(StringComparer.Ordinal);

    /// <returns>true when this is the user's first connection in the branch.</returns>
    public bool Join(string branchId, string user, string connectionId)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(connectionId, out var state))
            {
                state = new ConnectionState(user);
                _connections[connectionId] = state;
            }

            state.Branches.Add(branchId);

            if (!_byBranch.TryGetValue(branchId, out var users))
            {
                users = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                _byBranch[branchId] = users;
            }

            if (!users.TryGetValue(user, out var connections))
            {
                connections = new HashSet<string>(StringComparer.Ordinal);
                users[user] = connections;
            }

            var firstConnection = connections.Count == 0;
            connections.Add(connectionId);
            return firstConnection;
        }
    }

    /// <returns>true when this was the user's last connection in the branch.</returns>
    public bool Leave(string branchId, string user, string connectionId)
    {
        lock (_gate)
        {
            if (_connections.TryGetValue(connectionId, out var state))
                state.Branches.Remove(branchId);

            return RemoveLocked(branchId, user, connectionId);
        }
    }

    /// <returns>The branches whose presence changed because this connection went away.</returns>
    public IReadOnlyList<string> Disconnect(string connectionId)
    {
        lock (_gate)
        {
            if (!_connections.Remove(connectionId, out var state)) return [];

            var changed = new List<string>();
            foreach (var branchId in state.Branches)
            {
                if (RemoveLocked(branchId, state.User, connectionId))
                    changed.Add(branchId);
            }

            return changed;
        }
    }

    public IReadOnlyList<string> UsersIn(string branchId)
    {
        lock (_gate)
        {
            return _byBranch.TryGetValue(branchId, out var users)
                ? users.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
        }
    }

    private bool RemoveLocked(string branchId, string user, string connectionId)
    {
        if (!_byBranch.TryGetValue(branchId, out var users)) return false;
        if (!users.TryGetValue(user, out var connections)) return false;

        connections.Remove(connectionId);
        if (connections.Count > 0) return false;

        users.Remove(user);
        if (users.Count == 0) _byBranch.Remove(branchId);
        return true;
    }
}
