using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class ItemTagPolicyTests
{
    [TestMethod]
    public void ParseEditorText_SupportsDelimitersAndPreservesInternalSpaces()
    {
        List<string> tags = ItemTagPolicy.ParseEditorText(
            " 重要，Project Alpha;资料\n重要\t稍后 ");

        CollectionAssert.AreEquivalent(
            new[] { "重要", "Project Alpha", "资料", "稍后" },
            tags);
        CollectionAssert.AreEqual(
            tags.OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            tags);
    }

    [TestMethod]
    public void Normalize_DeduplicatesCaseInsensitivelyAndFormatsForEditor()
    {
        List<string> tags = ItemTagPolicy.Normalize([" Beta ", "alpha", "ALPHA", ""]);

        CollectionAssert.AreEqual(new[] { "alpha", "Beta" }, tags);
        Assert.AreEqual("alpha，Beta", ItemTagPolicy.FormatEditorText(tags));
    }

    [TestMethod]
    public void SetTags_EquivalentInputIsNoChangeAndEmptyInputRemovesEntry()
    {
        var itemTags = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Report.txt"] = ["Alpha", "Beta"]
        };

        Assert.IsFalse(ItemTagPolicy.SetTags(itemTags, "REPORT.TXT", ["beta", "alpha"]));
        Assert.IsTrue(ItemTagPolicy.SetTags(itemTags, "report.txt", Array.Empty<string>()));
        Assert.AreEqual(0, itemTags.Count);
    }

    [TestMethod]
    public void AddTag_MergesWithManualTagsWithoutDuplicates()
    {
        var itemTags = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["item.txt"] = ["手工"]
        };

        Assert.IsTrue(ItemTagPolicy.AddTag(itemTags, "ITEM.TXT", "规则"));
        Assert.IsFalse(ItemTagPolicy.AddTag(itemTags, "item.txt", "规则"));
        CollectionAssert.AreEquivalent(
            new[] { "手工", "规则" },
            itemTags["item.txt"]);
    }

    [TestMethod]
    public void NormalizeDictionary_DropsBlankEntriesAndReturnsIndependentLists()
    {
        var source = new Dictionary<string, List<string>>
        {
            ["item.txt"] = [" Beta ", "alpha", "ALPHA"],
            ["empty.txt"] = [" "],
            [" "] = ["ignored"]
        };

        Dictionary<string, List<string>> normalized = ItemTagPolicy.NormalizeDictionary(source);
        source["item.txt"][0] = "changed";

        Assert.AreEqual(1, normalized.Count);
        CollectionAssert.AreEqual(new[] { "alpha", "Beta" }, normalized["item.txt"]);
    }
}
