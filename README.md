# RPA POC

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

`POST /api/workflows/{workflowId}/execute` accepts:

```json
{
  "parameters": {
    "loan_number": "1000001"
  }
}
```

Health endpoint: `GET /health`

### Frontend

```bash
cd frontend
npm install
npm run dev
```

By default the frontend calls `/api/...`. Set `VITE_API_BASE_URL` for non-proxied environments.

## Docker Compose (Optional)

```bash
docker compose up --build
```

Services:
- Mock server on `5250`
- API on `http://localhost:5000`
- smtp4dev UI on `http://localhost:3000`
- Frontend on `http://localhost:8080`

## Demo and Migration Notes

- Demo runbook: `docs/demo-script.md`
- Step Functions mapping notes: `docs/step-functions-migration.md`

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
│   └── ...                      # Additional component projects
└── tests/
    ├── Hack13.Contracts.Tests/
    ├── Hack13.TerminalClient.Tests/
    ├── Hack13.TerminalServer.Tests/
    └── ...
```
