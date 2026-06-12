# Recordings

EtwSuite currently supports opening these recording/export files:

- `.etl`: native ETW trace files read with TraceEvent.
- `.json`: JSON files exported by EtwSuite.
- `.csv`: CSV files exported by EtwSuite.

`.evtx` and other file types are not supported yet. The Open Recording tab reports:

```text
This file type is not supported yet. Supported: .etl, .json, .csv.
```

## ETL Recording

ETL export is a real ETW recording captured while consuming a provider. EtwSuite does not synthesize ETL files from decoded event rows.

To create an ETL file:

1. Open Consume Provider.
2. Select a provider.
3. Enable `Record ETL`.
4. Start consuming and choose the `.etl` destination path.
5. Stop consuming to close the trace file.

After stopping, use `Open recorded` or open the `.etl` file from the Open Recording tab.

If a session was not recorded to ETL, ETL export reports:

```text
ETL export requires recording to ETL while consuming.
```

## Open Recording

Open Recording loads supported files into the same event table shape used by live consumption. ETL payload decoding is best-effort because provider metadata can be incomplete or unavailable.

The event filter supports the same Basic and SQL modes as the live consumed-event filter. Filtering changes the visible event snapshot only; it does not mutate the loaded recording buffer.
