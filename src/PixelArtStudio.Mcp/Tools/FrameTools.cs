using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PixelArtStudio.Core.Model;
using static PixelArtStudio.Mcp.Tools.ToolHelpers;

namespace PixelArtStudio.Mcp.Tools;

[McpServerToolType]
public sealed class FrameTools(IStudioSession session)
{
    [McpServerTool(Name = "add_frame")]
    [Description("Inserts a frame, blank or a copy of another frame, and makes it active. Returns its index.")]
    public Task<string> AddFrame(
        [Description("Insert after this frame. Default: the active frame, or the copied frame when duplicate_of is set.")] int? after = null,
        [Description("Copy this frame (all layers and its duration) instead of adding a blank one.")] int? duplicate_of = null) =>
        session.RunAsync(e =>
        {
            int count = e.Document.Frames.Count;
            int? source = duplicate_of is { } d ? CheckIndex(d, count, "duplicate_of") : null;
            int anchor = after is { } a ? CheckIndex(a, count, "after") : source ?? e.ActiveFrame;
            int index = anchor + 1;
            CheckBudget(e.Document.Width, e.Document.Height, e.Document.Layers.Count, count + 1);
            e.EditDocument(source != null ? "Duplicate frame" : "Add frame", doc =>
            {
                if (source is { } s) doc.Frames.Insert(index, doc.Frames[s].Clone());
                else doc.AddFrame(anchor);
            });
            e.ActiveFrame = index;
            return source is { } src
                ? $"Added frame {index} as a copy of frame {src}. The sprite has {e.Document.Frames.Count} frames."
                : $"Added blank frame {index}. The sprite has {e.Document.Frames.Count} frames.";
        });

    [McpServerTool(Name = "delete_frame", Destructive = true)]
    [Description("Deletes a frame. The last remaining frame cannot be deleted.")]
    public Task<string> DeleteFrame(
        [Description("Frame index. Default: the active frame.")] int? frame = null) =>
        session.RunAsync(e =>
        {
            int f = Frame(e, frame);
            if (e.Document.Frames.Count <= 1) throw new McpException("A sprite needs at least one frame; frame 0 cannot be deleted.");
            e.EditDocument("Delete frame", doc => doc.DeleteFrame(f));
            return $"Deleted frame {f}. The sprite has {e.Document.Frames.Count} frame(s).";
        });

    [McpServerTool(Name = "move_frame")]
    [Description("Moves a frame to a new position in the timeline.")]
    public Task<string> MoveFrame(
        [Description("Current index of the frame.")] int from,
        [Description("New index of the frame.")] int to) =>
        session.RunAsync(e =>
        {
            int count = e.Document.Frames.Count;
            CheckIndex(from, count, "from");
            CheckIndex(to, count, "to");
            if (from == to) return $"Frame {from} is already at {to}.";
            e.EditDocument("Move frame", doc => doc.MoveFrame(from, to));
            e.ActiveFrame = to;
            return $"Moved frame {from} to {to}.";
        });

    [McpServerTool(Name = "set_frame_duration")]
    [Description("Sets how long a frame shows during playback, in milliseconds (10-60000). 100 ms is 10 fps.")]
    public Task<string> SetFrameDuration(
        [Description("Duration in milliseconds.")] int duration_ms,
        [Description("Frame index. Default: the active frame.")] int? frame = null,
        [Description("Set the duration of every frame.")] bool all_frames = false) =>
        session.RunAsync(e =>
        {
            int ms = Math.Clamp(duration_ms, Core.Model.Frame.MinDurationMs, Core.Model.Frame.MaxDurationMs);
            string clamped = ms != duration_ms ? $" (clamped from {duration_ms})" : "";
            if (all_frames)
            {
                e.EditMeta("Set frame rate", doc => doc.Frames.ForEach(fr => fr.DurationMs = ms));
                return $"All {e.Document.Frames.Count} frames set to {ms} ms{clamped}.";
            }
            int f = Frame(e, frame);
            e.EditMeta("Frame duration", doc => doc.Frames[f].DurationMs = ms);
            return $"Frame {f} set to {ms} ms{clamped}.";
        });
}
