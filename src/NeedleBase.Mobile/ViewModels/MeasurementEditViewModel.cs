using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Measurements;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

public partial class MeasurementEditViewModel : BaseViewModel, IQueryAttributable
{
    private const string SavedCopy = "Saved on this device. Will sync when you're online.";
    private const decimal CmPerInch = 2.54m;

    private readonly IMeasurementLocalService _measurements;
    private readonly IAuthService _auth;
    private bool _initialized;
    private bool _suppressUnitConversion;
    private DateTimeOffset? _originalMeasuredAtUtc;

    /// <summary>True once create template is ready or edit load succeeded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool readyToSave;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    private bool isSaving;

    public bool CanSave => !IsBusy && ReadyToSave;
    public bool CanGoBack => !IsSaving;

    protected override void OnBusyChanged() => OnPropertyChanged(nameof(CanSave));

    public MeasurementEditViewModel(IMeasurementLocalService measurements, IAuthService auth)
    {
        _measurements = measurements;
        _auth = auth;
        Title = "Measurements";
        foreach (var t in GarmentTemplate.All)
        {
            TemplateChoices.Add(new TemplateChoice(t, GarmentTemplate.DisplayName(t)));
        }
    }

    public string CustomerId { get; private set; } = string.Empty;
    public string MeasurementId { get; private set; } = string.Empty;
    public string Template { get; private set; } = GarmentTemplate.Dress;

    public ObservableCollection<TemplateChoice> TemplateChoices { get; } = new();
    public ObservableCollection<MeasurementFieldItem> Fields { get; } = new();

    [ObservableProperty]
    private TemplateChoice? selectedTemplate;

    [ObservableProperty]
    private string unit = MeasurementUnit.Centimetres;

    [ObservableProperty]
    private string notes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private bool useInches;

    [ObservableProperty]
    private string unitLabel = "cm";

    [ObservableProperty]
    private bool fieldsEnabled = true;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    private void SetFieldsEnabled(bool enabled)
    {
        FieldsEnabled = enabled;
        foreach (var field in Fields)
        {
            field.IsEnabled = enabled;
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("customerId", out var customer) && customer is not null)
        {
            CustomerId = Uri.UnescapeDataString(customer.ToString() ?? string.Empty);
        }

        if (query.TryGetValue("measurementId", out var measurement) && measurement is not null)
        {
            MeasurementId = Uri.UnescapeDataString(measurement.ToString() ?? string.Empty);
        }

        if (query.TryGetValue("template", out var template) && template is not null)
        {
            Template = Uri.UnescapeDataString(template.ToString() ?? GarmentTemplate.Dress);
        }

        MainThread.BeginInvokeOnMainThread(async () => await InitializeAsync());
    }

    partial void OnUseInchesChanged(bool value)
    {
        var previousUnit = Unit;
        var nextUnit = value ? MeasurementUnit.Inches : MeasurementUnit.Centimetres;
        Unit = nextUnit;
        UnitLabel = nextUnit;
        ApplyUnitSuffixToFields();

        if (!_initialized || _suppressUnitConversion || previousUnit == nextUnit)
        {
            return;
        }

        ConvertFieldValues(previousUnit, nextUnit);
    }

    partial void OnSelectedTemplateChanged(TemplateChoice? value)
    {
        if (value is null || !_initialized)
        {
            return;
        }

        RebuildFields(value.Key, keepValues: true);
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        ReadyToSave = false;
        var templateKey = string.IsNullOrWhiteSpace(Template) ? GarmentTemplate.Dress : Template;
        SelectedTemplate = TemplateChoices.FirstOrDefault(t => t.Key == templateKey)
                           ?? TemplateChoices.First();

        if (!string.IsNullOrWhiteSpace(MeasurementId))
        {
            Title = "Edit measurements";
            IsBusy = true;
            SetFieldsEnabled(false);
            try
            {
                var businessId = await _auth.GetBusinessIdAsync();
                if (string.IsNullOrWhiteSpace(businessId))
                {
                    ErrorMessage = "Missing business session.";
                    return;
                }

                var detail = await _measurements.GetByIdAsync(businessId, MeasurementId);
                if (detail is null)
                {
                    ErrorMessage = "Measurement not found on this device.";
                    return;
                }

                _originalMeasuredAtUtc = detail.MeasuredAtUtc;
                SelectedTemplate = TemplateChoices.FirstOrDefault(t => t.Key == detail.GarmentTemplate)
                                   ?? SelectedTemplate;
                _suppressUnitConversion = true;
                Unit = detail.Unit;
                UnitLabel = detail.Unit;
                UseInches = detail.Unit == MeasurementUnit.Inches;
                _suppressUnitConversion = false;
                Notes = detail.Notes ?? string.Empty;
                RebuildFields(detail.GarmentTemplate, keepValues: false, detail.Fields);
                ReadyToSave = true;
            }
            finally
            {
                IsBusy = false;
                // Keep form locked when edit load failed so Save cannot silently no-op.
                SetFieldsEnabled(ReadyToSave);
            }

            return;
        }

        Title = "New measurements";
        UnitLabel = Unit;
        RebuildFields(SelectedTemplate!.Key, keepValues: false);
        ReadyToSave = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy || !ReadyToSave)
        {
            if (!ReadyToSave && !IsBusy)
            {
                ErrorMessage ??= "Couldn't load this measurement. Go back and try again.";
            }

            return;
        }

        ErrorMessage = null;
        if (SelectedTemplate is null || string.IsNullOrWhiteSpace(CustomerId))
        {
            ErrorMessage = "Pick a garment template.";
            return;
        }

        // Snapshot fields before disabling edits during save.
        var fieldMap = new Dictionary<string, decimal?>();
        foreach (var field in Fields)
        {
            if (string.IsNullOrWhiteSpace(field.ValueText))
            {
                fieldMap[field.Key] = null;
                continue;
            }

            if (!decimal.TryParse(field.ValueText, out var number))
            {
                ErrorMessage = $"{field.Label} must be a number.";
                return;
            }

            fieldMap[field.Key] = number;
        }

        IsBusy = true;
        IsSaving = true;
        SetFieldsEnabled(false);
        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            if (string.IsNullOrWhiteSpace(businessId))
            {
                ErrorMessage = "Missing business session.";
                return;
            }

            var request = new CreateMeasurementRequest
            {
                Id = string.IsNullOrWhiteSpace(MeasurementId) ? null : MeasurementId,
                GarmentTemplate = SelectedTemplate.Key,
                Unit = Unit,
                Fields = fieldMap,
                Notes = Notes,
                // New: now. Edit: preserve original history date.
                MeasuredAtUtc = string.IsNullOrWhiteSpace(MeasurementId)
                    ? DateTimeOffset.UtcNow
                    : _originalMeasuredAtUtc
            };

            var (saved, error) = await _measurements.SaveAsync(businessId, CustomerId, request);
            if (error is not null || saved is null)
            {
                ErrorMessage = error ?? "Could not save measurements.";
                return;
            }

            await Shell.Current.DisplayAlert("Saved", SavedCopy, "OK");
            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            IsBusy = false;
            IsSaving = false;
            SetFieldsEnabled(true);
        }
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (IsSaving)
        {
            return;
        }

        await Shell.Current.GoToAsync("..");
    }

    private void ConvertFieldValues(string fromUnit, string toUnit)
    {
        foreach (var field in Fields)
        {
            if (string.IsNullOrWhiteSpace(field.ValueText) ||
                !decimal.TryParse(field.ValueText, out var value))
            {
                continue;
            }

            var converted = fromUnit == MeasurementUnit.Centimetres && toUnit == MeasurementUnit.Inches
                ? value / CmPerInch
                : fromUnit == MeasurementUnit.Inches && toUnit == MeasurementUnit.Centimetres
                    ? value * CmPerInch
                    : value;
            field.ValueText = converted.ToString("0.##");
        }
    }

    private void RebuildFields(
        string templateKey,
        bool keepValues,
        IReadOnlyDictionary<string, decimal?>? seed = null)
    {
        Dictionary<string, string>? previous = null;
        if (keepValues)
        {
            previous = Fields.ToDictionary(f => f.Key, f => f.ValueText);
        }

        Fields.Clear();
        foreach (var key in GarmentTemplateFields.GetFields(templateKey))
        {
            var text = string.Empty;
            if (seed is not null && seed.TryGetValue(key, out var seeded) && seeded is not null)
            {
                text = seeded.Value.ToString("0.##");
            }
            else if (previous is not null && previous.TryGetValue(key, out var prior))
            {
                text = prior;
            }

            Fields.Add(new MeasurementFieldItem(key, GarmentTemplateFields.DisplayLabel(key), text, Unit, FieldsEnabled));
        }
    }

    private void ApplyUnitSuffixToFields()
    {
        foreach (var field in Fields)
        {
            field.UnitSuffix = Unit;
            field.IsEnabled = FieldsEnabled;
        }
    }
}

public sealed class TemplateChoice(string key, string displayName)
{
    public string Key { get; } = key;
    public string DisplayName { get; } = displayName;
    public override string ToString() => DisplayName;
}

public partial class MeasurementFieldItem : ObservableObject
{
    public MeasurementFieldItem(string key, string label, string valueText, string unitSuffix, bool isEnabled)
    {
        Key = key;
        Label = label;
        ValueText = valueText;
        UnitSuffix = unitSuffix;
        IsEnabled = isEnabled;
    }

    public string Key { get; }
    public string Label { get; }

    [ObservableProperty]
    private string valueText;

    [ObservableProperty]
    private string unitSuffix;

    [ObservableProperty]
    private bool isEnabled = true;
}
