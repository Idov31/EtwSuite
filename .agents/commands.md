# Commands

Use commands that match the current solution layout. Prefer local project files
over assumptions; inspect `EtwSuite.sln` and project files when in doubt.

## Discovery

```powershell
rg --files
dotnet sln EtwSuite.sln list
dotnet --info
```

## Build

```powershell
dotnet build EtwSuite.sln
dotnet build EtwSuite.sln -c Release
```

WinUI/Windows App SDK builds may require Windows workloads and Visual Studio
components installed on the machine.

## Test

```powershell
dotnet test EtwSuite.sln
dotnet test EtwSuite.sln --filter "Category!=Integration"
```

Do not require administrator privileges for normal unit tests. Mark live ETW or
admin-dependent tests as integration tests.

## Useful Searches

```powershell
rg "TraceEvent|TraceEventSession|Tdh|StartTrace|EnableTrace" .
rg "Result|Wait\\(" .
rg "XmlReaderSettings|DtdProcessing|XmlResolver" .
rg "ObservableCollection|ItemsSource|ListView|DataGrid" .
```

