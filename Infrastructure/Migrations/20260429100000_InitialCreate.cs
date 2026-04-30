using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReconcileDocs.Infrastructure.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "document_uploads",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                document_kind = table.Column<int>(type: "integer", nullable: false),
                original_file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                stored_file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                size_bytes = table.Column<long>(type: "bigint", nullable: false),
                uploaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                reconcile_status = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_document_uploads", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "template_definitions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                document_kind = table.Column<int>(type: "integer", nullable: false),
                parser_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                configuration_json = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_template_definitions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "parsed_document_rows",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                document_upload_id = table.Column<Guid>(type: "uuid", nullable: false),
                row_number = table.Column<int>(type: "integer", nullable: false),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                amount = table.Column<decimal>(type: "numeric", nullable: false),
                transaction_date = table.Column<DateOnly>(type: "date", nullable: true),
                source_parser = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_parsed_document_rows", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "reconcile_runs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                spreadsheet_upload_id = table.Column<Guid>(type: "uuid", nullable: false),
                statement_upload_id = table.Column<Guid>(type: "uuid", nullable: false),
                template_definition_id = table.Column<Guid>(type: "uuid", nullable: true),
                parser_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                matched_count = table.Column<int>(type: "integer", nullable: false),
                unmatched_count = table.Column<int>(type: "integer", nullable: false),
                error_count = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_reconcile_runs", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "reconcile_matches",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                reconcile_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                spreadsheet_row_number = table.Column<int>(type: "integer", nullable: false),
                statement_row_number = table.Column<int>(type: "integer", nullable: false),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                amount = table.Column<decimal>(type: "numeric", nullable: false),
                is_matched = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_reconcile_matches", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_parsed_document_rows_document_upload_id",
            table: "parsed_document_rows",
            column: "document_upload_id");

        migrationBuilder.CreateIndex(
            name: "ix_reconcile_matches_reconcile_run_id",
            table: "reconcile_matches",
            column: "reconcile_run_id");

        migrationBuilder.CreateIndex(
            name: "ix_reconcile_runs_spreadsheet_upload_id",
            table: "reconcile_runs",
            column: "spreadsheet_upload_id");

        migrationBuilder.CreateIndex(
            name: "ix_reconcile_runs_statement_upload_id",
            table: "reconcile_runs",
            column: "statement_upload_id");

        migrationBuilder.CreateIndex(
            name: "ix_reconcile_runs_template_definition_id",
            table: "reconcile_runs",
            column: "template_definition_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "reconcile_matches");
        migrationBuilder.DropTable(name: "parsed_document_rows");
        migrationBuilder.DropTable(name: "reconcile_runs");
        migrationBuilder.DropTable(name: "template_definitions");
        migrationBuilder.DropTable(name: "document_uploads");
    }
}