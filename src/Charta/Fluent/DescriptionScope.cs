namespace Charta.Fluent;

/// <summary>
/// Ambient flag scope active while a document description lambda runs, so descriptor calls like
/// TotalPages() can mark capabilities that change how generation works (a counting pre-pass).
/// </summary>
internal static class DescriptionScope
{
    [ThreadStatic]
    private static DocumentDescriptor? _current;

    public static IDisposable Begin(DocumentDescriptor descriptor)
    {
        _current = descriptor;
        return new Scope();
    }

    public static void MarkUsesTotalPages()
    {
        if (_current is not null)
        {
            _current.UsesTotalPages = true;
        }
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => _current = null;
    }
}
