namespace Charta.Fonts;

/// <summary>
/// Thrown when font data is malformed or unsupported. The font parser guarantees that arbitrary input
/// produces either a successful parse or this exception — never corruption, hangs, or unbounded allocation.
/// </summary>
public sealed class FontFormatException : Exception
{
    /// <summary>Initializes the exception without a message.</summary>
    public FontFormatException()
    {
    }

    /// <summary>Initializes the exception with a message describing the malformed structure.</summary>
    public FontFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception with a message and the underlying cause.</summary>
    public FontFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
