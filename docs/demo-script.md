# Demo Script

## 1. Architecture (2 min)
- Show the component diagram in `README.md`.
- Explain the contract: every component implements `IComponent` and exchanges data through `Dictionary<string,string>`.
- Highlight orchestrator behavior: workflow JSON, step conditions, failure policy, retries.

## 2. Configuration-Driven Workflow (2 min)
- Open `configs/workflows/escrow_statement_generation.json`.
- Open `configs/components/escrow_notice_decision.json` and show rules drive template selection.
- Open `configs/components/escrow_email_config.json` and show placeholder use (`{{loan_number}}`, `{{pdf_file_path}}`).
- Point out that swapping workflow/component JSON requires no orchestrator code change.

## 3. Live Happy Path (loan 1000001) (3 min)
1. Start services:
   - `dotnet run --project src/Hack13.TerminalServer`
   - `dotnet run --project src/Hack13.Api`
   - `cd frontend && npm install && npm run dev`
2. Open the frontend and run with loan `1000001`.
3. Narrate step progression and show final success state.
4. Show generated PDF path from result panel and confirm email status.

## 4. Alternate Data Path (loan 1000002) (2 min)
- Run again with loan `1000002`.
- Show different decision output (`notice_type`, `pdf_template`) in final data dictionary / API response.

## 5. Error Handling Demo (invalid loan) (2 min)
- Run with loan `9999999`.
- Show clean failure in UI including failed step and error code/message.
- Explain `on_failure` behavior from workflow config.

## 6. Extensibility (1 min)
- Explain: add a new workflow by adding JSON files + templates.
- No orchestrator code changes are needed when reusing existing component types.

## 7. Production Path (2 min)
- Open `docs/step-functions-migration.md`.
- Walk through local orchestrator-to-Step Functions mapping.

## Presenter Checklist
- Pre-run one successful flow to warm PDF browser download.
- Keep mock server and API logs visible in separate terminals.
- Have smtp4dev open if using SMTP transport.
