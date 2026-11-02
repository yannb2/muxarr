using Microsoft.EntityFrameworkCore.Diagnostics;
using Muxarr.Data.Entities;

namespace Muxarr.Data;

/// <summary>
/// Flags snapshots read from the database so their tracks read back in Index
/// order whatever order SQLite returned the rows in. See <see cref="MediaSnapshot.Tracks"/>.
/// </summary>
public sealed class SnapshotLoadInterceptor : IMaterializationInterceptor
{
    public static readonly SnapshotLoadInterceptor Instance = new();

    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        if (entity is MediaSnapshot snapshot)
        {
            snapshot.MarkLoadedFromDatabase();
        }

        return entity;
    }
}
