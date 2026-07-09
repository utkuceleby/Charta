using System.Diagnostics.CodeAnalysis;

namespace Charta;

/// <summary>
/// A single-child slot in the document tree. Chaining style methods nests decorators
/// (<c>.Padding(10).Border(1)</c> puts the border inside the padding); content methods like
/// <c>Text</c>, <c>Column</c>, or <c>Image</c> fill the slot and can be used once per container.
/// </summary>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Opaque handle; all functionality lives in extension methods so add-on packages can contribute elements first-class.")]
public interface IContainer
{
}
