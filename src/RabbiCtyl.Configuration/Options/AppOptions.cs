namespace RabbiCtyl.Configuration.Options;

public sealed class AppOptions
{
    public string Name { get; set; } = "RabbiCtyl";

    public string Url { get; set; } = "http://localhost:8080";

    public string Environment { get; set; } = "Production";

    public bool Debug { get; set; }

    public string Timezone { get; set; } = "UTC";

    public string Locale { get; set; } = "en";
}
