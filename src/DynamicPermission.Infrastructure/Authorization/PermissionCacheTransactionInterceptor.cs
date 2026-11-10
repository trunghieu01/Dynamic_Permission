using System.Data.Common;
using System.Runtime.CompilerServices;
using DynamicPermission.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DynamicPermission.Infrastructure.Authorization;

public sealed class PermissionCacheTransactionInterceptor : DbTransactionInterceptor
{
    private readonly object _sync = new();
    private readonly ConditionalWeakTable<DbContext, PermissionCacheInvalidation> _pending = new();
    private readonly IPermissionCache _permissionCache;

    public PermissionCacheTransactionInterceptor(IPermissionCache permissionCache)
    {
        _permissionCache = permissionCache;
    }

    public void InvalidateAfterSave(DbContext context, PermissionCacheInvalidation invalidation)
    {
        if (invalidation.IsEmpty)
        {
            return;
        }

        if (context.Database.CurrentTransaction is null)
        {
            invalidation.Apply(_permissionCache);
            return;
        }

        lock (_sync)
        {
            if (_pending.TryGetValue(context, out var existing))
            {
                existing.Merge(invalidation);
            }
            else
            {
                _pending.Add(context, invalidation);
            }
        }
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        base.TransactionCommitted(transaction, eventData);
        Complete(eventData.Context, commit: true);
    }

    public override async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
        Complete(eventData.Context, commit: true);
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        base.TransactionRolledBack(transaction, eventData);
        Complete(eventData.Context, commit: false);
    }

    public override async Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await base.TransactionRolledBackAsync(transaction, eventData, cancellationToken);
        Complete(eventData.Context, commit: false);
    }

    private void Complete(DbContext? context, bool commit)
    {
        if (context is null)
        {
            return;
        }

        PermissionCacheInvalidation? invalidation;
        lock (_sync)
        {
            if (!_pending.TryGetValue(context, out invalidation))
            {
                return;
            }

            _pending.Remove(context);
        }

        if (commit)
        {
            invalidation.Apply(_permissionCache);
        }
    }
}
