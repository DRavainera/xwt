//
// TreeGutter.cs — the leading gutter of a TreeView row (ListBackends.cs).
//
// It draws, to the left of the first cell of an expandable/sub-level row:
//   * one vertical guide line per ancestor level (the "indentation rails" that
//     make the depth between levels and sub-levels visible), and
//   * the expander triangle (right-pointing when collapsed, down-pointing when
//     expanded) at this node's own level.
//
// Every mark is a solid (fully opaque) filled rectangle: in this Avalonia build a
// 1px *stroked* line (DrawLine / DrawGeometry pen) is antialiased down to a few
// percent of its set alpha from inside a Control.Render override, while a *fill*
// (DrawRectangle) composites at full brightness — so the rails are 1px-wide
// rectangles and the triangle is rasterised scanline-by-scanline with rectangles.
//
// The rail/glyph colours are NOT taken from Application.ActualThemeVariant: in this
// app the window paints a dark background even though the application-level variant
// reads Light, so the variant is not a reliable signal. Instead the effective
// background the gutter sits on is read from the visual tree (first opaque solid
// background above it) and a contrasting colour is chosen from its luminance.
// The control itself is transparent, so the row's selection highlight shows through
// behind the marks. Like ListBackends.cs the Avalonia types are top-level aliases:
// inside namespace Xwt.AvaloniaBackend the bare names `Point`/`Color`/`Rectangle`/
// `DrawingContext` resolve to the Xwt frontend's own types, so the aliases (bound at
// global scope) are the only reliable way to name the Avalonia ones.
//
using System;
using Avalonia.VisualTree;
using AControl = Avalonia.Controls.Control;
using AGrid = Avalonia.Controls.Grid;
using AVisual = Avalonia.Visual;
using ADrawingContext = Avalonia.Media.DrawingContext;
using AColor = Avalonia.Media.Color;
using ASolidColorBrush = Avalonia.Media.SolidColorBrush;
using ARect = Avalonia.Rect;

namespace Xwt.AvaloniaBackend
{
	class TreeGutter : AControl
	{
		public int Depth;
		public bool HasExpander;
		public bool IsExpanded;
		public double IndentUnit = 16;
		public double ExpanderSlot = 16;

		public TreeGutter () => ClipToBounds = true;

		static AColor EffectiveBackground (AControl self)
		{
			for (AVisual? v = self; v != null; v = v.GetVisualParent ()) {
				if (v is AGrid g && g.Background is ASolidColorBrush sb && sb.Color.A > 0.5)
					return sb.Color;
			}
			return AColor.FromRgb (0x1E, 0x1E, 0x1E);
		}

		public override void Render (ADrawingContext ctx)
		{
			double w = Bounds.Width;
			double h = Bounds.Height;
			if (w < 1 || h < 1)
				return;

			var bg = EffectiveBackground (this);
			double lum = 0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B;
			bool dark = lum < 128;

			// Subtle indentation rails: one 1px-wide rect per ancestor level, aligned
			// to a full pixel so the fill lands at full coverage.
			byte rv = dark ? (byte)0x60 : (byte)0xB0;
			var rail = new ASolidColorBrush (AColor.FromRgb (rv, rv, rv), 1.0);
			for (int i = 0; i < Depth; i++) {
				double x = i * IndentUnit + IndentUnit / 2;
				ctx.DrawRectangle (rail, null, new ARect (x, 0, 1, h));
			}

			if (!HasExpander)
				return;

			// Expander triangle, contrasting the background.
			byte gv = dark ? (byte)0xE6 : (byte)0x33;
			var glyph = new ASolidColorBrush (AColor.FromRgb (gv, gv, gv), 1.0);
			double cx = Depth * IndentUnit + ExpanderSlot / 2;
			double cy = h / 2;
			double r = 4;
			if (IsExpanded) {
				// Down-pointing: wide flat top (cy-r) narrowing to the bottom apex (cy+r).
				double top = cy - r, bot = cy + r;
				for (int yy = (int)Math.Floor (top); yy <= (int)Math.Ceiling (bot); yy++) {
					double t = (yy - top) / (bot - top);
					if (t < 0 || t > 1)
						continue;
					double hw = r * (1 - t);
					if (hw < 0.25)
						continue;
					ctx.DrawRectangle (glyph, null, new ARect (cx - hw, yy, hw * 2, 1));
				}
			} else {
				// Right-pointing: flat left edge (cx-r) narrowing to the right apex (cx+r).
				double left = cx - r, apex = cx + r;
				for (int xx = (int)Math.Floor (left); xx <= (int)Math.Ceiling (apex); xx++) {
					double t = (xx - left) / (apex - left);
					if (t < 0 || t > 1)
						continue;
					double hh = r * (1 - t);
					if (hh < 0.25)
						continue;
					ctx.DrawRectangle (glyph, null, new ARect (xx, cy - hh, 1, hh * 2));
				}
			}
		}
	}
}
