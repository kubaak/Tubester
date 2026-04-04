using System.ComponentModel.DataAnnotations;

namespace Tubester.Application.Channels;

public sealed class UpdateChannelSettingsRequest
{
    public bool IsCommentAssistantEnabled { get; set; }

    public bool IsSuggestRepliesForTopLevelCommentsOnly { get; set; }

    [Range(0, int.MaxValue)]
    public int MaxSuggestedRepliesPerSync { get; set; }

    [Range(0, int.MaxValue)]
    public int MaxCommentAgeDays { get; set; }

    [Required]
    [MaxLength(50)]
    public string ReplyLanguage { get; set; } = null!;

    [MaxLength(500)]
    public string? ResponseForNonTextualComments { get; set; }
}
