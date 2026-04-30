using FluentValidation;
using ReconcileDocs.Contracts.RequestModels.Templates;

namespace ReconcileDocs.Application.Features.Templates;

public sealed class SuggestTemplateFromUploadValidator : AbstractValidator<SuggestTemplateFromUploadCommand>
{
    public SuggestTemplateFromUploadValidator()
    {
        RuleFor(command => command.UploadId)
            .NotEqual(Guid.Empty);
    }
}
