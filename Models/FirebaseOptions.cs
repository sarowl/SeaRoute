namespace LadingSystem.Models;

// Public web-app configuration; never put service-account keys in this class.
public sealed class FirebaseOptions
{
    public string ProjectId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string AuthDomain { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string MessagingSenderId { get; set; } = string.Empty;
    public string StorageBucket { get; set; } = string.Empty;
}
