# Hack13

A proof-of-concept Robotic Process Automation engine built on .NET 8. The system connects to legacy green-screen (TN5250/AS400) systems, extracts data through scripted workflows, and feeds it into downstream processing pipelines.

## Architecture

The solution follows a component-based design: each piece of work is a self-contained **component** that implements a shared interface, receives inputs via a data dictionary, and returns a structured result. An **orchestrator** chains components together according to a workflow definition.

```
┌─────────────────────────────────────────────────────────────┐
│                        Orchestrator                          │
│  reads WorkflowDefinition → sequences ComponentConfigs      │
└──────────────┬──────────────────────────────────────────────┘
               │ executes
   ┌───────────┼───────────────┬──────────────┐
   ▼           ▼               ▼              ▼
GreenScreen  Calculator  DecisionEngine  EmailSender ...
Connector
   │
   ▼ (TN5250)
 AS400 / MockServer
```

### Projects

| Project | Description |
|---|---|
| `Hack13.Contracts` | Shared interfaces, models, and utilities used by all components |
| `Hack13.TerminalClient` | TN5250 client — connects to AS400, runs scripted navigation workflows, scrapes screen data |
| `Hack13.TerminalServer` | Headless TN5250 server that simulates a mortgage servicing AS400 for testing |
| `Hack13.Calculator` | Numeric computation component |
| `Hack13.DecisionEngine` | Rule-based decision component |
| `Hack13.EmailSender` | Email delivery component |
| `Hack13.PdfGenerator` | PDF generation component |
| `Hack13.Database.Common` | Shared database connection factory (SQL Server, PostgreSQL, MySQL, SQLite) |
| `Hack13.DatabaseReader` | SQL database query component |
| `Hack13.DatabaseWriter` | SQL database write component (INSERT/UPDATE/DELETE and scalar queries) |
| `Hack13.HttpClient` | HTTP REST client component |
| `Hack13.ApprovalGate` | Human-in-the-loop approval polling component |
| `Hack13.Orchestrator` | Workflow execution engine |
| `Hack13.Api` | HTTP API surface |
| `Hack13.Cli` | Command-line runner |

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Building

```bash
dotnet build hack13.sln
```

## Running Tests

```bash
# All tests
dotnet test hack13.sln

# Specific test project
dotnet test tests/Hack13.TerminalClient.Tests/
dotnet test tests/Hack13.TerminalServer.Tests/
dotnet test tests/Hack13.Contracts.Tests/
```

## Running the End-to-End Workflow

### CLI

```bash
dotnet run --project src/Hack13.Cli -- \
  --workflow configs/workflows/escrow_statement_generation.json \
  --param loan_number=1000001 \
  --param host=localhost \
  --param screen_catalog_path=configs/screen-catalog \
  --param user_id=TESTUSER \
  --param password=TEST1234 \
  --param customer_email=demo@example.com
```

### API

```bash
dotnet run --project src/Hack13.Api
```

#### Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/workflows` | List all workflows with metadata (description, version, component types, PDF templates, last modified) |
| `POST` | `/api/workflows/{workflowId}/execute` | Execute a workflow synchronously — returns the full `WorkflowExecutionSummary` |
| `GET` | `/api/workflows/{workflowId}/execute-stream` | Execute a workflow with SSE streaming — emits `progress`, `summary`, and `workflow_error` events |
| `GET` | `/api/workflows/{workflowId}/definition` | Return the raw workflow JSON |
| `PUT` | `/api/workflows/{workflowId}/definition` | Overwrite the workflow JSON on disk (admin token required by default) |
| `GET` | `/api/workflows/{workflowId}/explain` | Generate a natural-language workflow explanation via Bedrock (admin token required by default) |
| `GET` | `/api/files/pdf?path=...` | Download a generated PDF from the `output/` directory |
| `GET` | `/health` | Health check |

`POST /api/workflows/{workflowId}/execute` accepts:

```json
{
  "parameters": {
    "loan_number": "1000001"
  }
}
```

`GET /api/workflows/{workflowId}/execute-stream` accepts workflow parameters as query-string key/value pairs and streams SSE events:

- `progress` — emitted as each step starts, retries, or completes: `{ stepName, componentType, state, attempt, maxAttempts, message }`
- `summary` — emitted at workflow completion: full `WorkflowExecutionSummary` JSON
- `workflow_error` — emitted on a fatal error before any result: `{ message }`

Security defaults:
- CORS is restricted to `Cors:AllowedOrigins` (defaults to local frontend origins).
- Mutating workflow definitions and calling explain require `Api:AdminToken` by default.
- Provide token via `X-Api-Key: <token>` or `Authorization: Bearer <token>`.

### Frontend

```bash
cd frontend
npm install
npm run dev
```

By default the frontend calls `/api/...`. Set `VITE_API_BASE_URL` for non-proxied environments.

The frontend has two pages accessible via the top nav:

- **Workflow Runner** — select a workflow from a dropdown (populated via `GET /api/workflows`), fill in its required parameters, and run it. Step progress is streamed in real time via SSE. On completion the result panel shows the final status, a PDF download link (if applicable), and email delivery status. Configuration errors in workflow files are surfaced inline.
- **Workflow Catalog** — a read-only browser showing all available workflows in a two-pane layout. The left pane lists workflows with description and step count; the right pane shows full metadata for the selected workflow (version, parameters, step names, component types, PDF templates, and a collapsible raw JSON view).

## Docker Compose (Optional)

```bash
docker compose up --build
```

Services:
- Mock server on `5250`
- API on `http://localhost:5000`
- smtp4dev UI on `http://localhost:3000`
- Frontend on `http://localhost:8080`

## Running the Mock Server

The mock server simulates a TN5250 mortgage servicing system. It loads screen definitions, navigation rules, and test loan data from the `configs/` and `test-data/` directories.

```bash
# From the project root
dotnet run --project src/Hack13.TerminalServer

# With a custom port (default: 5250)
dotnet run --project src/Hack13.TerminalServer -- . 5251
```

The server accepts connections from any TN5250 client. Three test loans are pre-loaded: `1000001`, `1000002`, `1000003`. Valid credentials: `TESTUSER/TEST1234`, `ADMIN/ADMIN123`, `RPA/RPA5250`.

## Component Contract

All components implement `IComponent`:

```csharp
public interface IComponent
{
    string ComponentType { get; }

    Task<ComponentResult> ExecuteAsync(
        ComponentConfiguration config,
        Dictionary<string, string> dataDictionary,
        CancellationToken cancellationToken = default);
}
```

**`ComponentConfiguration`** wraps a component type string and a `JsonElement Config` that holds component-specific settings.

**`Dictionary<string, string> dataDictionary`** is the shared data pipeline — inputs flow in and scraped outputs flow out, keyed by canonical field names.

**`ComponentResult`** contains:
- `Status` — `Success`, `Failure`, or `Skipped`
- `OutputData` — `Dictionary<string, string>` of produced values
- `Error` — `ErrorCode`, `ErrorMessage`, `StepDetail` on failure
- `LogEntries` — timestamped, leveled log messages for diagnostics
- `DurationMs` — wall-clock execution time

Placeholders in configuration strings use `{{key}}` syntax and are resolved against the data dictionary at runtime.

## GreenScreenConnector

Connects to a TN5250 host and executes a JSON-defined workflow of navigation, assertion, and scraping steps.

### Component type

```
green_screen_connector
```

### Configuration schema

```json
{
  "connection": {
    "host": "{{host}}",
    "port": 5250,
    "connect_timeout_seconds": 5,
    "response_timeout_seconds": 10
  },
  "screen_catalog_path": "{{screen_catalog_path}}",
  "steps": [ ... ]
}
```

`host` and `screen_catalog_path` support `{{placeholder}}` substitution from the data dictionary. `port` can also be overridden by supplying a `"port"` key in the data dictionary.

### Step types

#### `Navigate`

Fills input fields on the current screen, presses an AID key, and verifies the resulting screen.

```json
{
  "step_name": "enter_loan_number",
  "type": "Navigate",
  "fields": { "loan_number": "{{loan_number}}" },
  "aid_key": "Enter",
  "expect_screen": "loan_details"
}
```

Supported `aid_key` values: `Enter`, `F1`–`F12`, `PageUp`, `PageDown`, `Clear`, `Help`, `Print`.

#### `Assert`

Verifies that the current screen matches an expected ID and that no error text appears at a given row.

```json
{
  "step_name": "verify_loan_details",
  "type": "Assert",
  "expect_screen": "loan_details",
  "error_text": "Loan not found",
  "error_row": 24
}
```

#### `Scrape`

Reads named field values from the screen buffer at positions defined in the screen catalog.

```json
{
  "step_name": "scrape_loan_details",
  "type": "Scrape",
  "screen": "loan_details",
  "scrape_fields": ["borrower_name", "current_balance", "loan_status"]
}
```

All scraped field names are written into `OutputData` using the canonical names from the screen catalog.

#### `ForEach`

Iterates over a JSON-serialized list of rows stored in the data dictionary and executes a sequence of sub-steps for each row.

```json
{
  "step_name": "process_each_loan",
  "component_type": "foreach",
  "foreach": {
    "rows_key": "db_rows",
    "row_prefix": "loan_"
  },
  "on_failure": "log_and_continue",
  "sub_steps": [
    {
      "step_name": "lookup_escrow_data",
      "component_type": "green_screen_connector",
      "component_config": "../components/escrow_lookup.json",
      "on_failure": "abort"
    }
  ]
}
```

`rows_key` names the data dictionary key that holds the JSON array (e.g. produced by `DatabaseReader`). Each row's columns are merged into the dictionary with the optional `row_prefix` prepended. Sub-steps run sequentially for every row.

### Screen catalog

Screen definitions live in `configs/screen-catalog/` as individual JSON files. Each file describes a screen's identifier (row/col/text used to detect it) and its fields (name, type, position, length).

```json
{
  "screen_id": "loan_details",
  "identifier": { "row": 1, "col": 25, "expected_text": "Loan Details" },
  "fields": [
    { "name": "borrower_name", "type": "display", "row": 5, "col": 35, "length": 30 }
  ]
}
```

### Escrow lookup workflow

`configs/workflows/escrow-lookup.json` implements the full escrow data extraction workflow:

1. Sign on (user/password → `loan_inquiry`)
2. Assert sign-on succeeded
3. Enter loan number (→ `loan_details`)
4. Assert loan found
5. Scrape 10 loan detail fields
6. Navigate to escrow screen (`F6` → `escrow_analysis`)
7. Scrape 13 escrow fields

**Required data dictionary keys:** `host`, `screen_catalog_path`, `user_id`, `password`, `loan_number`

**Scraped output keys:**

| Field | Screen |
|---|---|
| `borrower_name`, `property_address`, `loan_type`, `original_amount`, `current_balance`, `interest_rate`, `monthly_payment`, `next_due_date`, `loan_status`, `origination_date` | Loan Details |
| `escrow_balance`, `escrow_payment`, `required_reserve`, `shortage_amount`, `surplus_amount`, `escrow_status`, `tax_amount`, `hazard_insurance`, `flood_insurance`, `mortgage_insurance`, `last_analysis_date`, `next_analysis_date`, `projected_balance` | Escrow Analysis |

## DatabaseReader

Executes a parameterized SQL query and writes the results into the data dictionary.

### Component type

```
database_reader
```

### Configuration schema

```json
{
  "provider": "sqlite",
  "connection_string": "{{db_connection_string}}",
  "query": "SELECT loan_number, principal_balance FROM Loans WHERE loan_type = @loan_type",
  "parameters": {
    "loan_type": "{{filter_loan_type}}"
  },
  "command_timeout_seconds": 30,
  "output_prefix": "",
  "require_row": false,
  "multi_row": false,
  "rows_output_key": "db_rows"
}
```

**`provider`** — database driver to use. Supported values: `sqlite`, `sqlserver` (SQL Server / Azure SQL).

**`connection_string`** supports `{{placeholder}}` substitution.

**`parameters`** — named query parameters; values support `{{placeholder}}` substitution. Parameter names are passed to the driver with a leading `@` if not already present.

**Single-row mode** (default, `multi_row: false`): reads the first row and writes each column as `{output_prefix}{column_name}` into the data dictionary.

**Multi-row mode** (`multi_row: true`): serializes all rows to a JSON array and writes it under `rows_output_key`. Intended for use with a `foreach` step downstream.

`db_row_count` is always written into the data dictionary with the number of rows returned.

If `require_row` is `true` and the query returns no rows, the component returns a `Failure` with code `NO_ROWS_RETURNED`.

## DatabaseWriter

Executes a parameterized SQL write command (INSERT, UPDATE, DELETE) or scalar query and writes the result into the data dictionary.

### Component type

```
database_writer
```

### Configuration schema

```json
{
  "provider": "sqlserver",
  "connection_string": "{{db_connection_string}}",
  "query": "UPDATE Loans SET bucket_id = @bucket_id WHERE loan_number = @loan_number",
  "parameters": {
    "bucket_id": "{{assigned_bucket_id}}",
    "loan_number": "{{loan_number}}"
  },
  "command_timeout_seconds": 30,
  "output_key": "rows_affected",
  "scalar": false
}
```

**`provider`** — same values as `DatabaseReader`: `sqlite`, `sqlserver`, `postgresql`, `mysql`.

**`query`** and **`parameters`** values support `{{placeholder}}` substitution.

**`scalar`** — if `true`, executes `ExecuteScalarAsync` and writes the single return value; if `false` (default), executes `ExecuteNonQueryAsync` and writes rows affected.

**`output_key`** — data dictionary key to write the result into; default `"rows_affected"`. `db_rows_affected` (integer) is always also written.

---

## HttpClient

Sends an HTTP request to an external REST API and maps response fields into the data dictionary.

### Component type

```
http_client
```

### Configuration schema

```json
{
  "method": "POST",
  "url": "{{letter_queue_api_url}}/letters",
  "headers": {
    "Authorization": "Bearer {{api_token}}",
    "Content-Type": "application/json"
  },
  "body": "{\"loanNumber\": \"{{loan_number}}\", \"template\": \"{{pdf_template}}\"}",
  "timeout_seconds": 30,
  "success_status_codes": [200, 201, 202],
  "response_field_map": {
    "approval_id": "$.id",
    "queue_status": "$.status"
  },
  "response_body_key": "http_response_body"
}
```

**`url`**, header values, and **`body`** support `{{placeholder}}` substitution.

**`success_status_codes`** — list of acceptable HTTP status codes; if empty, treats 2xx as success.

**`response_field_map`** — maps dot-notation JSON paths (e.g. `$.id`, `$.data.user.name`, `$.items[0]`) to data dictionary keys.

**`response_body_key`** — key to write the full raw response body under; default `"http_response_body"`.

`http_status_code` is always written to the data dictionary.

---

## ApprovalGate

Polls a REST endpoint on a fixed interval until an approved or rejected response is detected, or a timeout is reached. Used after an `http_client` step that creates the approval request.

### Component type

```
approval_gate
```

### Configuration schema

```json
{
  "poll_url": "{{approval_api_url}}/approvals/{{approval_id}}",
  "poll_method": "GET",
  "poll_headers": {
    "Authorization": "Bearer {{api_token}}"
  },
  "poll_interval_seconds": 30,
  "timeout_seconds": 86400,
  "approved_path": "status",
  "approved_value": "approved",
  "rejected_path": "status",
  "rejected_value": "rejected"
}
```

**`poll_url`** and header values support `{{placeholder}}` substitution — typically references an `approval_id` written by a prior `http_client` step.

**`approved_path`** / **`rejected_path`** — dot-notation JSON paths in the response body.

**`approved_value`** / **`rejected_value`** — compared case-insensitively.

Transient HTTP errors (non-2xx) are tolerated and retried. `approval_status` (`"approved"`, `"rejected"`, or `"timeout"`) and `approval_poll_count` are always written to the data dictionary.

### Typical workflow pattern

```
http_client  (POST → creates approval, maps $.id → approval_id)
    ↓
approval_gate  (polls GET /approvals/{{approval_id}})
    ↓
[downstream steps only run if approved]
```

---

## Shared Utilities

**`PlaceholderResolver`** — resolves `{{key}}` tokens in any string against a `Dictionary<string, string>`. Unresolved placeholders are left as-is.

**`NumericParser`** — parses financial strings to `decimal`, handling `$`, `£`, `€`, commas, and parentheses-as-negative (e.g. `"($1,234.56)"` → `-1234.56`).

**`DataDictionaryExtensions`** — typed getters/setters on `Dictionary<string, string>`: `GetRequired`, `GetOptional`, `GetDecimal`, `GetInt`, `GetBool`, `Set`.

## Project Layout

```
hack13.sln
├── configs/
│   ├── screen-catalog/          # TN5250 screen definitions (JSON)
│   ├── workflows/               # Connector workflow definitions
│   └── navigation.json          # Mock server screen transition rules
├── test-data/
│   └── loans.json               # Three test mortgage loans
├── src/
│   ├── Hack13.Contracts/        # Shared interfaces and models
│   ├── Hack13.TerminalClient/
│   │   ├── Protocol/            # TN5250 protocol layer
│   │   ├── Screen/              # Screen identification
│   │   └── Workflow/            # Workflow engine and config models
│   ├── Hack13.TerminalServer/
│   │   ├── Protocol/            # TN5250 server-side protocol
│   │   ├── Engine/              # Screen rendering and field extraction
│   │   ├── Navigation/          # Session state and transition rules
│   │   └── Server/              # TCP server and client session handling
│   ├── Hack13.DatabaseReader/   # SQL database query component
│   └── ...                      # Additional component projects
└── tests/
    ├── Hack13.Contracts.Tests/
    ├── Hack13.TerminalClient.Tests/
    ├── Hack13.TerminalServer.Tests/
    └── ...
```
