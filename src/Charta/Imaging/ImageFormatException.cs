namespace Charta.Imaging;

/// <summary>
/// Thrown when image data is malformed or unsupported. Like the font parser, the image decoders
/// guarantee arbitrary input produces either a successful decode or this exception.
/// </summary>
public sealed class ImageFormatException : Exception
{
    /// <summary>Initializes the exception without a message.</summary>
    public ImageFormatException()
    {
    }

    /// <summary>Initializes the exception with a message describing the malformed structure.</summary>
    public ImageFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception with a message and the underlying cause.</summary>
    public ImageFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
