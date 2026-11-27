using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Expenses;

namespace NeedleBase.Application.Expenses;

/// <summary>Expenses use cases (plans step 7b-2). Owner/Manager on plans with profit reports; tenant-filtered.</summary>
public interface IExpenseService
{
    Task<RecordResult<ExpenseListResponse>> ListAsync(ExpenseListQuery query, CancellationToken cancellationToken = default);

    Task<RecordResult<ExpenseDto>> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<RecordResult<ExpenseSaveResult>> RecordAsync(RecordExpenseRequest request, CancellationToken cancellationToken = default);

    Task<RecordResult<ExpenseDto>> VoidAsync(string id, VoidExpenseRequest request, CancellationToken cancellationToken = default);
}
