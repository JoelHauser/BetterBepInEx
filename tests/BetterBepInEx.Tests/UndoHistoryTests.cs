using BetterBepInEx.Core;

namespace BetterBepInEx.Tests;

public class UndoHistoryTests
{
    private sealed class Key(string name)
    {
        public override string ToString() => name;
    }

    private static readonly Key A = new("a");
    private static readonly Key B = new("b");

    [Fact]
    public void UndoReturnsTheOldValueAndRedoTheNew()
    {
        var h = new UndoHistory<Key>();
        h.Record(A, 1, 2, 0, "a");

        var undo = h.Undo(out string label)!;
        Assert.Equal("a", label);
        Assert.Equal(A, Assert.Single(undo).Key);
        Assert.Equal(1, undo[0].Value);

        var redo = h.Redo(out _)!;
        Assert.Equal(2, Assert.Single(redo).Value);
    }

    [Fact]
    public void NoOpChangesAreNotRecorded()
    {
        var h = new UndoHistory<Key>();
        h.Record(A, 1, 1, 0, "a");
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void ADragOnOneSettingIsOneStep()
    {
        var h = new UndoHistory<Key>();
        for (int i = 0; i < 50; i++)
        {
            h.Record(A, i, i + 1, i * 0.02, "a");
        }
        Assert.Equal(1, h.UndoCount);
        Assert.Equal(0, h.Undo(out _)![0].Value);
    }

    [Fact]
    public void ChangesFurtherApartThanTheWindowAreSeparateSteps()
    {
        var h = new UndoHistory<Key>();
        h.Record(A, 1, 2, 0, "a");
        h.Record(A, 2, 3, UndoHistory<Key>.MergeWindowSeconds + 0.5, "a");
        Assert.Equal(2, h.UndoCount);
    }

    [Fact]
    public void ADifferentSettingStartsANewStep()
    {
        var h = new UndoHistory<Key>();
        h.Record(A, 1, 2, 0, "a");
        h.Record(B, 1, 2, 0.1, "b");
        Assert.Equal(2, h.UndoCount);
        Assert.Equal("b", h.NextUndoLabel);
    }

    [Fact]
    public void DraggingBackToTheStartLeavesNothingToUndo()
    {
        var h = new UndoHistory<Key>();
        h.Record(A, 1, 2, 0, "a");
        h.Record(A, 2, 1, 0.1, "a");
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void AGroupUndoesTogetherNewestFirst()
    {
        var h = new UndoHistory<Key>();
        h.BeginGroup("paste");
        h.Record(A, 1, 2, 0, "a");
        h.Record(B, "x", "y", 0, "b");
        h.Record(A, 2, 3, 0, "a");
        h.EndGroup();

        Assert.Equal(1, h.UndoCount);
        var undo = h.Undo(out string label)!;
        Assert.Equal("paste", label);
        Assert.Equal(2, undo.Count);
        Assert.Equal(B, undo[0].Key);
        Assert.Equal("x", undo[0].Value);
        Assert.Equal(A, undo[1].Key);
        Assert.Equal(1, undo[1].Value);
    }

    [Fact]
    public void AGroupThatChangedNothingAddsNoStep()
    {
        var h = new UndoHistory<Key>();
        h.BeginGroup("paste");
        h.Record(A, 1, 2, 0, "a");
        h.Record(A, 2, 1, 0, "a");
        h.EndGroup();
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void GroupsDoNotNest()
    {
        var h = new UndoHistory<Key>();
        h.BeginGroup("one");
        Assert.Throws<InvalidOperationException>(() => h.BeginGroup("two"));
    }

    [Fact]
    public void ANewChangeClearsRedo()
    {
        var h = new UndoHistory<Key>();
        h.Record(A, 1, 2, 0, "a");
        h.Undo(out _);
        Assert.True(h.CanRedo);
        h.Record(B, 1, 2, 5, "b");
        Assert.False(h.CanRedo);
    }

    [Fact]
    public void AStepAfterAGroupIsNotMergedIntoIt()
    {
        var h = new UndoHistory<Key>();
        h.BeginGroup("paste");
        h.Record(A, 1, 2, 0, "a");
        h.EndGroup();
        h.Record(A, 2, 3, 0.1, "a");
        Assert.Equal(2, h.UndoCount);
    }

    [Fact]
    public void HistoryIsCapped()
    {
        var h = new UndoHistory<Key>();
        for (int i = 0; i < UndoHistory<Key>.MaxSteps + 20; i++)
        {
            h.Record(i % 2 == 0 ? A : B, i, i + 1, i, "x");
        }
        Assert.Equal(UndoHistory<Key>.MaxSteps, h.UndoCount);
    }
}
