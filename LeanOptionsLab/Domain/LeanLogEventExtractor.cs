using System;
using System.Collections.Generic;
using System.IO;

namespace LeanOptionsLab.Domain;

/// <summary>
/// Extracts only explicit OPTIONS_LAB log markers. It never tries to manufacture an
/// event time from an unknown LEAN log layout, so a missing timestamp is kept null.
/// </summary>
public static class LeanLogEventExtractor
{
    public static ExtractedLifecycleEvents Extract(string logPath)
    {
        var orderEvents = new List<RecordedLifecycleEvent>();
        var assignmentEvents = new List<RecordedLifecycleEvent>();
        var exerciseEvents = new List<RecordedLifecycleEvent>();

        foreach (var line in File.ReadLines(logPath))
        {
            AddIfMarked(line, "OPTIONS_LAB|order-event|", "order", orderEvents);
            AddIfMarked(line, "OPTIONS_LAB|assignment-event|", "assignment", assignmentEvents);
            AddIfMarked(line, "OPTIONS_LAB|exercise-event|", "exercise", exerciseEvents);
        }

        return new(orderEvents, assignmentEvents, exerciseEvents);
    }

    private static void AddIfMarked(
        string line,
        string marker,
        string kind,
        ICollection<RecordedLifecycleEvent> target)
    {
        var markerIndex = line.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return;
        }

        target.Add(new RecordedLifecycleEvent
        {
            Kind = kind,
            Detail = line[(markerIndex + marker.Length)..]
        });
    }
}

public sealed record ExtractedLifecycleEvents(
    IReadOnlyList<RecordedLifecycleEvent> OrderEvents,
    IReadOnlyList<RecordedLifecycleEvent> AssignmentEvents,
    IReadOnlyList<RecordedLifecycleEvent> ExerciseEvents);
