# Event SQL Filtering

The Consume Provider window supports SQL-style filtering for captured events.
Use the `Mode` dropdown beside `Event filter` and select `SQL`.

This is a safe in-process expression parser. EtwSuite does not execute SQL,
does not query a database, and does not allow function calls or arbitrary
member access.

## Syntax

The filter accepts an optional leading `WHERE` followed by a boolean expression.

Supported operators:

- `AND`
- `OR`
- `NOT`
- Parentheses: `(` and `)`
- Comparisons: `=`, `!=`, `<>`, `<`, `<=`, `>`, `>=`
- Pattern matching: `LIKE`

String values use single quotes. Escape a single quote by doubling it:

```sql
event_name = 'User''s Event'
```

`LIKE` supports SQL wildcards and EtwSuite wildcards:

- `%` or `*` matches zero or more characters.
- `_` or `?` matches one character.

Matching is case-insensitive.

## Event Fields

Supported fields:

- `provider`
- `provider_id`
- `event` or `event_name`
- `id` or `event_id`
- `version`
- `opcode`
- `level`
- `pid` or `process_id`
- `process` or `process_name`
- `tid` or `thread_id`
- `payload`
- `payload.<fieldName>`

Numeric comparisons are supported for numeric fields such as `event_id`,
`level`, `pid`, and `thread_id`.

`payload` searches the combined payload names, types, and values.
`payload.<fieldName>` searches one named payload field, for example
`payload.ImageName`.

## Examples

Show one event ID:

```sql
event_id = 1
```

Show warnings and more severe events when lower level numbers represent higher
severity:

```sql
level <= 4
```

Find events from PowerShell:

```sql
process_name LIKE 'powershell%'
```

Find command execution by image path:

```sql
payload.ImageName LIKE '*cmd.exe'
```

Combine process and event criteria:

```sql
event_id = 1 AND process_name LIKE 'powershell%'
```

Use grouping:

```sql
(event_id = 1 OR event_id = 2) AND level <= 4
```

Use the optional `WHERE` prefix:

```sql
WHERE provider LIKE 'Microsoft-Windows-*' AND pid = 4242
```

Exclude noisy events:

```sql
NOT (event_name LIKE '*Verbose*' OR level > 5)
```

Search across all payload text:

```sql
payload LIKE '*whoami*'
```

## Basic Mode

`Basic` mode remains available for event filtering and provider search. It
matches text case-insensitively across the relevant fields. If the text contains
`*` or `?`, EtwSuite treats it as a wildcard pattern.
