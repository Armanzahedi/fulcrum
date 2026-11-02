namespace Fulcrum.Tests;

public class ContentTests
{
    [Fact]
    public void String_converts_implicitly_with_no_images()
    {
        Content c = "hello";

        Assert.Equal("hello", c.Text);
        Assert.Empty(c.Images);
    }

    [Fact]
    public void From_then_WithImage_keeps_text_and_adds_one_image()
    {
        var original = Content.From("text");

        var c = original.WithImage(new byte[] { 1, 2 }, "image/png");

        Assert.Equal("text", c.Text);
        var image = Assert.Single(c.Images);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal(new byte[] { 1, 2 }, image.Bytes!.Value.ToArray());
        Assert.Empty(original.Images);
    }

    [Fact]
    public void Image_factory_has_empty_text()
    {
        var c = Content.Image(new Uri("https://example.com/a.png"));

        Assert.Equal(string.Empty, c.Text);
        Assert.Equal(new Uri("https://example.com/a.png"), Assert.Single(c.Images).Url);
    }

    [Fact]
    public void Image_with_bytes_requires_a_media_type()
    {
        Assert.Throws<ArgumentException>(() => new Image(new byte[] { 1 }, null, null));
        Assert.Throws<ArgumentException>(() => new Image(new byte[] { 1 }, " ", null));
    }

    [Fact]
    public void Image_record_requires_exactly_one_of_bytes_or_url()
    {
        Assert.Throws<ArgumentException>(() => new Image(new byte[] { 1 }, "image/png", new Uri("https://example.com/a.png")));
        Assert.Throws<ArgumentException>(() => new Image(null, null, null));
    }
}
