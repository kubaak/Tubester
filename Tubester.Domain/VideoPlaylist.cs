namespace Tubester.Domain;

public sealed class VideoPlaylist
{
    public string VideoId { get; private set; } = default!;
    public string PlaylistId { get; private set; } = default!;

    public static VideoPlaylist Create(string videoId, string playlistId)
    {
        return new VideoPlaylist
        {
            VideoId = videoId,
            PlaylistId = playlistId
        };
    }

    private VideoPlaylist()
    {
    }
}