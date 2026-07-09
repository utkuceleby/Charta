# Unicode Character Database files

These files are from the [Unicode Character Database](https://www.unicode.org/ucd/), version 16.0.0,
© Unicode, Inc., and are distributed under the [Unicode License v3](https://www.unicode.org/license.txt).

| File | Source |
|---|---|
| LineBreak.txt | https://www.unicode.org/Public/16.0.0/ucd/LineBreak.txt |
| EastAsianWidth.txt | https://www.unicode.org/Public/16.0.0/ucd/EastAsianWidth.txt |
| DerivedGeneralCategory.txt | https://www.unicode.org/Public/16.0.0/ucd/extracted/DerivedGeneralCategory.txt |
| emoji-data.txt | https://www.unicode.org/Public/16.0.0/ucd/emoji/emoji-data.txt |

`tests/Charta.Tests/Data/LineBreakTest.txt` comes from
https://www.unicode.org/Public/16.0.0/ucd/auxiliary/LineBreakTest.txt under the same license.

To regenerate `src/Charta/Text/Generated/LineBreakData.g.cs` after a version bump, replace these
files and run `dotnet run --project tools/Charta.CodeGen`.
