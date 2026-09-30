using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hoboman.Tests.Base64;

public sealed class Base64JsonTests
{
    static JsonNode Parsed(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void Encode_WhenAPathLeadsToText_ThenSendsTheTextAsBase64()
    {
        // Act
        var body = Base64Json.Encode("""{"html": "<p>Ærø</p>", "rendered": false}""", ["$.html"]);

        // Assert
        Assert.Equal(("<p>Ærø</p>", false), (Base64Text.Decode(Parsed(body)["html"]!.GetValue<string>()), Parsed(body)["rendered"]!.GetValue<bool>()));
    }

    [Fact]
    public void Encode_WhenAPathLeadsToAnObject_ThenSendsItsJsonAsBase64()
    {
        // Act
        var body = Base64Json.Encode("""{"data": {"name": "<b>Hobo</b>"}}""", ["$.data"]);

        // Assert
        Assert.Equal("""{"name":"<b>Hobo</b>"}""", Base64Text.Decode(Parsed(body)["data"]!.GetValue<string>()));
    }

    [Fact]
    public void Encode_WhenAPathGoesThroughAList_ThenEncodesEveryElement()
    {
        // Act
        var body = Base64Json.Encode("""{"items": [{"html": "a"}, {"html": "b"}]}""", ["$.items[*].html"]);

        // Assert
        Assert.Equal(["a", "b"], Parsed(body)["items"]!.AsArray().Select(item => Base64Text.Decode(item!["html"]!.GetValue<string>())));
    }

    [Fact]
    public void Encode_WhenAPropertyInsideAnotherIsChosenToo_ThenEncodesTheInnerOneFirst()
    {
        // Act
        var body = Base64Json.Encode("""{"data": {"html": "a"}}""", ["$.data", "$.data.html"]);

        // Assert
        Assert.Equal($$"""{"html":"{{Base64Text.Encode("a")}}"}""", Base64Text.Decode(Parsed(body)["data"]!.GetValue<string>()));
    }

    [Fact]
    public void Encode_WhenTheWholeBodyIsChosen_ThenSendsItAsBase64EvenIfItIsNotJson()
    {
        // Act
        var body = Base64Json.Encode("not json", [JsonPath.Root]);

        // Assert
        Assert.Equal(Base64Text.Encode("not json"), body);
    }

    [Fact]
    public void Encode_WhenAPropertyAndTheWholeBodyAreChosen_ThenEncodesThePropertyFirst()
    {
        // Act
        var body = Base64Json.Encode("""{"html":"a"}""", [JsonPath.Root, "$.html"]);

        // Assert
        Assert.Equal($$"""{"html":"{{Base64Text.Encode("a")}}"}""", Base64Text.Decode(body));
    }

    [Theory]
    [InlineData("$.html")]
    [InlineData("$.items[*].name")]
    [InlineData("html")]
    public void Encode_WhenAPathDoesNotLeadToAValue_ThenThrows(string path)
    {
        // Act
        var encoding = () => Base64Json.Encode("""{"items": [{"name": "a"}, {"html": "b"}]}""", [path]);

        // Assert
        Assert.Equal(path, Assert.Throws<MissingBase64PathException>(encoding).Path);
    }

    [Fact]
    public void Encode_WhenAChosenListIsEmpty_ThenSendsTheBodyAsItIs()
    {
        // Act
        var body = Base64Json.Encode("""{"items": []}""", ["$.items[*].html"]);

        // Assert
        Assert.Equal("""{"items": []}""", body);
    }

    [Fact]
    public void Encode_WhenTheBodyIsNotJson_ThenThrows()
    {
        // Act
        var encoding = () => Base64Json.Encode("not json", ["$.html"]);

        // Assert
        Assert.ThrowsAny<JsonException>(encoding);
    }

    [Fact]
    public void Decode_WhenAValueIsBase64Text_ThenGivesTheText()
    {
        // Act
        var decoded = Base64Json.Decode($$"""{"html": "{{Base64Text.Encode("<p>Ærø</p>")}}"}""", ["$.html"]);

        // Assert
        Assert.Equal("<p>Ærø</p>", Parsed(decoded.Body)["html"]!.GetValue<string>());
    }

    [Fact]
    public void Decode_WhenAValueIsBase64Json_ThenGivesItAsJson()
    {
        // Arrange
        var data = Base64Text.Encode("""{"name":"Hobo"}""");

        // Act
        var decoded = Base64Json.Decode($$"""{"data": "{{data}}"}""", ["$.data"]);

        // Assert
        Assert.Equal("Hobo", Parsed(decoded.Body)["data"]!["name"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("false", JsonValueKind.False)]
    [InlineData("1250", JsonValueKind.Number)]
    [InlineData("null", JsonValueKind.Null)]
    public void Decode_WhenAValueIsBase64OfAJsonValue_ThenGivesItAsThatValue(string json, JsonValueKind kind)
    {
        // Act
        var decoded = Base64Json.Decode($$"""{"value": "{{Base64Text.Encode(json)}}"}""", ["$.value"]);

        // Assert
        Assert.Equal(kind, JsonDocument.Parse(decoded.Body).RootElement.GetProperty("value").ValueKind);
    }

    [Fact]
    public void Decode_WhenAValueIsNotBase64_ThenKeepsItAndNamesItsPlace()
    {
        // Act
        var decoded = Base64Json.Decode("""{"items": [{"token": "not base64!"}]}""", ["$.items[*].token"]);

        // Assert
        Assert.Equal(("not base64!", "$.items[0].token"), (Parsed(decoded.Body)["items"]![0]!["token"]!.GetValue<string>(), Assert.Single(decoded.Failed)));
    }

    [Fact]
    public void Decode_WhenTheWholeBodyIsBase64_ThenDecodesIt()
    {
        // Act
        var decoded = Base64Json.Decode(Base64Text.Encode("""{"a":1}"""), [JsonPath.Root]);

        // Assert
        Assert.Equal("""{"a":1}""", decoded.Body);
    }

    [Fact]
    public void Decode_WhenTheWholeBodyIsNotBase64_ThenKeepsItAndNamesTheRoot()
    {
        // Act
        var decoded = Base64Json.Decode("not base64!", [JsonPath.Root]);

        // Assert
        Assert.Equal(("not base64!", JsonPath.Root), (decoded.Body, Assert.Single(decoded.Failed)));
    }

    [Fact]
    public void Decode_WhenTheBodyIsNotJson_ThenLeavesItAsItIs()
    {
        // Act
        var decoded = Base64Json.Decode("plain text", ["$.html"]);

        // Assert
        Assert.Equal(("plain text", 0), (decoded.Body, decoded.Failed.Count));
    }
}
