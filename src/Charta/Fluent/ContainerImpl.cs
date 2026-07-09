using Charta.Layout;
using Charta.Layout.Elements;

namespace Charta.Fluent;

/// <summary>The build-side implementation: wrappers applied outermost-first around a deferred source.</summary>
internal sealed class ContainerImpl : IContainer
{
    private readonly List<Func<BuildContext, Element, Element>> _wrappers = [];
    private Func<BuildContext, Element>? _source;

    public void AddWrapper(Func<BuildContext, Element, Element> wrapper) => _wrappers.Add(wrapper);

    public void SetSource(Func<BuildContext, Element> source)
    {
        if (_source is not null)
        {
            throw new InvalidOperationException(
                "This container already has content. Wrap multiple elements in a Column or Row.");
        }

        _source = source;
    }

    public Element Build(BuildContext context)
    {
        var element = _source?.Invoke(context) ?? (Element)EmptyElement.Instance;
        for (var i = _wrappers.Count - 1; i >= 0; i--)
        {
            element = _wrappers[i](context, element);
        }

        return element;
    }
}
