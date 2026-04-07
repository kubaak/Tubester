using System.ComponentModel.DataAnnotations;

namespace Tubester.Application.Account;

public sealed class UpdateAccountSettingsRequest
{
    [Required]
    [MaxLength(50)]
    [AllowedValues("light", "dark", "system", ErrorMessage = "PreferredTheme must be 'light', 'dark', or 'system'.")]
    public string PreferredTheme { get; set; } = null!;

    [Required]
    [MaxLength(20)]
    public string PreferredLanguage { get; set; } = null!;
}
