using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;

namespace XIVJobGamba.Windows;

internal static class WheelRenderer
{
    public static void Draw(
        Vector2 center,
        float radius,
        float rotation,
        IReadOnlyList<GearsetSnapshot> entries,
        uint? mutedJobId,
        string hubText,
        ITextureProvider textures,
        float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var drawRotation = WheelMath.Wrap(rotation);
        var ring = ImGui.GetColorU32(new Vector4(0.86f, 0.72f, 0.38f, 0.95f));
        var line = ImGui.GetColorU32(new Vector4(0.05f, 0.04f, 0.03f, 0.85f));
        var hub = ImGui.GetColorU32(new Vector4(0.07f, 0.06f, 0.05f, 1f));
        var hubRing = ImGui.GetColorU32(new Vector4(0.72f, 0.58f, 0.28f, 0.9f));
        var shadow = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.80f));
        var ink = ImGui.GetColorU32(new Vector4(0.98f, 0.96f, 0.90f, 1f));
        var dimInk = ImGui.GetColorU32(new Vector4(0.75f, 0.73f, 0.68f, 1f));

        if (entries.Count == 0)
        {
            drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(new Vector4(0.16f, 0.14f, 0.12f, 1f)), 64);
        }
        else if (entries.Count == 1)
        {
            var onlyColor = JobPalette.For(entries[0].ClassJobId, entries[0].JobType);
            var only = mutedJobId == entries[0].ClassJobId ? JobPalette.Muted(onlyColor) : onlyColor;
            drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(only), 64);
        }
        else
        {
            var slice = MathF.Tau / entries.Count;
            for (var i = 0; i < entries.Count; i++)
            {
                var color = JobPalette.For(entries[i].ClassJobId, entries[i].JobType);
                if (mutedJobId == entries[i].ClassJobId)
                    color = JobPalette.Muted(color);

                var start = drawRotation + (i * slice);
                drawList.PathClear();
                drawList.PathLineTo(center);
                drawList.PathArcTo(center, radius, start, start + slice, 18);
                drawList.PathFillConvex(ImGui.GetColorU32(color));
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var start = drawRotation + (i * (MathF.Tau / entries.Count));
                var dir = new Vector2(MathF.Cos(start), MathF.Sin(start));
                drawList.AddLine(center + (dir * radius * 0.40f), center + (dir * radius), line, 2f * scale);
            }
        }

        drawList.AddCircle(center, radius, ring, 72, 3f * scale);

        var hubRadius = radius * 0.40f;
        drawList.AddCircleFilled(center, hubRadius, hub, 48);
        drawList.AddCircle(center, hubRadius, hubRing, 48, 2f * scale);

        if (entries.Count > 0)
        {
            var pointed = WheelMath.SliceIndexAtPointer(rotation, entries.Count);
            var slice = MathF.Tau / entries.Count;
            var arcStart = drawRotation + (pointed * slice);
            drawList.PathClear();
            drawList.PathArcTo(center, radius - (3f * scale), arcStart, arcStart + slice, 16);
            drawList.PathStroke(ring, 4f * scale);

            for (var i = 0; i < entries.Count; i++)
            {
                var mid = drawRotation + ((i + 0.5f) * slice);
                var dir = new Vector2(MathF.Cos(mid), MathF.Sin(mid));
                var labelRadius = radius * 0.78f;
                var iconRadius = radius * 0.58f;
                var labelChord = 2f * labelRadius * MathF.Sin(slice / 2f);
                var iconChord = 2f * iconRadius * MathF.Sin(slice / 2f);
                var iconSize = 26f * scale;
                var muted = mutedJobId == entries[i].ClassJobId;

                if (iconSize <= iconChord * 0.82f && entries[i].IconId != 0)
                    DrawIcon(drawList, textures, entries[i].IconId, center + (dir * iconRadius), iconSize, muted);

                var label = entries[i].JobAbbreviation;
                var textSize = ImGui.CalcTextSize(label);
                if (textSize.X <= labelChord * 0.90f)
                {
                    var pos = center + (dir * labelRadius) - (textSize / 2f);
                    drawList.AddText(pos + new Vector2(1f, 1f), shadow, label);
                    drawList.AddText(pos, muted ? dimInk : ink, label);
                }
            }
        }

        if (!string.IsNullOrEmpty(hubText))
        {
            var hubSize = ImGui.CalcTextSize(hubText);
            var hubPos = center - (hubSize / 2f);
            drawList.AddText(hubPos + new Vector2(1f, 1f), shadow, hubText);
            drawList.AddText(hubPos, ink, hubText);
        }

        var pointerHalf = 12f * scale;
        var pointerBase = center + new Vector2(0f, -radius - (2f * scale));
        var pointerTip = center + new Vector2(0f, -radius + (18f * scale));
        drawList.AddTriangleFilled(
            pointerBase + new Vector2(-pointerHalf, -4f * scale),
            pointerBase + new Vector2(pointerHalf, -4f * scale),
            pointerTip,
            ring);
    }

    private static void DrawIcon(
        ImDrawListPtr drawList,
        ITextureProvider textures,
        uint iconId,
        Vector2 center,
        float size,
        bool muted)
    {
        var lookup = new GameIconLookup(iconId);
        if (!textures.TryGetFromGameIcon(in lookup, out var texture))
            return;

        if (!texture.TryGetWrap(out var wrap, out _) || wrap == null)
            return;

        var half = new Vector2(size / 2f, size / 2f);
        if (muted)
        {
            var tint = ImGui.GetColorU32(new Vector4(0.55f, 0.55f, 0.55f, 0.85f));
            drawList.AddImage(wrap.Handle, center - half, center + half, Vector2.Zero, Vector2.One, tint);
            return;
        }

        drawList.AddImage(wrap.Handle, center - half, center + half);
    }
}
