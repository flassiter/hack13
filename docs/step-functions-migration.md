# Step Functions Migration Notes

## Mapping

| Local Orchestrator | Step Functions Equivalent |
|---|---|
| Workflow definition JSON | Amazon States Language (ASL) |
| Sequential step execution | Ordered `Task` states |
| Step condition | `Choice` state before task |
| `on_failure: abort` | `Catch` -> `Fail` |
| `on_failure: retry` | `Retry` block on task |
| `on_failure: log_and_continue` | `Catch` -> next state |
| Component registry | Lambda ARN mapping table |
| Data dictionary | State input/output JSON |
| Workflow execution summary | Execution history + final state output |
| Frontend polling | `DescribeExecution` API |

## Lambda Strategy
- Keep each component implementation intact.
- Wrap each component in a thin Lambda handler that:
  1. maps event payload to `ComponentConfiguration` + data dictionary,
  2. runs component,
  3. returns `ComponentResult`.

## Error and Retry Guidance
- Use Step Functions `Retry` for transient exceptions.
- Convert component-level business failures into structured error payloads (do not throw for expected validation failures).
- Use `Catch` to route non-fatal component failures (equivalent to `log_and_continue`).

## Data Shape Guidance
- Keep state object close to current dictionary model (`{ "data": { ... } }`).
- Preserve reserved keys (`_workflow_id`, `_started_at`, `_step_name`, `_step_status`) for observability parity.

## Deployment Notes
- Replace local component registry with config-driven Lambda mapping (e.g., SSM or AppConfig).
- Use CloudWatch logs and X-Ray for per-step observability.
- Persist summaries to DynamoDB if historical query is required.
