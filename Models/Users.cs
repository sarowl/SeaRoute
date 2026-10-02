using System.ComponentModel.DataAnnotations;

namespace LadingSystem.Models;

public class User
{
    public int Id { get; set; }

    [Required]
    public string FirebaseUid { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string FullName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}