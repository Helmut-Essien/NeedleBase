# Validation UX

Rule (from [handoff.md](handoff.md)): validate on submit, show inline field errors, clear an error as soon as the user edits that field, disable double-submit.

## Mobile pattern (MVVM)

ViewModel: one `XError` string + `HasXError` bool per field, cleared in the generated partial:

```csharp
// CustomerEditViewModel.cs
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(HasNameError))]
private string? nameError;

public bool HasNameError => !string.IsNullOrWhiteSpace(NameError);

partial void OnNameChanged(string value) => NameError = null;

// on save:
NameError = string.IsNullOrWhiteSpace(Name) ? "Name is required." : null;
if (HasNameError || HasPhoneError) return;
```

In use: `CustomerEditViewModel` (`NameError`, `PhoneError` — format via `PhoneNumbers.Check`, only for a new or changed number; local/server `RecordError.PhoneInvalid` (`PHONE_INVALID`) maps to `PhoneError` by code, never by message text), `MeasurementFieldItem` (`Error`, `HasError`), Register (incl. `TermsError`).

XAML: error label directly under the field; field Border turns red.

```xml
<Label Text="{Binding NameError}" IsVisible="{Binding HasNameError}"
       TextColor="{StaticResource SemanticError}" FontSize="13"
       LineBreakMode="WordWrap" />
```

```xml
<Border.Triggers>
    <DataTrigger TargetType="Border" Binding="{Binding HasEmailError}" Value="True">
        <Setter Property="Stroke" Value="{StaticResource SemanticError}" />
    </DataTrigger>
</Border.Triggers>
```

## Copy

- Say what to do, not what went wrong: "Enter a phone number", "Passwords don't match".
- No codes, no stack text. Server/network failure → page-level message ("Couldn't sign in — check your connection and try again"), not a field error.
- Terms gate: block submit with an inline error under the checkbox, never a silent disabled button.

## Web

`<span asp-validation-for="Input.Email" class="text-danger small nb-auth-error"></span>` under each input (Login/Register.cshtml); error colour `--nb-error`. Keep message wording identical to mobile.

## Checklist

- [ ] Every required field has an inline error, announced (label text, not colour alone)
- [ ] Error clears on edit
- [ ] Primary button guarded by `IsBusy`; shows progress
- [ ] Offline save ≠ validation success copy — see [offline-sync.md](offline-sync.md)
