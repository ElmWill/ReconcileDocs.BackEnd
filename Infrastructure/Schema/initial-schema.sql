CREATE TABLE document_uploads (
    id uuid PRIMARY KEY,
    document_kind integer NOT NULL,
    original_file_name character varying(260) NOT NULL,
    content_type character varying(100) NOT NULL,
    stored_file_name character varying(260) NOT NULL,
    storage_path character varying(500) NOT NULL,
    sha256 character varying(64) NOT NULL,
    size_bytes bigint NOT NULL,
    uploaded_at_utc timestamp with time zone NOT NULL,
    reconcile_status integer NOT NULL
);

CREATE TABLE template_definitions (
    id uuid PRIMARY KEY,
    name character varying(200) NOT NULL,
    document_kind integer NOT NULL,
    parser_key character varying(200) NOT NULL,
    configuration_json character varying(2000) NOT NULL,
    is_active boolean NOT NULL,
    created_at_utc timestamp with time zone NOT NULL
);

CREATE TABLE parsed_document_rows (
    id uuid PRIMARY KEY,
    document_upload_id uuid NOT NULL,
    row_number integer NOT NULL,
    description character varying(500) NOT NULL,
    amount numeric NOT NULL,
    transaction_date date NULL,
    source_parser character varying(100) NOT NULL
);

CREATE TABLE reconcile_runs (
    id uuid PRIMARY KEY,
    spreadsheet_upload_id uuid NOT NULL,
    statement_upload_id uuid NOT NULL,
    template_definition_id uuid NULL,
    parser_name character varying(100) NOT NULL,
    matched_count integer NOT NULL,
    unmatched_count integer NOT NULL,
    error_count integer NOT NULL,
    status integer NOT NULL,
    error_message character varying(500) NULL,
    started_at_utc timestamp with time zone NOT NULL,
    completed_at_utc timestamp with time zone NULL
);

CREATE TABLE reconcile_matches (
    id uuid PRIMARY KEY,
    reconcile_run_id uuid NOT NULL,
    spreadsheet_row_number integer NOT NULL,
    statement_row_number integer NOT NULL,
    description character varying(500) NOT NULL,
    amount numeric NOT NULL,
    is_matched boolean NOT NULL
);

CREATE INDEX ix_parsed_document_rows_document_upload_id ON parsed_document_rows (document_upload_id);
CREATE INDEX ix_reconcile_runs_spreadsheet_upload_id ON reconcile_runs (spreadsheet_upload_id);
CREATE INDEX ix_reconcile_runs_statement_upload_id ON reconcile_runs (statement_upload_id);
CREATE INDEX ix_reconcile_runs_template_definition_id ON reconcile_runs (template_definition_id);
CREATE INDEX ix_reconcile_matches_reconcile_run_id ON reconcile_matches (reconcile_run_id);