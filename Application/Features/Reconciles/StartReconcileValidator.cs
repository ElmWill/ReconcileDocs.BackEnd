using FluentValidation;
using ReconcileDocs.Contracts.RequestModels.Reconciles;

namespace ReconcileDocs.Application.Features.Reconciles;

public sealed class StartReconcileValidator : AbstractValidator<StartReconcileCommand>
{
    public StartReconcileValidator()
    {
        RuleFor(command => command.SpreadsheetUploadId)
            .NotEmpty();

        RuleFor(command => command.StatementUploadId)
            .NotEmpty();

        RuleFor(command => command.StatementUploadId)
            .NotEqual(command => command.SpreadsheetUploadId)
            .WithMessage("The spreadsheet and statement upload identifiers must differ.");
    }
}