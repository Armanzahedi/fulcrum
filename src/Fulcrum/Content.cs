namespace Fulcrum;

public sealed class Content
{
    private Content(string text, IReadOnlyList<Image> images)
    {
        Text = text;
        Images = images;
    }

    public string Text { get; }

    public IReadOnlyList<Image> Images { get; }

    public static Content From(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new(text, []);
    }

    public static Content Image(ReadOnlyMemory<byte> bytes, string mediaType) => From(string.Empty).WithImage(bytes, mediaType);

    public static Content Image(Uri url) => From(string.Empty).WithImage(url);

    public Content WithImage(ReadOnlyMemory<byte> bytes, string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        return new(Text, [.. Images, new Image(bytes, mediaType, null)]);
    }

    public Content WithImage(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return new(Text, [.. Images, new Image(null, null, url)]);
    }

    public static implicit operator Content(string text) => From(text);
}

public sealed record Image
{
    internal Image(ReadOnlyMemory<byte>? bytes, string? mediaType, Uri? url)
    {
        if (bytes.HasValue == (url is not null))
            throw new ArgumentException("An image has exactly one of Bytes or Url.");
        if (bytes.HasValue && string.IsNullOrWhiteSpace(mediaType))
            throw new ArgumentException("An image given as bytes needs a media type.");
        Bytes = bytes;
        MediaType = mediaType;
        Url = url;
    }

    public ReadOnlyMemory<byte>? Bytes { get; }

    public string? MediaType { get; }

    public Uri? Url { get; }
}
