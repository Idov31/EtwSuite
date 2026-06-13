using EtwSuite.Core;
using System.Collections.ObjectModel;

namespace EtwSuite.ViewModels;

public sealed class ProviderDetailsViewModel : ObservableObject
{
    private IReadOnlyList<EtwSchemaEvent> _allEvents = Array.Empty<EtwSchemaEvent>();
    private string _schemaSearchText = string.Empty;
    private bool _isSchemaLoading;
    private string? _schemaStatus;

    public ProviderDetailsViewModel(EtwProviderInfo provider)
    {
        Name = provider.Name;
        Id = provider.Id;
        SchemaSource = FormatSchemaSource(provider.SchemaSource);
    }

    public string Name { get; }

    public Guid Id { get; }

    public string SchemaSource { get; }

    public string IdText => Id.ToString("D");

    public ObservableCollection<SchemaEventViewModel> SchemaItems { get; } = new();

    public string SchemaSearchText
    {
        get => _schemaSearchText;
        set
        {
            if (SetProperty(ref _schemaSearchText, value))
            {
                ApplySchemaFilter();
            }
        }
    }

    public bool IsSchemaLoading
    {
        get => _isSchemaLoading;
        private set
        {
            if (SetProperty(ref _isSchemaLoading, value))
            {
                OnPropertyChanged(nameof(SchemaCountText));
            }
        }
    }

    public string? SchemaStatus
    {
        get => _schemaStatus;
        private set => SetProperty(ref _schemaStatus, value);
    }

    public string SchemaCountText => IsSchemaLoading
        ? "Loading schema..."
        : $"{SchemaItems.Count:N0} events/tasks";

    public void SetSchema(EtwProviderSchema schema)
    {
        _allEvents = schema.Events;
        SchemaStatus = schema.Diagnostics.Count == 0
            ? null
            : string.Join(Environment.NewLine, schema.Diagnostics);

        ApplySchemaFilter();
    }

    public void BeginSchemaLoad()
    {
        IsSchemaLoading = true;
        SchemaStatus = null;
        _allEvents = Array.Empty<EtwSchemaEvent>();
        SchemaItems.Clear();
        OnPropertyChanged(nameof(SchemaCountText));
    }

    public void EndSchemaLoad()
    {
        IsSchemaLoading = false;
        OnPropertyChanged(nameof(SchemaCountText));
    }

    public void SetSchemaError(string message)
    {
        _allEvents = Array.Empty<EtwSchemaEvent>();
        SchemaItems.Clear();
        SchemaStatus = message;
        OnPropertyChanged(nameof(SchemaCountText));
    }

    private static string FormatSchemaSource(EtwProviderSchemaSource schemaSource)
    {
        return schemaSource switch
        {
            EtwProviderSchemaSource.XmlManifest => "XML manifest",
            EtwProviderSchemaSource.Wbem => "WMI/MOF",
            EtwProviderSchemaSource.Wpp => "WPP",
            EtwProviderSchemaSource.TraceLogging => "TraceLogging",
            _ => "Unknown"
        };
    }

    private void ApplySchemaFilter()
    {
        string searchText = SchemaSearchText.Trim();
        IEnumerable<EtwSchemaEvent> filteredEvents = _allEvents;

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            filteredEvents = filteredEvents.Where(schemaEvent =>
                schemaEvent.Name.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                schemaEvent.Id.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                schemaEvent.Version.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                schemaEvent.Opcode.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                schemaEvent.Level.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                schemaEvent.Parameters.Any(parameter =>
                    parameter.Name.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                    parameter.Type.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)));
        }

        SchemaItems.Clear();
        foreach (EtwSchemaEvent schemaEvent in filteredEvents)
        {
            SchemaItems.Add(new SchemaEventViewModel(schemaEvent));
        }

        OnPropertyChanged(nameof(SchemaCountText));
    }
}

public sealed class SchemaEventViewModel
{
    public SchemaEventViewModel(EtwSchemaEvent schemaEvent)
    {
        Name = schemaEvent.Name;
        Id = schemaEvent.Id;
        Version = schemaEvent.Version;
        Opcode = schemaEvent.Opcode;
        Level = schemaEvent.Level;
        Parameters = new ObservableCollection<EtwSchemaParameter>(schemaEvent.Parameters);
    }

    public string Name { get; }

    public ushort Id { get; }

    public byte Version { get; }

    public string Opcode { get; }

    public string Level { get; }

    public ObservableCollection<EtwSchemaParameter> Parameters { get; }

    public string ParameterCountText => $"{Parameters.Count:N0} parameters";
}
