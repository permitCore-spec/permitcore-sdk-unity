using System.Collections.Generic;
using PermitCore;
using Xunit;

namespace PermitCore.UnityTests
{
    public class JsonTests
    {
        [Fact]
        public void ParsesFlatObject()
        {
            var d = (Dictionary<string, object>)Json.Parse("{\"a\":1,\"b\":\"x\",\"c\":true,\"d\":null}");
            Assert.Equal(1.0, d["a"]);
            Assert.Equal("x", d["b"]);
            Assert.Equal(true, d["c"]);
            Assert.Null(d["d"]);
        }

        [Fact]
        public void ParsesNestedObjectsAndArrays()
        {
            var d = (Dictionary<string, object>)Json.Parse("{\"features\":[\"pro\",\"export\"],\"nested\":{\"x\":42}}");
            var features = (List<object>)d["features"];
            Assert.Equal(2, features.Count);
            Assert.Equal("pro", features[0]);
            var nested = (Dictionary<string, object>)d["nested"];
            Assert.Equal(42.0, nested["x"]);
        }

        [Fact]
        public void ParsesEscapedStringsAndUnicode()
        {
            var d = (Dictionary<string, object>)Json.Parse("{\"s\":\"line1\\nline2\\t\\\"quoted\\\"\\u00e9\"}");
            Assert.Equal("line1\nline2\t\"quoted\"\u00e9", d["s"]);
        }

        [Fact]
        public void RoundTripsSerializeThenParse()
        {
            var original = new Dictionary<string, object>
            {
                ["licenseKey"] = "PERMIT-XXXX",
                ["quantity"] = 5,
                ["meta"] = new Dictionary<string, object> { ["format"] = "pdf", ["pages"] = 12 },
            };
            string json = Json.Serialize(original);
            var parsed = (Dictionary<string, object>)Json.Parse(json);

            Assert.Equal("PERMIT-XXXX", parsed["licenseKey"]);
            Assert.Equal(5.0, parsed["quantity"]);
            var meta = (Dictionary<string, object>)parsed["meta"];
            Assert.Equal("pdf", meta["format"]);
            Assert.Equal(12.0, meta["pages"]);
        }

        [Fact]
        public void SerializeEscapesSpecialCharacters()
        {
            string json = Json.Serialize(new Dictionary<string, object> { ["s"] = "a\"b\\c\nd" });
            var parsed = (Dictionary<string, object>)Json.Parse(json);
            Assert.Equal("a\"b\\c\nd", parsed["s"]);
        }

        [Fact]
        public void GetStringDictionary_HandlesCustomFields()
        {
            var d = (Dictionary<string, object>)Json.Parse("{\"customFields\":{\"seats\":\"5\",\"tier\":\"pro\"}}");
            var cf = Json.GetStringDictionary(d, "customFields");
            Assert.Equal("5", cf["seats"]);
            Assert.Equal("pro", cf["tier"]);
        }
    }
}
