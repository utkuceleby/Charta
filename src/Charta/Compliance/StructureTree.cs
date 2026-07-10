using Charta.Cos;

namespace Charta.Compliance;

/// <summary>
/// A node in the logical structure tree (PDF/UA / tagged PDF). Leaf nodes reference marked content
/// by (page, MCID); container nodes hold child elements. The tree is built during drawing and
/// serialized in the trailer phase.
/// </summary>
internal sealed class StructElement(string type)
{
    public string Type { get; } = type;

    public StructElement? Parent { get; set; }

    /// <summary>Children: nested <see cref="StructElement"/>s and/or <see cref="McidRef"/> content links.</summary>
    public List<object> Kids { get; } = [];

    /// <summary>Alternate text (required on figures for accessibility).</summary>
    public string? Alt { get; set; }

    public CosReference? Reference { get; set; }
}

/// <summary>A link from a structure element to marked content: MCID <paramref name="Mcid"/> on a page.</summary>
internal readonly record struct McidRef(int PageIndex, int Mcid);

/// <summary>
/// Builds the structure tree and the per-page MCID→element mapping (the parent tree). Content is
/// tagged as it is drawn; the flat model places tagged leaves directly under the Document root in
/// reading (draw) order, which is correct for typical flowing documents.
/// </summary>
internal sealed class StructureBuilder
{
    public StructElement Root { get; } = new("Document");

    /// <summary>Per page: the structure element owning each MCID (index = MCID).</summary>
    public List<List<StructElement>> PageMcidOwners { get; } = [];

    /// <summary>Adds a child element under a parent (Document root by default).</summary>
    public StructElement AddElement(string type, StructElement? parent = null)
    {
        var element = new StructElement(type) { Parent = parent ?? Root };
        element.Parent!.Kids.Add(element);
        return element;
    }

    /// <summary>Allocates the next MCID on a page and records which element owns it.</summary>
    public int AllocateMcid(int pageIndex, StructElement owner)
    {
        while (PageMcidOwners.Count <= pageIndex)
        {
            PageMcidOwners.Add([]);
        }

        var owners = PageMcidOwners[pageIndex];
        var mcid = owners.Count;
        owners.Add(owner);
        owner.Kids.Add(new McidRef(pageIndex, mcid));
        return mcid;
    }
}
