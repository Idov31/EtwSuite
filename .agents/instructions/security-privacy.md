# Security, Privacy, Logging, and Packaging

## Privacy

ETW data can contain sensitive information. Treat event payloads as sensitive.

Do not automatically upload, transmit, or send ETW data anywhere. Do not add
telemetry, crash upload, cloud sync, or remote diagnostics without explicit
project approval.

When exporting events:

- Make the output path explicit.
- Avoid overwriting files without confirmation.
- Preserve data faithfully.
- Do not redact unless the user explicitly requests redaction.
- If redaction is implemented, make it deterministic and visible.

## Permissions

Some ETW operations require administrator privileges or tracing/logging group
membership. Detect access denied errors and return structured errors with
actionable messages. Do not automatically elevate without explicit user action.

Basic browsing/parsing tasks such as opening manifests or viewing cached
metadata should not require administrator privileges.

## Logging

Use structured logging for:

- App startup and shutdown.
- ETW session creation and disposal.
- Provider enable/disable operations.
- Native API failures.
- Manifest parsing failures.
- ETL load failures.
- Export operations.
- Unexpected exceptions.

Do not log excessive live ETW event payloads by default. They can be very large
and sensitive.

## Packaging

Use Windows App SDK packaging appropriate for a WinUI 3 desktop app. Be careful
with capabilities, elevation, and deployment assumptions. Support non-admin
functionality where practical and explain when elevation is required.

