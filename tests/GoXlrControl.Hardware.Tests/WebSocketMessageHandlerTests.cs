using System.Text.Json.Nodes;
using FluentAssertions;
using GoXlrControl.Hardware;

namespace GoXlrControl.Hardware.Tests;

/// <summary>
/// Regression: Utility replies with data:"Ok" (JsonValue). Indexing ["Patch"] on that
/// threw and tore down the WebSocket every lighting tick (~100ms reconnect storm).
/// </summary>
public class WebSocketMessageHandlerTests
{
    [Fact]
    public void OkStringReply_DoesNotThrow_WhenCheckingForPatch()
    {
        var envelope = JsonNode.Parse("""{"id":2,"data":"Ok"}""")!;
        var data = envelope["data"]!;

        var act = () =>
        {
            if (data is JsonObject dataObj && dataObj["Patch"] is JsonArray)
                throw new InvalidOperationException("should not treat Ok as Patch");
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void OkStringReply_IndexingPatch_WouldThrow_WithoutGuard()
    {
        var data = JsonNode.Parse("\"Ok\"")!;
        var act = () => _ = data["Patch"];
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*JsonObject*");
    }

    [Fact]
    public void PatchObject_IsDetected()
    {
        var data = JsonNode.Parse("""{"Patch":[{"op":"replace","path":"/x","value":1}]}""")!;
        (data is JsonObject obj && obj["Patch"] is JsonArray).Should().BeTrue();
    }
}
