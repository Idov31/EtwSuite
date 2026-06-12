# Roadmap

Recommended MVP order:

1. Provider listing. DONE.
2. Provider details page. DONE.
3. XML manifest parser. DONE.
4. Basic trace session creation. DONE.
5. Enable provider by GUID/name. DONE.
6. Live event view. DONE.
7. Basic filtering for the provider consuming window. DONE.
8. Export live events to JSON. DONE.
9. Decode ETL and EVTX events. DONE.
10. Open ETL or EVTX files and generated JSON, CSV. DONE.
11. Save and allow to delete session templates (providers + filters) in local SQLite database. Make a new tab for it. DONE.
12. Support SQL for filtering (provider consuming window) and wildcard matching (provider listing + basic search on provider consuming window). DONE.
13. Add right click option for the provider listing to open the provider in the provider consuming window with a filter for that provider. DONE.
14. Add logo. DONE.
15. Create a CI to build and publish the app to GitHub releases. DONE.

Optimize for correct ETW behavior, stable abstractions, good diagnostics,
native Windows integration, testable backend logic, responsive UI under high
event volume, and explicit handling of incomplete metadata and access
restrictions.
