using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using static PixelArtStudio.Mcp.Tools.ToolHelpers;

namespace PixelArtStudio.Mcp.Tools;

[McpServerToolType]
public sealed class LayerTools(IStudioSession session)
{
    [McpServerTool(Name = "add_layer")]
    [Description("Adds a blank layer (in every frame) and makes it active. Returns its index. Layer 0 is the bottom.")]
    public Task<string> AddLayer(
        [Description("Layer name. Default: Layer N.")] string? name = null,
        [Description("Index of the layer that will sit directly below the new one; -1 adds it at the bottom. Default: the active layer.")] int? below = null) =>
        session.RunAsync(e =>
        {
            int count = e.Document.Layers.Count;
            int under = below is -1 ? -1 : Layer(e, below, "below");
            string? layerName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            CheckBudget(e.Document.Width, e.Document.Height, count + 1, e.Document.Frames.Count);
            int index = 0;
            e.EditDocument("Add layer", doc => index = doc.AddLayer(under, layerName));
            e.ActiveLayer = index;
            return $"Added {LayerLabel(e, index)}. The sprite has {count + 1} layers.";
        });

    [McpServerTool(Name = "duplicate_layer")]
    [Description("Copies a layer (all frames) directly above itself and makes the copy active.")]
    public Task<string> DuplicateLayer(
        [Description("Layer index. Default: the active layer.")] int? layer = null) =>
        session.RunAsync(e =>
        {
            int l = Layer(e, layer);
            CheckBudget(e.Document.Width, e.Document.Height, e.Document.Layers.Count + 1, e.Document.Frames.Count);
            int index = 0;
            e.EditDocument("Duplicate layer", doc => index = doc.DuplicateLayer(l));
            e.ActiveLayer = index;
            return $"Duplicated layer {l} as {LayerLabel(e, index)}.";
        });

    [McpServerTool(Name = "delete_layer", Destructive = true)]
    [Description("Deletes a layer from every frame. The last remaining layer cannot be deleted.")]
    public Task<string> DeleteLayer(
        [Description("Layer index. Default: the active layer.")] int? layer = null) =>
        session.RunAsync(e =>
        {
            int l = Layer(e, layer);
            if (e.Document.Layers.Count <= 1) throw new McpException("A sprite needs at least one layer; layer 0 cannot be deleted.");
            string label = LayerLabel(e, l);
            e.EditDocument("Delete layer", doc => doc.DeleteLayer(l));
            return $"Deleted {label}. The sprite has {e.Document.Layers.Count} layer(s).";
        });

    [McpServerTool(Name = "move_layer")]
    [Description("Moves a layer to a new position in the stack (0 is the bottom).")]
    public Task<string> MoveLayer(
        [Description("Current index of the layer.")] int from,
        [Description("New index of the layer.")] int to) =>
        session.RunAsync(e =>
        {
            int count = e.Document.Layers.Count;
            CheckIndex(from, count, "from");
            CheckIndex(to, count, "to");
            if (from == to) return $"Layer {from} is already at {to}.";
            e.EditDocument("Move layer", doc => doc.MoveLayer(from, to));
            e.ActiveLayer = to;
            return $"Moved layer {from} to {to}: {LayerLabel(e, to)}.";
        });

    [McpServerTool(Name = "merge_down", Destructive = true)]
    [Description("Blends a layer onto the layer below it (in every frame) and removes it.")]
    public Task<string> MergeDown(
        [Description("Layer index, 1 or more. Default: the active layer.")] int? layer = null) =>
        session.RunAsync(e =>
        {
            int l = Layer(e, layer);
            if (l == 0) throw new McpException("Layer 0 is the bottom layer; there is no layer below to merge into.");
            string label = LayerLabel(e, l);
            e.EditDocument("Merge down", doc => doc.MergeDown(l));
            e.ActiveLayer = l - 1;
            return $"Merged {label} into {LayerLabel(e, l - 1)}.";
        });

    [McpServerTool(Name = "set_layer")]
    [Description("Changes a layer's name, visibility and/or opacity. Omitted values are left unchanged.")]
    public Task<string> SetLayer(
        [Description("Layer index. Default: the active layer.")] int? layer = null,
        [Description("New name.")] string? name = null,
        [Description("Show (true) or hide (false) the layer.")] bool? visible = null,
        [Description("Opacity from 0.0 (invisible) to 1.0 (opaque).")] double? opacity = null)
    {
        if (opacity is < 0 or > 1) throw new McpException($"opacity must be 0.0 to 1.0 (got {opacity}).");
        if (name != null && string.IsNullOrWhiteSpace(name)) throw new McpException("name cannot be empty.");
        if (name == null && visible == null && opacity == null) throw new McpException("Pass at least one of name, visible or opacity.");

        return session.RunAsync(e =>
        {
            int l = Layer(e, layer);
            e.EditMeta("Layer properties", doc =>
            {
                var target = doc.Layers[l];
                if (name != null) target.Name = name.Trim();
                if (visible is { } v) target.Visible = v;
                if (opacity is { } o) target.Opacity = o;
            });
            var result = e.Document.Layers[l];
            return $"Layer {l}: name '{result.Name}', visible {result.Visible}, opacity {result.Opacity:0.##}.";
        });
    }
}
