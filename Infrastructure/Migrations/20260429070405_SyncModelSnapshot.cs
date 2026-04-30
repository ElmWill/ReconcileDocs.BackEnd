using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReconcileDocs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_template_definitions",
                table: "template_definitions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_reconcile_runs",
                table: "reconcile_runs");

            migrationBuilder.DropIndex(
                name: "ix_reconcile_runs_spreadsheet_upload_id",
                table: "reconcile_runs");

            migrationBuilder.DropIndex(
                name: "ix_reconcile_runs_statement_upload_id",
                table: "reconcile_runs");

            migrationBuilder.DropIndex(
                name: "ix_reconcile_runs_template_definition_id",
                table: "reconcile_runs");

            migrationBuilder.DropPrimaryKey(
                name: "pk_reconcile_matches",
                table: "reconcile_matches");

            migrationBuilder.DropIndex(
                name: "ix_reconcile_matches_reconcile_run_id",
                table: "reconcile_matches");

            migrationBuilder.DropPrimaryKey(
                name: "pk_parsed_document_rows",
                table: "parsed_document_rows");

            migrationBuilder.DropIndex(
                name: "ix_parsed_document_rows_document_upload_id",
                table: "parsed_document_rows");

            migrationBuilder.DropPrimaryKey(
                name: "pk_document_uploads",
                table: "document_uploads");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "template_definitions",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "reconcile_runs",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "reconcile_matches",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "parsed_document_rows",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "document_uploads",
                newName: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_template_definitions",
                table: "template_definitions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_reconcile_runs",
                table: "reconcile_runs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_reconcile_matches",
                table: "reconcile_matches",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_parsed_document_rows",
                table: "parsed_document_rows",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_document_uploads",
                table: "document_uploads",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_template_definitions",
                table: "template_definitions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_reconcile_runs",
                table: "reconcile_runs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_reconcile_matches",
                table: "reconcile_matches");

            migrationBuilder.DropPrimaryKey(
                name: "PK_parsed_document_rows",
                table: "parsed_document_rows");

            migrationBuilder.DropPrimaryKey(
                name: "PK_document_uploads",
                table: "document_uploads");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "template_definitions",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "reconcile_runs",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "reconcile_matches",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "parsed_document_rows",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "document_uploads",
                newName: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_template_definitions",
                table: "template_definitions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_reconcile_runs",
                table: "reconcile_runs",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_reconcile_matches",
                table: "reconcile_matches",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_parsed_document_rows",
                table: "parsed_document_rows",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_document_uploads",
                table: "document_uploads",
                column: "id");

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

            migrationBuilder.CreateIndex(
                name: "ix_reconcile_matches_reconcile_run_id",
                table: "reconcile_matches",
                column: "reconcile_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_parsed_document_rows_document_upload_id",
                table: "parsed_document_rows",
                column: "document_upload_id");
        }
    }
}
