using FluentValidation;
using ReconcileDocs.Contracts.RequestModels.Templates;

namespace ReconcileDocs.Application.Features.Templates;

public sealed class CreateTemplateValidator : AbstractValidator<CreateTemplateCommand>
{
    public CreateTemplateValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(command => command.ParserKey)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(command => command.ConfigurationJson)
            .NotEmpty()
            .MaximumLength(2000);
    }
}