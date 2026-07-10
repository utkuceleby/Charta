using Charta.Cos;

namespace Charta.Compliance;

/// <summary>
/// Serializes the structure tree into PDF objects: a StructTreeRoot, one StructElem per node, and a
/// ParentTree (number tree) mapping each page's marked content to its owning element. Marked-content
/// references use MCR dictionaries so an element whose content spans pages is handled correctly.
/// </summary>
internal static class StructureWriter
{
    /// <summary>Writes the tree and returns the StructTreeRoot reference for the catalog.</summary>
    public static CosReference Write(PdfWriter writer, StructureBuilder builder, IReadOnlyList<CosReference> pageRefs)
    {
        var structTreeRootRef = writer.Allocate();

        // Allocate a reference for every element first so parents/kids can cross-reference.
        var all = new List<StructElement>();
        Collect(builder.Root, all);
        foreach (var element in all)
        {
            element.Reference = writer.Allocate();
        }

        foreach (var element in all)
        {
            var dict = new CosDictionary
            {
                [CosNames.Type] = CosName.Get("StructElem"),
                [CosNames.S] = CosName.Get(element.Type),
                [CosNames.P] = element.Parent?.Reference ?? structTreeRootRef,
            };

            if (element.Alt is { } alt)
            {
                dict[CosName.Get("Alt")] = CosString.FromText(alt);
            }

            var kids = new CosArray();
            foreach (var kid in element.Kids)
            {
                switch (kid)
                {
                    case StructElement child:
                        kids.Add(child.Reference!);
                        break;
                    case McidRef mcr:
                        kids.Add(new CosDictionary
                        {
                            [CosNames.Type] = CosName.Get("MCR"),
                            [CosName.Get("Pg")] = pageRefs[mcr.PageIndex],
                            [CosName.Get("MCID")] = new CosInteger(mcr.Mcid),
                        });
                        break;
                    default:
                        break;
                }
            }

            if (kids.Count > 0)
            {
                dict[CosName.Get("K")] = kids;
            }

            writer.WriteObject(element.Reference!, dict);
        }

        // ParentTree: for each page, an array of the elements owning each MCID (index = MCID).
        var nums = new CosArray();
        for (var page = 0; page < builder.PageMcidOwners.Count; page++)
        {
            nums.Add(new CosInteger(page));
            var owners = new CosArray();
            foreach (var owner in builder.PageMcidOwners[page])
            {
                owners.Add(owner.Reference!);
            }

            nums.Add(owners);
        }

        var parentTreeRef = writer.Allocate();
        writer.WriteObject(parentTreeRef, new CosDictionary { [CosName.Get("Nums")] = nums });

        var structTreeRoot = new CosDictionary
        {
            [CosNames.Type] = CosName.Get("StructTreeRoot"),
            [CosName.Get("K")] = new CosArray(builder.Root.Reference!),
            [CosName.Get("ParentTree")] = parentTreeRef,
            [CosName.Get("ParentTreeNextKey")] = new CosInteger(builder.PageMcidOwners.Count),
        };
        writer.WriteObject(structTreeRootRef, structTreeRoot);

        return structTreeRootRef;
    }

    private static void Collect(StructElement element, List<StructElement> into)
    {
        into.Add(element);
        foreach (var kid in element.Kids)
        {
            if (kid is StructElement child)
            {
                Collect(child, into);
            }
        }
    }
}
