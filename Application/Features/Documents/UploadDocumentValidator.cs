using FluentValidation;
using ReconcileDocs.Contracts.RequestModels.Documents;

namespace ReconcileDocs.Application.Features.Documents;

public sealed class UploadDocumentValidator : AbstractValidator<UploadDocumentCommand>
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xlsx",
        ".pdf"
    };

    public UploadDocumentValidator()
    {
        RuleFor(command => command.FileName)
            .NotEmpty()
            .MaximumLength(260)
            .Must(fileName => AllowedExtensions.Contains(Path.GetExtension(fileName)))
            .WithMessage("Only .xlsx and .pdf uploads are supported.");

        RuleFor(command => command.ContentType)
            .NotEmpty();

        RuleFor(command => command.Content)
            .NotNull()
            .Must(content => content.Length > 0)
            .WithMessage("File content is required.");
    }
}