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
        var hubInk = ImGui.GetColorU32(JobPalette.LightText);
        var hubShadow = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.80f));
        var shades = ShadesFor(entries);

        if (entries.Count == 0)
        {
            drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(new Vector4(0.16f, 0.14f, 0.12f, 1f)), 64);
        }
        else if (entries.Count == 1)
        {
            var only = SliceFill(entries[0], shades[0], mutedJobId);
            drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(only), 64);
        }
        else
        {
            var slice = MathF.Tau / entries.Count;
            for (var i = 0; i < entries.Count; i++)
            {
                var color = SliceFill(entries[i], shades[i], mutedJobId);
                var start = drawRotation + (i * slice);
                drawList.PathClear();
                drawList.PathLineTo(center);
                drawList.PathArcTo(center, radius, start, start + slice, 18);
                drawList.PathFillConvex(ImGui.GetColorU32(color));
            }

            var lightLine = ImGui.GetColorU32(new Vector4(0.96f, 0.94f, 0.88f, 0.70f));
            for (var i = 0; i < entries.Count; i++)
            {
                var start = drawRotation + (i * slice);
                var dir = new Vector2(MathF.Cos(start), MathF.Sin(start));
                var normal = new Vector2(-dir.Y, dir.X) * (1.2f * scale);
                var inner = center + (dir * radius * 0.40f);
                var outer = center + (dir * radius);
                drawList.AddLine(inner - normal, outer - normal, line, 2.2f * scale);
                drawList.AddLine(inner + normal, outer + normal, lightLine, 1.4f * scale);
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
                    var fill = SliceFill(entries[i], shades[i], mutedJobId);
                    DrawLabel(drawList, center + (dir * labelRadius) - (textSize / 2f), label, fill);
                }
            }
        }

        if (!string.IsNullOrEmpty(hubText))
        {
            var hubSize = ImGui.CalcTextSize(hubText);
            var hubPos = center - (hubSize / 2f);
            drawList.AddText(hubPos + new Vector2(1f, 1f), hubShadow, hubText);
            drawList.AddText(hubPos, hubInk, hubText);
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

    private static int[] ShadesFor(IReadOnlyList<GearsetSnapshot> entries)
    {
        var roles = new WheelRole[entries.Count];
        for (var i = 0; i < entries.Count; i++)
            roles[i] = entries[i].Role;

        return JobPalette.AlternatingShades(roles);
    }

    private static Vector4 SliceFill(GearsetSnapshot entry, int shade, uint? mutedJobId)
        => JobPalette.SliceColor(entry.Role, shade, mutedJobId == entry.ClassJobId);

    private static void DrawLabel(ImDrawListPtr drawList, Vector2 pos, string label, Vector4 background)
    {
        var halo = ImGui.GetColorU32(JobPalette.TextHalo(background));
        var ink = ImGui.GetColorU32(JobPalette.TextColor(background));
        drawList.AddText(pos + new Vector2(-1f, 0f), halo, label);
        drawList.AddText(pos + new Vector2(1f, 0f), halo, label);
        drawList.AddText(pos + new Vector2(0f, -1f), halo, label);
        drawList.AddText(pos + new Vector2(0f, 1f), halo, label);
        drawList.AddText(pos, ink, label);
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
