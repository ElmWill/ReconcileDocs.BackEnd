using ReconcileDocs.Contracts.ResponseModels.Reconciles;

namespace ReconcileDocs.Contracts.Abstractions;

public interface IReconcileProgressCache
{
    bool TryGet(Guid runId, out ReconcileProgressResult? result);
    void Set(Guid runId, ReconcileProgressResult result);
}
