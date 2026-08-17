using System.Text;
using FamilyTree.Import;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: FamilyTree.Import <pdf-path> <output-dir>");
    return 1;
}

var pdfPath = args[0];
var outputDir = args[1];

var streams = PdfStreams.Inflate(File.ReadAllBytes(pdfPath));
var page = ContentStream.Read(PdfStreams.LargestOf(streams), ToUnicodeCMap.Parse(streams));
var classified = Geometry.Classify(page);
var reconstruction = Reconstruct.Build(page, classified);

Directory.CreateDirectory(outputDir);

// No BOM: a BOM in family-tree.json would sit before the leading '{' and break naive JSON
// parsers in Task 7; consistent with the .txt file for the same reason.
var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var txtPath = Path.Combine(outputDir, "family-tree.txt");
var jsonPath = Path.Combine(outputDir, "family-tree.json");

File.WriteAllText(txtPath, Emit.IndentedTree(reconstruction), utf8NoBom);
File.WriteAllText(jsonPath, Emit.Json(reconstruction), utf8NoBom);

// Verify Arabic survived the round trip to disk, rather than assuming UTF8Encoding did the
// right thing -- read the file back and compare against the in-memory names.
var roundTrippedTxt = File.ReadAllText(txtPath, Encoding.UTF8);
var expectedNames = reconstruction.Members.Select(m => m.Name).ToHashSet();
var survivedNames = expectedNames.Count(n => roundTrippedTxt.Contains(n));
if (survivedNames != expectedNames.Count)
{
    Console.Error.WriteLine(
        $"WARNING: only {survivedNames}/{expectedNames.Count} member names round-tripped intact through {txtPath}.");
}

var members = reconstruction.Members;
var byId = members.ToDictionary(m => m.Id);
var rootCount = members.Count(m => m.ParentId is null);

int DepthOf(ImportedMember m)
{
    var depth = 0;
    var current = m;
    while (current.ParentId is { } parentId)
    {
        depth++;
        current = byId[parentId];
    }
    return depth;
}

var maxDepth = members.Count == 0 ? 0 : members.Max(DepthOf);
var distinctNameCount = members.Select(m => m.Name).Distinct().Count();

var topRepeatedNames = members
    .GroupBy(m => m.Name)
    .Select(g => (Name: g.Key, Count: g.Count()))
    .Where(x => x.Count > 1)
    .OrderByDescending(x => x.Count)
    .ThenBy(x => x.Name, StringComparer.Ordinal)
    .Take(10)
    .ToList();

Console.WriteLine("Family tree reconstruction summary");
Console.WriteLine("-----------------------------------");
Console.WriteLine($"Members:          {members.Count}");
Console.WriteLine($"Roots:            {rootCount}");
Console.WriteLine($"Max depth:        {maxDepth} (root is depth 0)");
Console.WriteLine($"Distinct names:   {distinctNameCount}");
Console.WriteLine($"Orientation:      {reconstruction.Orientation}");
Console.WriteLine();
Console.WriteLine("Top 10 repeated names:");
if (topRepeatedNames.Count == 0)
{
    Console.WriteLine("  (no name repeats more than once)");
}
else
{
    foreach (var (name, count) in topRepeatedNames)
        Console.WriteLine($"  {count,4}  {name}");
}
Console.WriteLine();
Console.WriteLine($"Wrote: {txtPath}");
Console.WriteLine($"Wrote: {jsonPath}");

return 0;
