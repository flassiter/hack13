namespace Hack13.EmailSender;

public sealed class EmailSenderEnvironmentConfig
{
    public string TemplateBasePath { get; set; } = "configs/templates";
    public long AttachmentSizeLimitBytes { get; set; } = 10 * 1024 * 1024;
    public TransportConfig Transport { get; set; } = new();
}

public sealed class TransportConfig
{
    public string Type { get; set; } = "mock";
    public SesTransportConfig Ses { get; set; } = new();
    public SmtpTransportConfig Smtp { get; set; } = new();
}

public sealed class SesTransportConfig
{
    public string Region { get; set; } = "us-east-1";
    public int MaxRetries { get; set; } = 3;
}

public sealed class SmtpTransportConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 25;
    public bool UseSsl { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}
