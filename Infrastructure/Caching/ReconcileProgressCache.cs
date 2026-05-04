using Microsoft.Extensions.Caching.Memory;
using ReconcileDocs.Contracts.Abstractions;
using ReconcileDocs.Contracts.ResponseModels.Reconciles;

namespace ReconcileDocs.Infrastructure.Caching;

public sealed class ReconcileProgressCache : IReconcileProgressCache
{
    private readonly IMemoryCache _memoryCache;
    private readonly MemoryCacheEntryOptions _cacheOptions;

    public ReconcileProgressCache(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
        _cacheOptions = new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(5),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
        };
    }

    public bool TryGet(Guid runId, out ReconcileProgressResult? result)
    {
        return _memoryCache.TryGetValue(runId, out result);
    }

    public void Set(Guid runId, ReconcileProgressResult result)
    {
        _memoryCache.Set(runId, result, _cacheOptions);
    }
}
