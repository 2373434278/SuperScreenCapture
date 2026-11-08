using System.Security.Cryptography;
using System.Text.Json;
using SSC.Application.Editing;
using SSC.Domain.Annotations;
using SSC.Domain.Geometry;
using Xunit;

namespace SSC.Application.Tests;

public sealed class AnnotationEditorSessionTests
{
    [Fact]
    public void Commit_AddsAnnotationAndTracksHistory()
    {
        var session = CreateSession();
        session.SelectTool(AnnotationToolKind.Rectangle);

        Assert.True(session.Commit(Input(new PixelPoint(10, 10), new PixelPoint(40, 30))));

        Assert.Single(session.Document.Annotations);
        Assert.Equal(1, session.History.UndoCount);
        Assert.Equal(0, session.History.RedoCount);
    }

    [Fact]
    public void UndoRedo_RestoresDocumentSnapshots()
    {
        var session = CreateSession();
        session.SelectTool(AnnotationToolKind.Ellipse);
        session.Commit(Input(new PixelPoint(10, 10), new PixelPoint(40, 30)));
        var hash = Hash(session.Document);

        Assert.True(session.Undo());
        Assert.Empty(session.Document.Annotations);
        Assert.True(session.Redo());

        Assert.Equal(hash, Hash(session.Document));
    }

    [Fact]
    public void FiftySteps_UndoRedoKeepsDocumentHash()
    {
        var session = CreateSession();
        session.SelectTool(AnnotationToolKind.Rectangle);

        for (var i = 0; i < 50; i++)
        {
            var offset = i * 2;
            Assert.True(session.Commit(Input(
                new PixelPoint(offset, offset),
                new PixelPoint(offset + 20, offset + 12))));
        }

        var expected = Hash(session.Document);
        for (var i = 0; i < 50; i++)
        {
            Assert.True(session.Undo());
        }

        Assert.Empty(session.Document.Annotations);
        for (var i = 0; i < 50; i++)
        {
            Assert.True(session.Redo());
        }

        Assert.Equal(expected, Hash(session.Document));
        Assert.Equal(50, session.History.UndoCount);
    }

    [Fact]
    public void Crop_UpdatesDocumentAndIsUndoable()
    {
        var session = CreateSession();
        session.SelectTool(AnnotationToolKind.Crop);

        Assert.True(session.Commit(Input(new PixelPoint(20, 10), new PixelPoint(80, 70))));
        Assert.Equal(new PixelRect(20, 10, 60, 60), session.Document.Crop);

        Assert.True(session.Undo());
        Assert.Equal(new PixelRect(0, 0, 100, 100), session.Document.Crop);
    }

    [Fact]
    public void RemoveAt_RenumbersStepsAndIsUndoable()
    {
        var session = CreateSession();
        session.SelectTool(AnnotationToolKind.Step);
        session.Commit(Input(new PixelPoint(10, 10), new PixelPoint(10, 10)));
        session.Commit(Input(new PixelPoint(30, 10), new PixelPoint(30, 10)));
        session.Commit(Input(new PixelPoint(50, 10), new PixelPoint(50, 10)));

        Assert.True(session.RemoveAt(0));
        Assert.Equal(new[] { 1, 2 }, session.Document.Annotations.Select(annotation => annotation.Step));

        Assert.True(session.Undo());
        Assert.Equal(new[] { 1, 2, 3 }, session.Document.Annotations.Select(annotation => annotation.Step));
    }

    [Fact]
    public void ToolSelection_IgnoresUnknownKind()
    {
        var session = CreateSession();
        session.SelectTool(AnnotationToolKind.Pen);

        session.SelectTool((AnnotationToolKind)999);

        Assert.Equal(AnnotationToolKind.Pen, session.ActiveTool);
    }

    [Fact]
    public void Style_IsNormalized()
    {
        var session = CreateSession();
        session.UpdateStyle(new AnnotationStyle("", 0, 500, 1));

        Assert.Equal(AnnotationStyle.DefaultColor, session.Style.Color);
        Assert.Equal(1, session.Style.StrokeWidth);
        Assert.Equal(200, session.Style.FontSize);
        Assert.Equal(2, session.Style.Intensity);
    }

    private static AnnotationEditorSession CreateSession() => new(100, 100);

    private static AnnotationToolInput Input(PixelPoint start, PixelPoint end) => new(start, end);

    private static string Hash(AnnotationDocument document) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(document)));
}
