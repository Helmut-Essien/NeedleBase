namespace NeedleBase.Mobile.Configuration;

public sealed class ApiSettings
{
    public const string SectionName = "Api";

    /// <summary>Primary API base URL (must end with /).</summary>
    public string BaseUrl { get; set; } = "https://10.0.2.2:7079/";
}
