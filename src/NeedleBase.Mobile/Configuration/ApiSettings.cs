namespace NeedleBase.Mobile.Configuration;

public sealed class ApiSettings
{
    public const string SectionName = "Api";

    /// <summary>Primary API base URL (must end with /).</summary>
    public string BaseUrl { get; set; } = "http://localhost:5036/";
}
