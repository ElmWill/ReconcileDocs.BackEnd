using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Application.Behaviors;
using ReconcileDocs.Application.Features.Documents;
using ReconcileDocs.Infrastructure.Parsing;
using ReconcileDocs.Infrastructure.Persistence;
using ReconcileDocs.Infrastructure.Storage;
using ReconcileDocs.Domain.Entities;
using ReconcileDocs.Domain;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be provided through user secrets or environment variables.");
    }

    options.UseNpgsql(connectionString);
});

builder.Services.AddScoped<IApplicationDbContext>(serviceProvider =>
    serviceProvider.GetRequiredService<ApplicationDbContext>());

builder.Services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssemblyContaining<UploadDocumentHandler>();
});

builder.Services.AddValidatorsFromAssemblyContaining<UploadDocumentValidator>();
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<IStatementParserResolver, StatementParserResolver>();
builder.Services.AddHttpClient<ReconcileDocs.Infrastructure.AI.OllamaStatementModelExtractor>();
builder.Services.AddSingleton<ReconcileDocs.Infrastructure.AI.ILastModelResponseStore, ReconcileDocs.Infrastructure.AI.LastModelResponseStore>();
builder.Services.AddScoped<IStatementModelExtractor, ReconcileDocs.Infrastructure.AI.OllamaStatementModelExtractor>();
builder.Services.AddScoped<IPdfOcrTextExtractor, ReconcileDocs.Infrastructure.AI.WindowsPdfOcrTextExtractor>();
builder.Services.AddScoped<IStatementParser, ExcelStatementParser>();
builder.Services.AddScoped<IStatementParser, PdfStatementParser>();
builder.Services.AddScoped<IStatementParser, TemplateStatementParser>();
builder.Services.AddScoped<IStatementParser, GenericStatementParser>();

// in-memory cache for reconcile progress
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ReconcileDocs.Contracts.Abstractions.IReconcileProgressCache, ReconcileDocs.Infrastructure.Caching.ReconcileProgressCache>();

// background reconcile queue and processor
builder.Services.AddSingleton<ReconcileDocs.Application.Abstractions.IBackgroundTaskQueue, ReconcileDocs.Infrastructure.Background.BackgroundTaskQueue>();
builder.Services.AddScoped<ReconcileDocs.Application.Services.ReconcileProcessor>();
builder.Services.AddHostedService<ReconcileDocs.Infrastructure.Background.ReconcileBackgroundService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();

    await SeedDefaultTemplatesAsync(dbContext);
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();

static async Task SeedDefaultTemplatesAsync(ApplicationDbContext dbContext)
{
    var existingTemplateNames = await dbContext.TemplateDefinitions
        .Select(template => template.Name)
        .ToListAsync();

    var existingNames = new HashSet<string>(existingTemplateNames, StringComparer.OrdinalIgnoreCase);
    var templatesToAdd = CreateDefaultTemplates()
        .Where(template => !existingNames.Contains(template.Name))
        .ToList();

    if (templatesToAdd.Count == 0)
    {
        return;
    }

    dbContext.TemplateDefinitions.AddRange(templatesToAdd);
    await dbContext.SaveChangesAsync();
}

static IReadOnlyList<TemplateDefinition> CreateDefaultTemplates()
{
    var now = DateTime.UtcNow;

    return new[]
    {
        new TemplateDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Bank statement basic",
            DocumentKind = (int)DocumentKind.StatementPdf,
            ParserKey = "template",
            ConfigurationJson = JsonSerializer.Serialize(new
            {
                skipRows = 0,
                datePatterns = new[] { "dd/MM/yyyy", "yyyy-MM-dd" },
                descriptionColumnHints = new[] { "description", "narration", "details" },
                amountColumnHints = new[] { "amount", "debit", "credit" }
            }),
            IsActive = true,
            CreatedAtUtc = now
        },
        new TemplateDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Bank statement compact",
            DocumentKind = (int)DocumentKind.StatementPdf,
            ParserKey = "template",
            ConfigurationJson = JsonSerializer.Serialize(new
            {
                skipRows = 1,
                datePatterns = new[] { "dd/MM/yyyy", "MM/dd/yyyy" },
                descriptionColumnHints = new[] { "desc", "narration", "ref" },
                amountColumnHints = new[] { "amt", "amount", "debit", "credit" }
            }),
            IsActive = true,
            CreatedAtUtc = now
        },
        new TemplateDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Spreadsheet transactions",
            DocumentKind = (int)DocumentKind.Spreadsheet,
            ParserKey = "template",
            ConfigurationJson = JsonSerializer.Serialize(new
            {
                skipRows = 1,
                datePatterns = new[] { "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd" },
                descriptionColumnHints = new[] { "description", "remarks", "narration" },
                amountColumnHints = new[] { "amount", "debit", "credit", "value" }
            }),
            IsActive = true,
            CreatedAtUtc = now
        },
        new TemplateDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Spreadsheet ledger",
            DocumentKind = (int)DocumentKind.Spreadsheet,
            ParserKey = "template",
            ConfigurationJson = JsonSerializer.Serialize(new
            {
                skipRows = 2,
                datePatterns = new[] { "dd/MM/yyyy", "dd-MM-yyyy" },
                descriptionColumnHints = new[] { "description", "particulars", "transaction" },
                amountColumnHints = new[] { "debit", "credit", "balance" }
            }),
            IsActive = true,
            CreatedAtUtc = now
        },
        new TemplateDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Statement fallback",
            DocumentKind = (int)DocumentKind.StatementPdf,
            ParserKey = "template",
            ConfigurationJson = JsonSerializer.Serialize(new
            {
                skipRows = 0,
                datePatterns = new[] { "dd-MM-yyyy", "dd/MM/yyyy" },
                descriptionColumnHints = new[] { "particulars", "description", "ref" },
                amountColumnHints = new[] { "amount", "withdrawal", "deposit" }
            }),
            IsActive = true,
            CreatedAtUtc = now
        },
        new TemplateDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Generic fallback",
            DocumentKind = (int)DocumentKind.Spreadsheet,
            ParserKey = "template",
            ConfigurationJson = JsonSerializer.Serialize(new
            {
                skipRows = 0,
                datePatterns = new[] { "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy" },
                descriptionColumnHints = new[] { "description", "details", "memo", "narration" },
                amountColumnHints = new[] { "amount", "value", "debit", "credit" }
            }),
            IsActive = true,
            CreatedAtUtc = now
        }
    };
}
