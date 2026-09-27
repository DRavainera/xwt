//
// DrawingBackends.cs — wave-1 drawing backend handlers (port plan item 1):
// Context, Font, TextLayout, Gradient, ImagePattern, ImageBuilder and the
// image handler. They map the SAME Xwt.Drawing object model the GtkBackend
// maps to Cairo onto Avalonia.Media (+ SkiaSharp for raster surfaces and
// text shaping — the exact stack the MonoDevelop Avalonia shell already
// proves in SkTextEditor).
//
// Context model: the Xwt Context a Canvas frontend receives IS one of our
// ContextBackend instances (Canvas.WidgetBackendHost.OnDraw wraps whatever
// object the backend hands to ICanvasEventSink.OnDraw). We keep a Cairo-like
// Save/Restore state stack (color, line width, dash, alpha, transform,
// pattern) that is replayed onto the target SKCanvas — a real SKCanvas per
// draw pass (offscreen for images/builders, the render-loop canvas for
// widgets). Single-threaded per pass, like the toolkit contract.
//
// NOTE: inside Xwt.AvaloniaBackend the enclosing Xwt namespaces shadow
// Avalonia type names; Avalonia types are referenced through aliases.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SkiaSharp;
using Xwt.Backends;
using Xwt.Drawing;

namespace Xwt.AvaloniaBackend
{
	// ----- Draw state and session -----

	/// <summary>Draw state snapshot for Save/Restore (Cairo-style stack).</summary>
	class DrawState
	{
		public Color Color = new Color (0, 0, 0, 1);
		public double LineWidth = 2;
		public double[] Dash;
		public double DashOffset;
		public double GlobalAlpha = 1;
		public SKMatrix Transform = SKMatrix.Identity;
		public SKShader Pattern; // active gradient/image-pattern shader (owned by the state)
	}

	/// <summary>
	/// One drawing session: the target SKCanvas, the state stack and the path
	/// being built (paths survive Fill/Stroke/Clip with preserve semantics).
	/// </summary>
	class SkDrawContext
	{
		readonly Stack<DrawState> stack = new ();

		public SKCanvas Canvas;
		public SKPath Path = new ();
		public DrawState Current;

		public SkDrawContext (SKCanvas canvas)
		{
			Canvas = canvas;
			Current = new DrawState ();
			stack.Push (Current);
		}

		public void Save ()
		{
			var s = new DrawState {
				Color = Current.Color,
				LineWidth = Current.LineWidth,
				Dash = Current.Dash,
				DashOffset = Current.DashOffset,
				GlobalAlpha = Current.GlobalAlpha,
				Transform = Current.Transform,
				Pattern = Current.Pattern, // shared, owned by the pusher
			};
			stack.Push (s);
			Current = s;
		}

		public void Restore ()
		{
			if (stack.Count > 1) {
				if (Current.Pattern is not null && !ReferenceEquals (Current.Pattern, stack.Peek ().Pattern))
					Current.Pattern.Dispose ();
				stack.Pop ();
			}
			Current = stack.Peek ();
		}

		public void ResetPath ()
		{
			Path.Rewind ();
		}

		/// <summary>Paint for a stroke/fill pass; installs the active pattern
		/// shader over the plain color when one is set.</summary>
		public SKPaint CreatePaint (SKPaintStyle style)
		{
			var paint = new SKPaint {
				Style = style,
				IsAntialias = true,
				Color = SkDrawExtensions.ToSkColor (Current.Color, Current.GlobalAlpha),
				StrokeWidth = (float)Current.LineWidth,
			};
			if (Current.Dash is { Length: > 0 })
				paint.PathEffect = SKPathEffect.CreateDash (
					Current.Dash.Select (d => (float)d).ToArray (), (float)Current.DashOffset);
			if (Current.Pattern is not null)
				paint.Shader = Current.Pattern;
			return paint;
		}

		public SKPaint CreateFillPaint () => CreatePaint (SKPaintStyle.Fill);

		public void ApplyTransform () => Canvas.SetMatrix (Current.Transform);
	}

	static class SkDrawExtensions
	{
		public static SKColor ToSkColor (Color c, double alpha = 1)
			=> new SKColor (
				(byte)Math.Round (c.Red * 255), (byte)Math.Round (c.Green * 255),
				(byte)Math.Round (c.Blue * 255), (byte)Math.Round (Math.Min (1, c.Alpha * alpha) * 255));

		public static Color ToXwt (this Avalonia.Media.Color c)
			=> new Color (c.R / 255d, c.G / 255d, c.B / 255d, c.A / 255d);

		/// <summary>Xwt 2x3 matrix (row-vector: x' = x·M11 + y·M21 + OffsetX)
		/// → SKMatrix (column-vector) layout.</summary>
		public static SKMatrix ToSkMatrix (Matrix m)
			=> new SKMatrix ((float)m.M11, (float)m.M21, (float)m.OffsetX,
				(float)m.M12, (float)m.M22, (float)m.OffsetY, 0, 0, 1);

		public static Matrix ToXwtMatrix (SKMatrix m)
			=> new Matrix (m.ScaleX, m.SkewY, m.SkewX, m.ScaleY, m.TransX, m.TransY);
	}

	// ----- Gradient backend (SKShader with stored stops) -----

	/// <summary>Gradient backend handle: geometry + color stops; materializes
	/// an SKShader when a paint needs it (disposed with the owning state).</summary>
	public class GradientData
	{
		public bool Linear;
		public double X0, Y0, X1, Y1, R0, R1;
		public readonly List<(double Position, Color Color)> Stops = new ();

		public SKShader CreateShader ()
		{
			var stops = Stops.OrderBy (s => s.Position).ToArray ();
			var colors = stops.Select (s => SkDrawExtensions.ToSkColor (s.Color)).ToArray ();
			var positions = stops.Select (s => (float)s.Position).ToArray ();
			if (colors.Length == 0)
				colors = new[] { SKColors.Black };
			return Linear
				? SKShader.CreateLinearGradient (new SKPoint ((float)X0, (float)Y0), new SKPoint ((float)X1, (float)Y1), colors, positions, SKShaderTileMode.Clamp)
				: SKShader.CreateRadialGradient (new SKPoint ((float)X1, (float)Y1), (float)Math.Max (0.01, R1), colors, positions, SKShaderTileMode.Clamp);
		}
	}

	// ----- Context backend -----

	public class ContextBackend : ContextBackendHandler
	{
		static SkDrawContext DC (object backend) => (SkDrawContext)backend;

		static SKPath P (object backend) => DC (backend).Path;

		public override void Save (object backend) => DC (backend).Save ();

		public override void Restore (object backend) => DC (backend).Restore ();

		public override void Clip (object backend)
		{
			var dc = DC (backend);
			if (!dc.Path.IsEmpty) {
				dc.ApplyTransform ();
				dc.Canvas.ClipPath (dc.Path, SKClipOperation.Intersect, true);
			}
			dc.ResetPath ();
		}

		public override void ClipPreserve (object backend)
		{
			var dc = DC (backend);
			if (!dc.Path.IsEmpty) {
				dc.ApplyTransform ();
				dc.Canvas.ClipPath (dc.Path, SKClipOperation.Intersect, true);
			}
		}

		public override void Fill (object backend)
		{
			var dc = DC (backend);
			if (!dc.Path.IsEmpty) {
				dc.ApplyTransform ();
				using var paint = dc.CreateFillPaint ();
				dc.Canvas.DrawPath (dc.Path, paint);
			}
			dc.ResetPath ();
		}

		public override void FillPreserve (object backend)
		{
			var dc = DC (backend);
			if (!dc.Path.IsEmpty) {
				dc.ApplyTransform ();
				using var paint = dc.CreateFillPaint ();
				dc.Canvas.DrawPath (dc.Path, paint);
			}
		}

		public override void NewPath (object backend) => DC (backend).ResetPath ();

		public override void Stroke (object backend)
		{
			var dc = DC (backend);
			if (!dc.Path.IsEmpty) {
				dc.ApplyTransform ();
				using var paint = dc.CreatePaint (SKPaintStyle.Stroke);
				dc.Canvas.DrawPath (dc.Path, paint);
			}
			dc.ResetPath ();
		}

		public override void StrokePreserve (object backend)
		{
			var dc = DC (backend);
			if (!dc.Path.IsEmpty) {
				dc.ApplyTransform ();
				using var paint = dc.CreatePaint (SKPaintStyle.Stroke);
				dc.Canvas.DrawPath (dc.Path, paint);
			}
		}

		public override void SetColor (object backend, Color color) => DC (backend).Current.Color = color;

		public override void SetLineWidth (object backend, double width) => DC (backend).Current.LineWidth = width;

		public override void SetLineDash (object backend, double offset, params double[] pattern)
		{
			var dc = DC (backend);
			dc.Current.Dash = pattern;
			dc.Current.DashOffset = offset;
		}

		public override void SetPattern (object backend, object p)
		{
			// p arrives as the FRONTEND Pattern (VectorImage records it as-is);
			// resolve to the backend handle like Xwt.WPF does: a GradientData
			// (gradient) or the SKBitmap of an image pattern.
			p = Toolkit.CurrentEngine.GetSafeBackend (p);
			SKShader shader = p switch {
				GradientData g => g.CreateShader (),
				SKBitmap bmp when bmp is not null => SKShader.CreateBitmap (bmp, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat),
				_ => null,
			};
			var dc = DC (backend);
			dc.Current.Pattern?.Dispose ();
			dc.Current.Pattern = shader;
		}

		public override void DrawTextLayout (object backend, TextLayout layout, double x, double y)
		{
			var tl = Toolkit.GetBackend (layout) as AvaloniaTextLayout;
			tl?.Draw (DC (backend), (float)x, (float)y);
		}

		public override void DrawImage (object backend, ImageDescription img, double x, double y)
		{
			// The image description carries the size the frontend asked for
			// (custom-drawn/vector images have no intrinsic backend size).
			var size = img.Size.Width > 0 && img.Size.Height > 0
				? img.Size
				: new AvaloniaImageBackend ().GetSize (img.Backend);
			if (size.Width <= 0 || size.Height <= 0)
				return;
			DrawImage (backend, img, new Rectangle (0, 0, size.Width, size.Height), new Rectangle (x, y, size.Width, size.Height));
		}

		public override void DrawImage (object backend, ImageDescription img, Rectangle srcRect, Rectangle destRect)
		{
			var dc = DC (backend);
			if (img.IsNull || destRect.Width <= 0 || destRect.Height <= 0)
				return;
			var bitmap = AvaloniaImageBackend.GetBitmap (img.Backend);
			if (bitmap is null)
				return;
			dc.ApplyTransform ();
			using var paint = new SKPaint {
				IsAntialias = true,
				Color = SKColors.White.WithAlpha ((byte)Math.Round (Math.Clamp (img.Alpha, 0, 1) * 255)),
			};
			var src = new SKRect ((float)srcRect.X, (float)srcRect.Y, (float)(srcRect.X + srcRect.Width), (float)(srcRect.Y + srcRect.Height));
			var dst = new SKRect ((float)destRect.X, (float)destRect.Y, (float)(destRect.X + destRect.Width), (float)(destRect.Y + destRect.Height));
			dc.Canvas.DrawBitmap (bitmap, src, dst, paint);
		}

		public override void Rotate (object backend, double angle)
		{
			var dc = DC (backend);
			dc.Current.Transform = dc.Current.Transform.PostConcat (SKMatrix.CreateRotation ((float)angle));
		}

		public override void Scale (object backend, double scaleX, double scaleY)
		{
			var dc = DC (backend);
			dc.Current.Transform = dc.Current.Transform.PostConcat (SKMatrix.CreateScale ((float)scaleX, (float)scaleY));
		}

		public override void Translate (object backend, double tx, double ty)
		{
			var dc = DC (backend);
			dc.Current.Transform = dc.Current.Transform.PostConcat (SKMatrix.CreateTranslation ((float)tx, (float)ty));
		}

		public override void ModifyCTM (object backend, Matrix transform)
		{
			var dc = DC (backend);
			dc.Current.Transform = dc.Current.Transform.PostConcat (SkDrawExtensions.ToSkMatrix (transform));
		}

		public override Matrix GetCTM (object backend) => SkDrawExtensions.ToXwtMatrix (DC (backend).Current.Transform);

		public override bool IsPointInStroke (object backend, double x, double y)
		{
			// SKPath has no stroke hit-test; widen-copy like the classic
			// backends do (getFillPath with the stroke paint).
			var dc = DC (backend);
			using var paint = dc.CreatePaint (SKPaintStyle.Stroke);
			paint.StrokeWidth = Math.Max (paint.StrokeWidth, 6);
			using var widened = new SKPath ();
			return paint.GetFillPath (dc.Path, widened) && widened.Contains ((float)x, (float)y);
		}

		public override bool IsPointInFill (object backend, double x, double y) => DC (backend).Path.Contains ((float)x, (float)y);

		public override void SetGlobalAlpha (object backend, double globalAlpha) => DC (backend).Current.GlobalAlpha = globalAlpha;

		public override double GetScaleFactor (object backend)
			=> Math.Max (0.01, Math.Abs (DC (backend).Current.Transform.ScaleX));

		public override void SetStyles (object backend, StyleSet styles) { } // AVALONIA-PORT: style sets

		// ----- Path primitives (Cairo-style on the shared SKPath) -----

		public override void Arc (object backend, double xc, double yc, double radius, double angle1, double angle2)
		{
			// Cairo angles are radians, 0 = +X, growing clockwise (screen Y-down
			// matches SKIA); convert to degrees for SKPath.ArcTo.
			var rect = new SKRect ((float)(xc - radius), (float)(yc - radius), (float)(xc + radius), (float)(yc + radius));
			var start = (float)(angle1 * 180 / Math.PI);
			var sweep = (float)((angle2 - angle1) * 180 / Math.PI);
			P (backend).ArcTo (rect, start, sweep, false);
		}

		public override void ArcNegative (object backend, double xc, double yc, double radius, double angle1, double angle2)
		{
			var rect = new SKRect ((float)(xc - radius), (float)(yc - radius), (float)(xc + radius), (float)(yc + radius));
			var start = (float)(angle1 * 180 / Math.PI);
			var sweep = (float)((angle2 - angle1) * 180 / Math.PI);
			P (backend).ArcTo (rect, start, -sweep, false);
		}

		public override void ClosePath (object backend) => P (backend).Close ();

		public override void CurveTo (object backend, double x1, double y1, double x2, double y2, double x3, double y3)
			=> P (backend).CubicTo ((float)x1, (float)y1, (float)x2, (float)y2, (float)x3, (float)y3);

		public override void LineTo (object backend, double x, double y) => P (backend).LineTo ((float)x, (float)y);

		public override void MoveTo (object backend, double x, double y) => P (backend).MoveTo ((float)x, (float)y);

		public override void Rectangle (object backend, double x, double y, double width, double height)
			=> P (backend).AddRect (new SKRect ((float)x, (float)y, (float)(x + width), (float)(y + height)));

		public override void RelCurveTo (object backend, double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
			=> P (backend).RCubicTo ((float)dx1, (float)dy1, (float)dx2, (float)dy2, (float)dx3, (float)dy3);

		public override void RelLineTo (object backend, double dx, double dy) => P (backend).RLineTo ((float)dx, (float)dy);

		public override void RelMoveTo (object backend, double dx, double dy) => P (backend).RMoveTo ((float)dx, (float)dy);

		public override object CreatePath () => new SKPath ();

		public override object CopyPath (object backend) => new SKPath (P (backend));

		public override void AppendPath (object backend, object otherBackend)
			=> P (backend).AddPath ((SKPath)otherBackend);

		public override void Dispose (object backend) { } // paths belong to the session
	}

	// ----- Font backend -----

	/// <summary>
	/// Font backend handle: family + size + style variations. Measurement and
	/// raster resolve to SkiaSharp (the fontconfig-backed manager, like the
	/// shell); Avalonia's own stack shapes its controls separately.
	/// </summary>
	public class FontData
	{
		public string Family = "";
		public double Size = 12;
		public FontStyle Style;
		public FontWeight Weight;
		public FontStretch Stretch;

		/// <summary>Optional per-run text color (ColorTextAttribute).</summary>
		public Color? Foreground;

		public FontData Clone () => (FontData)MemberwiseClone ();

		public SKFont CreateSkFont ()
			=> new SKFont (FontCache.ResolveTypeface (Family, Style, Weight, Stretch), (float)Size);
	}

	/// <summary>Resolves family/style/weight to SKTypeface with a cache;
	/// falls back to the platform default when the family is unknown.</summary>
	static class FontCache
	{
		static readonly Dictionary<(string, FontStyle, FontWeight, FontStretch), SKTypeface> cache = new ();

		/// <summary>Families registered at runtime (RegisterFontFromFile) —
		/// SKTypeface.FromFamilyName does not see them.</summary>
		public static readonly HashSet<string> RegisteredFamilies = new (StringComparer.OrdinalIgnoreCase);

		public static SKTypeface ResolveTypeface (string family, FontStyle style, FontWeight weight, FontStretch stretch)
		{
			var key = (family ?? "", style, weight, stretch);
			if (cache.TryGetValue (key, out var tf))
				return tf;
			var skWeight = weight switch {
				FontWeight.Thin => SKFontStyleWeight.Thin,
				FontWeight.Ultralight => SKFontStyleWeight.ExtraLight,
				FontWeight.Light => SKFontStyleWeight.Light,
				FontWeight.Semilight => SKFontStyleWeight.SemiBold,
				FontWeight.Semibold => SKFontStyleWeight.SemiBold,
				FontWeight.Bold => SKFontStyleWeight.Bold,
				FontWeight.Ultrabold => SKFontStyleWeight.ExtraBold,
				FontWeight.Heavy => SKFontStyleWeight.Black,
				_ => SKFontStyleWeight.Normal,
			};
			var skWidth = stretch switch {
				FontStretch.Condensed => SKFontStyleWidth.Condensed,
				FontStretch.SemiCondensed => SKFontStyleWidth.SemiCondensed,
				FontStretch.SemiExpanded => SKFontStyleWidth.SemiExpanded,
				FontStretch.Expanded => SKFontStyleWidth.Expanded,
				FontStretch.ExtraExpanded => SKFontStyleWidth.Expanded,
				FontStretch.UltraExpanded => SKFontStyleWidth.Expanded,
				_ => SKFontStyleWidth.Normal,
			};
			var slant = style == FontStyle.Italic || style == FontStyle.Oblique
				? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
			tf = SKTypeface.FromFamilyName (string.IsNullOrWhiteSpace (family) ? DefaultFamily () : family, (int)skWeight, (int)skWidth, slant);
			cache [key] = tf;
			return tf;
		}

		/// <summary>The fontconfig-backed Skia manager can report an empty
		/// default family (SkiaSharp 3.119 on Linux): SKTypeface.Default then
		/// yields zero metrics. Pick a real installed family instead — first
		/// a well-known sans, else the first non-empty family.</summary>
		public static string DefaultFamily ()
		{
			var name = SKTypeface.Default.FamilyName;
			if (!string.IsNullOrWhiteSpace (name))
				return name;
			foreach (var c in new[] { "DejaVu Sans", "Liberation Sans", "Noto Sans", "Cantarell", "FreeSans" })
				foreach (var f in SKFontManager.Default.FontFamilies)
					if (string.Equals (f, c, StringComparison.OrdinalIgnoreCase))
						return c;
			foreach (var f in SKFontManager.Default.FontFamilies)
				if (!string.IsNullOrWhiteSpace (f))
					return f;
			return name;
		}
	}

	public class FontBackend : FontBackendHandler
	{
		// Default families resolve at RUNTIME through the Skia font manager:
		// hardcoding Windows families yields broken typefaces on Linux, and
		// SKTypeface.Default can even report an empty family (see FontCache).
		public override object GetSystemDefaultFont ()
			=> new FontData { Family = FontCache.DefaultFamily (), Size = 12 };

		public override object GetSystemDefaultSansSerifFont ()
			=> new FontData { Family = FontCache.DefaultFamily (), Size = 12 };

		public override object GetSystemDefaultMonospaceFont ()
			=> new FontData { Family = FirstInstalledFamily (
				"Cascadia Mono", "Cascadia Code", "DejaVu Sans Mono", "Noto Sans Mono",
				"Liberation Mono", "FreeMono", " monospace") ?? FontCache.DefaultFamily (), Size = 12 };

		public override object GetSystemDefaultSerifFont ()
			=> new FontData { Family = FirstInstalledFamily (
				"DejaVu Serif", "Liberation Serif", "Noto Serif", "FreeSerif", "Times New Roman", "serif")
				?? FontCache.DefaultFamily (), Size = 12 };

		static string FirstInstalledFamily (params string[] candidates)
		{
			foreach (var c in candidates)
				foreach (var f in SKFontManager.Default.FontFamilies)
					if (string.Equals (f, c, StringComparison.OrdinalIgnoreCase))
						return c;
			return null;
		}

		public override IEnumerable<string> GetInstalledFonts ()
			=> SKFontManager.Default.FontFamilies.OrderBy (f => f, StringComparer.OrdinalIgnoreCase);

		public override IEnumerable<KeyValuePair<string, object>> GetAvailableFamilyFaces (string family)
		{
			// Wave-1: the four classic style buckets (faces resolve through the
			// typeface cache; per-face enumeration lands with Avalonia's
			// FontManagerModel in a later wave).
			foreach (var (style, weight, name) in new[] {
				(FontStyle.Normal, FontWeight.Normal, "Regular"),
				(FontStyle.Normal, FontWeight.Bold, "Bold"),
				(FontStyle.Italic, FontWeight.Normal, "Italic"),
				(FontStyle.Italic, FontWeight.Bold, "Bold Italic"),
			})
				yield return new KeyValuePair<string, object> (name,
					new FontData { Family = family, Size = 12, Style = style, Weight = weight });
		}

		public override object Create (string fontName, double size, FontStyle style, FontWeight weight, FontStretch stretch)
		{
			// Contract: null when the family is not available.
			if (!string.IsNullOrWhiteSpace (fontName) && !IsFamilyKnown (fontName))
				return null;
			return new FontData { Family = fontName, Size = size, Style = style, Weight = weight, Stretch = stretch };
		}

		// Skia's fontconfig manager is the family source of truth; fonts
		// registered at runtime are kept in FontCache.RegisteredFamilies.
		static bool IsFamilyKnown (string family)
		{
			if (FontCache.RegisteredFamilies.Contains (family))
				return true;
			foreach (var f in SKFontManager.Default.FontFamilies)
				if (string.Equals (f, family, StringComparison.OrdinalIgnoreCase))
					return true;
			return false;
		}

		public override bool RegisterFontFromFile (string fontPath)
		{
			try {
				var tf = SKFontManager.Default.CreateTypeface (fontPath);
				if (tf is null)
					return false;
				FontCache.RegisteredFamilies.Add (tf.FamilyName);
				return true;
			} catch {
				return false;
			}
		}

		public override object Copy (object handle) => ((FontData)handle).Clone ();

		public override object SetSize (object handle, double size) { var f = ((FontData)handle).Clone (); f.Size = size; return f; }

		public override object SetFamily (object handle, string family) { var f = ((FontData)handle).Clone (); f.Family = family; return f; }

		public override object SetStyle (object handle, FontStyle style) { var f = ((FontData)handle).Clone (); f.Style = style; return f; }

		public override object SetWeight (object handle, FontWeight weight) { var f = ((FontData)handle).Clone (); f.Weight = weight; return f; }

		public override object SetStretch (object handle, FontStretch stretch) { var f = ((FontData)handle).Clone (); f.Stretch = stretch; return f; }

		public override double GetSize (object handle) => ((FontData)handle).Size;

		public override string GetFamily (object handle) => ((FontData)handle).Family;

		public override FontStyle GetStyle (object handle) => ((FontData)handle).Style;

		public override FontWeight GetWeight (object handle) => ((FontData)handle).Weight;

		public override FontStretch GetStretch (object handle) => ((FontData)handle).Stretch;
	}

	// ----- Text layout backend -----

	/// <summary>TextLayout backend: single-run shaping through SKFont, with
	/// per-range attributes (color/weight/style/size) split into runs.</summary>
	public class AvaloniaTextLayout : IDisposable
	{
		public string Text = "";
		public FontData Font = new ();
		public double Width = -1, Height = -1;
		public TextTrimming Trimming;
		public Alignment Alignment;

		readonly List<TextAttribute> attributes = new ();
		// One blob per run: a text blob draws with a single paint color, so
		// per-run ColorTextAttribute colors need separate blobs.
		readonly List<(SKTextBlob Blob, Color? Color, float Width)> runBlobs = new ();
		double measuredWidth, measuredHeight;

		sealed class TextRun
		{
			public string Text = "";
			public FontData Font;
			public Color? Color;
		}

		public void Rebuild ()
		{
			Reset ();
			if (string.IsNullOrEmpty (Text))
				return;
			var			runs = SplitByAttributes ();
			// Ellipsize the last run when it does not fit the layout width.
			if (Width > 0 && Trimming == TextTrimming.WordElipsis && runs.Count > 0) {
				var last = runs [^1];
				float baseWidth = TotalWidth (runs) - WidthOf (last);
				if (baseWidth + WidthOf (last) > Width) {
					using var font = last.Font.CreateSkFont ();
					int len = last.Text.Length;
					while (len > 1 && baseWidth + font.MeasureText (last.Text [..len] + "…") > Width)
						len--;
					last.Text = last.Text [..len] + "…";
				}
			}
			float baseline = 0, ascent = 0;
			foreach (var run in runs) {
				using var font = run.Font.CreateSkFont ();
				var metrics = font.Metrics;
				ascent = Math.Max (ascent, -metrics.Ascent);
				baseline = Math.Max (baseline, metrics.Descent - metrics.Ascent);
			}
			// Whole-layout alignment: shift the run origins.
			float x = 0;
			if (Width > 0 && Alignment != Alignment.Start) {
				float slack = (float)Math.Max (0, Width - TotalWidth (runs));
				x = Alignment == Alignment.Center ? slack / 2 : slack;
			}
			float textWidth = 0;
			foreach (var run in runs) {
				using var font = run.Font.CreateSkFont ();
				ushort [] glyphs = font.GetGlyphs (run.Text);
				var builder = new SKTextBlobBuilder ();
				builder.AddRun (glyphs, font, new SKPoint (0, ascent));
				var b = builder.Build ();
				float w = font.MeasureText (run.Text);
				runBlobs.Add ((b, run.Color, w));
				x += w;
				textWidth += w;
			}
			measuredWidth = Math.Max (textWidth, 1);
			measuredHeight = Height > 0 ? Height : Math.Max (baseline, 1);
		}

		static float WidthOf (TextRun run)
		{
			using var font = run.Font.CreateSkFont ();
			return font.MeasureText (run.Text);
		}

		float TotalWidth (List<TextRun> runs)
		{
			float w = 0;
			foreach (var run in runs)
				using (var font = run.Font.CreateSkFont ())
					w += font.MeasureText (run.Text);
			return w;
		}

		List<TextRun> SplitByAttributes ()
		{
			// Segment the text by attribute ranges; uncovered spans keep the
			// base font. Overlapping ranges: last attribute wins per span.
			var runs = new List<TextRun> ();
			if (attributes.Count == 0) {
				runs.Add (new TextRun { Text = Text, Font = Font.Clone () });
				return runs;
			}
			var fontAt = new FontData [Text.Length];
			var perColor = new Dictionary<int, (int Len, Color Color)> ();
			var marked = new bool [Text.Length];
			foreach (var attr in attributes) {
				var start = Math.Clamp (attr.StartIndex, 0, Text.Length);
				var len = Math.Clamp (attr.Count, 0, Text.Length - start);
				for (int i = start; i < start + len; i++)
					marked [i] = true;
				var f = Font.Clone ();
				ApplyAttribute (f, attr);
				if (f.Foreground is Color c) {
					perColor [start] = (len, c);
					f.Foreground = null;
				}
				for (int i = start; i < start + len; i++)
					fontAt [i] = f;
			}
			int idx = 0;
			while (idx < Text.Length) {
				var f = fontAt [idx];
				int len = 1;
				while (idx + len < Text.Length && ReferenceEquals (fontAt [idx + len], f) && len < Text.Length)
					len++;
				Color? runColor = null;
				if (perColor.TryGetValue (idx, out var pc) && pc.Len == len)
					runColor = pc.Color;
				runs.Add (new TextRun { Text = Text.Substring (idx, len), Font = f is not null ? f : Font.Clone (), Color = runColor });
				idx += len;
			}
			return runs;
		}

		static void ApplyAttribute (FontData f, TextAttribute attr)
		{
			switch (attr) {
			case ColorTextAttribute c: f.Foreground = c.Color; break;
			case FontWeightTextAttribute w: f.Weight = w.Weight; break;
			case FontStyleTextAttribute s: f.Style = s.Style; break;
			case FontSizeTextAttribute sz: f.Size = sz.Size; break;
			}
		}

		void Reset ()
		{
			foreach (var (b, _, _) in runBlobs)
				b.Dispose ();
			runBlobs.Clear ();
		}

		internal void Draw (SkDrawContext dc, float x, float y)
		{
			if (runBlobs.Count == 0)
				Rebuild ();
			dc.ApplyTransform ();
			// Base color: context color (or the layout Font.Foreground); each
			// run blob overrides it when it carries a ColorTextAttribute.
			var baseColor = dc.Current.Color;
			if (Font.Foreground is Color fg)
				baseColor = fg;
			foreach (var (b, color, _) in runBlobs) {
				using var paint = new SKPaint { IsAntialias = true };
			paint.Color = SkDrawExtensions.ToSkColor (color is Color c ? c : baseColor, dc.Current.GlobalAlpha);
			dc.Canvas.DrawText (b, x, y, paint);
			}
		}

		public Size GetSize ()
		{
			if (runBlobs.Count == 0 && !string.IsNullOrEmpty (Text))
				Rebuild ();
			return new Size (measuredWidth, measuredHeight);
		}

		public int GetIndexFromCoordinates (double px, double py)
		{
			if (string.IsNullOrEmpty (Text))
				return 0;
			using var font = Font.CreateSkFont ();
			float acc = 0;
			for (int i = 0; i < Text.Length; i++) {
				float cw = font.MeasureText (Text [i].ToString ());
				if (px < acc + cw / 2)
					return i;
				acc += cw;
			}
			return Text.Length;
		}

		public Point GetCoordinateFromIndex (int index)
		{
			if (string.IsNullOrEmpty (Text))
				return new Point (0, 0);
			index = Math.Clamp (index, 0, Text.Length);
			using var font = Font.CreateSkFont ();
			float acc = 0;
			for (int i = 0; i < index; i++)
				acc += font.MeasureText (Text [i].ToString ());
			return new Point (acc, 0);
		}

		public double GetBaseline ()
		{
			using var font = Font.CreateSkFont ();
			return -font.Metrics.Ascent;
		}

		public double GetMeanline ()
		{
			using var font = Font.CreateSkFont ();
			return -font.Metrics.XHeight;
		}

		public void AddAttribute (TextAttribute attribute) { attributes.Add (attribute); Invalidate (); }

		public void ClearAttributes () { attributes.Clear (); Invalidate (); }

		void Invalidate ()
		{
			Reset ();
		}

		public void Dispose () => Reset ();
	}

	public class TextLayoutBackend : TextLayoutBackendHandler
	{
		public override object Create () => new AvaloniaTextLayout ();

		public override void SetWidth (object backend, double value) { var t = (AvaloniaTextLayout)backend; t.Width = value; t.Rebuild (); }

		public override void SetHeight (object backend, double value) { var t = (AvaloniaTextLayout)backend; t.Height = value; t.Rebuild (); }

		public override void SetText (object backend, string text) { var t = (AvaloniaTextLayout)backend; t.Text = text ?? ""; t.Rebuild (); }

		public override void SetFont (object backend, Font font)
		{
			var t = (AvaloniaTextLayout)backend;
			t.Font = font is null ? new FontData () : new FontData {
				Family = font.Family,
				Size = font.Size,
				Style = font.Style,
				Weight = font.Weight,
				Stretch = font.Stretch,
			};
			t.Rebuild ();
		}

		public override void SetTrimming (object backend, TextTrimming textTrimming) { var t = (AvaloniaTextLayout)backend; t.Trimming = textTrimming; t.Rebuild (); }

		public override void SetAlignment (object backend, Alignment alignment) { var t = (AvaloniaTextLayout)backend; t.Alignment = alignment; t.Rebuild (); }

		public override Size GetSize (object backend) => ((AvaloniaTextLayout)backend).GetSize ();

		public override int GetIndexFromCoordinates (object backend, double x, double y) => ((AvaloniaTextLayout)backend).GetIndexFromCoordinates (x, y);

		public override Point GetCoordinateFromIndex (object backend, int index) => ((AvaloniaTextLayout)backend).GetCoordinateFromIndex (index);

		public override double GetBaseline (object backend) => ((AvaloniaTextLayout)backend).GetBaseline ();

		public override double GetMeanline (object backend) => ((AvaloniaTextLayout)backend).GetMeanline ();

		public override void AddAttribute (object backend, TextAttribute attribute) => ((AvaloniaTextLayout)backend).AddAttribute (attribute);

		public override void ClearAttributes (object backend) => ((AvaloniaTextLayout)backend).ClearAttributes ();

		public override void Dispose (object backend) => ((AvaloniaTextLayout)backend).Dispose ();
	}

	// ----- Gradient / image pattern backends -----

	public class GradientBackend : GradientBackendHandler
	{
		public override object CreateLinear (double x0, double y0, double x1, double y1)
			=> new GradientData { Linear = true, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 };

		public override object CreateRadial (double cx0, double cy0, double radius0, double cx1, double cy1, double radius1)
			=> new GradientData { Linear = false, X0 = cx0, Y0 = cy0, R0 = radius0, X1 = cx1, Y1 = cy1, R1 = radius1 };

		public override void AddColorStop (object backend, double position, Color color)
			=> ((GradientData)backend).Stops.Add ((position, color));

		public override void Dispose (object backend) { } // shaders dispose with the draw state
	}

	public class ImagePatternBackend : ImagePatternBackendHandler
	{
		// The pattern handle is the image's SKBitmap; ContextBackend.SetPattern
		// materializes the repeating shader from it.
		public override object Create (ImageDescription img)
			=> AvaloniaImageBackend.GetBitmap (img.Backend);

		public override void Dispose (object backend) { }
	}

	// ----- Image builder backend (offscreen raster) -----

	/// <summary>ImageBuilder handle: an offscreen surface plus its draw
	/// session (the ImageBuilder frontend draws through Context).</summary>
	public class AvaloniaImageBuilder
	{
		public SKBitmap Bitmap;
		public SKSurface Surface;
	}

	public class ImageBuilderBackend : ImageBuilderBackendHandler
	{
		public override object CreateImageBuilder (int width, int height, ImageFormat format)
		{
			var info = new SKImageInfo (Math.Max (1, width), Math.Max (1, height),
				SKColorType.Bgra8888, format == ImageFormat.ARGB32 ? SKAlphaType.Premul : SKAlphaType.Opaque);
			var bmp = new SKBitmap (info);
			var surface = SKSurface.Create (info, bmp.GetPixels (), bmp.RowBytes)
				?? throw new InvalidOperationException ("Could not create the offscreen surface");
			surface.Canvas.Clear (SKColors.Transparent);
			return new AvaloniaImageBuilder { Bitmap = bmp, Surface = surface };
		}

		public override object CreateContext (object backend)
		{
			var b = (AvaloniaImageBuilder)backend;
			return new SkDrawContext (b.Surface.Canvas);
		}

		public override object CreateImage (object backend)
		{
			var b = (AvaloniaImageBuilder)backend;
			b.Surface.Canvas.Flush ();
			return new AvaloniaImageData { Bitmap = new SKBitmap (b.Bitmap.Info) .Tap (dst => b.Bitmap.CopyTo (dst)) };
		}

		public override void Dispose (object backend)
		{
			var b = (AvaloniaImageBuilder)backend;
			b.Surface?.Dispose ();
			b.Bitmap?.Dispose ();
		}
	}

	// ----- Image backend -----

	/// <summary>Image backend handle: an SKBitmap plus optional custom-draw
	/// callback (rasterized offscreen on first use).</summary>
	public class AvaloniaImageData : IDisposable
	{
		public SKBitmap Bitmap;
		public ImageDrawCallback CustomDraw;
		public double CustomWidth, CustomHeight;
		public List<AvaloniaImageData> MultiResolution;

		public void Dispose ()
		{
			Bitmap?.Dispose ();
			Bitmap = null;
			if (MultiResolution is not null) {
				foreach (var r in MultiResolution)
					r.Dispose ();
				MultiResolution = null;
			}
		}
	}

	public class AvaloniaImageBackend : ImageBackendHandler
	{
		// Custom-draw rasterization is reachable from static paths (Context
		// handlers do not own an image-backend instance); the active toolkit
		// resolves lazily (CurrentEngine is set once the toolkit loads).
		static Toolkit T => Toolkit.CurrentEngine;
		internal static SKBitmap GetBitmap (object backend)
		{
			if (backend is not AvaloniaImageData d)
				return null;
			if (d.Bitmap is not null)
				return d.Bitmap;
			if (d.CustomDraw is not null) {
				// Custom-drawn image: rasterize once at its declared size on
				// first draw (MonoDevelop draws these at fixed sizes).
				var size = GetSizeStatic (d);
				int w = Math.Max (1, (int)Math.Ceiling (size.Width));
				int h = Math.Max (1, (int)Math.Ceiling (size.Height));
				d.Bitmap = RenderCustomDrawStatic (d, w, h);
				return d.Bitmap;
			}
			if (d.MultiResolution is { Count: > 0 } list)
				return GetBitmap (list [0]);
			return null;
		}

		static Size GetSizeStatic (object handle)
		{
			switch (handle) {
			case AvaloniaImageData d when d.CustomDraw is not null:
				return new Size (d.CustomWidth, d.CustomHeight);
			case AvaloniaImageData d when d.MultiResolution is { Count: > 0 } list:
				return GetSizeStatic (list [0]);
			case AvaloniaImageData d when d.Bitmap is not null:
				return new Size (d.Bitmap.Width, d.Bitmap.Height);
			default:
				return Size.Zero;
			}
		}

		static SKBitmap RenderCustomDrawStatic (AvaloniaImageData data, int w, int h, double scaleFactor = 1)
		{
			var info = new SKImageInfo (w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
			var bmp = new SKBitmap (info);
			using var surface = SKSurface.Create (info, bmp.GetPixels (), bmp.RowBytes);
			if (surface is null)
				return bmp;
			surface.Canvas.Clear (SKColors.Transparent);
			var ctx = new SkDrawContext (surface.Canvas);
			if (scaleFactor != 1)
				ctx.Current.Transform = SKMatrix.CreateScale ((float)scaleFactor, (float)scaleFactor);
			try {
				// Alpha defaults to 0 in ImageDescription; the recorded draw
				// applies it as global alpha, so an unset value hides everything.
				data.CustomDraw (ctx, new Rectangle (0, 0, w / scaleFactor, h / scaleFactor),
					new ImageDescription { Backend = data, Size = new Size (w / scaleFactor, h / scaleFactor), Alpha = 1 },
					T);
				surface.Canvas.Flush ();
			} catch (Exception ex) {
				// A throwing custom draw must not take the toolkit down.
				Console.Error.WriteLine ($"[Xwt.Avalonia] custom-draw replay failed: {ex}");
			}
			return bmp;
		}

		public override object LoadFromStream (Stream stream)
		{
			var bmp = SKBitmap.Decode (stream)
				?? throw new InvalidOperationException ("Unsupported image format");
			return new AvaloniaImageData { Bitmap = bmp };
		}

		public override void SaveToStream (object backend, Stream stream, ImageFileType fileType)
		{
			var bmp = GetBitmap (backend) ?? throw new InvalidOperationException ("Empty image");
			var fmt = fileType switch {
				ImageFileType.Png => SKEncodedImageFormat.Png,
				ImageFileType.Jpeg => SKEncodedImageFormat.Jpeg,
				ImageFileType.Bmp => SKEncodedImageFormat.Bmp,
				_ => SKEncodedImageFormat.Png,
			};
			using var image = SKImage.FromBitmap (bmp);
			using var data = image.Encode (fmt, 90);
			data.SaveTo (stream);
		}

		public override Image GetStockIcon (string id) => null; // AVALONIA-PORT: wave 4

		public override bool IsBitmap (object handle) => GetBitmap (handle) is not null;

		public override object ConvertToBitmap (ImageDescription idesc, double scaleFactor, ImageFormat format)
		{
			// Custom-drawn images (VectorImage/DrawingImage, e.g. everything an
			// ImageBuilder records) rasterize by REPLAYING the draw callback at
			// the requested size (idesc.Size — the backend itself has no size);
			// bitmap images go through the DrawImage path.
			if (idesc.Backend is AvaloniaImageData { CustomDraw: not null } cd) {
				var cdSize = idesc.Size;
				if (cdSize.Width <= 0 || cdSize.Height <= 0)
					cdSize = GetSizeStatic (cd);
				int cw = Math.Max (1, (int)Math.Ceiling (cdSize.Width * scaleFactor));
				int ch = Math.Max (1, (int)Math.Ceiling (cdSize.Height * scaleFactor));
				return new AvaloniaImageData { Bitmap = RenderCustomDrawStatic (cd, cw, ch, scaleFactor) };
			}
			var size = idesc.Size.Width > 0 && idesc.Size.Height > 0 ? idesc.Size : GetSizeStatic (idesc.Backend);
			int w = Math.Max (1, (int)Math.Ceiling (size.Width * scaleFactor));
			int h = Math.Max (1, (int)Math.Ceiling (size.Height * scaleFactor));
			var info = new SKImageInfo (w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
			var bmp = new SKBitmap (info);
			using (var surface = SKSurface.Create (info, bmp.GetPixels (), bmp.RowBytes)) {
				if (surface is not null) {
					surface.Canvas.Clear (SKColors.Transparent);
					var ctx = new SkDrawContext (surface.Canvas);
					ctx.Current.Transform = SKMatrix.CreateScale ((float)scaleFactor, (float)scaleFactor);
					new ContextBackend ().DrawImage (ctx, idesc, 0, 0);
					surface.Canvas.Flush ();
				}
			}
			return new AvaloniaImageData { Bitmap = bmp };
		}

		public override bool HasMultipleSizes (object handle)
			=> handle is AvaloniaImageData d && d.MultiResolution is { Count: > 1 };

		public override Size GetSize (object handle) => GetSizeStatic (handle);

		public override Size GetSize (string file)
		{
			using var s = File.OpenRead (file);
			using var codec = SKCodec.Create (s);
			return codec is null ? Size.Zero : new Size (codec.Info.Width, codec.Info.Height);
		}

		public override object CopyBitmap (object handle)
		{
			var src = GetBitmap (handle) ?? throw new InvalidOperationException ("Not a bitmap");
			var dst = new SKBitmap (src.Info);
			src.CopyTo (dst);
			return new AvaloniaImageData { Bitmap = dst };
		}

		public override void CopyBitmapArea (object srcHandle, int srcX, int srcY, int width, int height, object destHandle, int destX, int destY)
		{
			var src = GetBitmap (srcHandle);
			var dst = GetBitmap (destHandle);
			if (src is null || dst is null)
				return;
			using var surface = SKSurface.Create (new SKImageInfo (dst.Width, dst.Height, SKColorType.Bgra8888, SKAlphaType.Premul), dst.GetPixels (), dst.RowBytes);
			surface.Canvas.DrawBitmap (src,
				new SKRect (srcX, srcY, srcX + width, srcY + height),
				new SKRect (destX, destY, destX + width, destY + height));
			surface.Canvas.Flush ();
		}

		public override object CropBitmap (object handle, int srcX, int srcY, int width, int height)
		{
			var src = GetBitmap (handle) ?? throw new InvalidOperationException ("Not a bitmap");
			var info = new SKImageInfo (width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
			var bmp = new SKBitmap (info);
			using var surface = SKSurface.Create (info, bmp.GetPixels (), bmp.RowBytes);
			if (surface is not null) {
				surface.Canvas.DrawBitmap (src,
					new SKRect (srcX, srcY, srcX + width, srcY + height),
					new SKRect (0, 0, width, height));
				surface.Canvas.Flush ();
			}
			return new AvaloniaImageData { Bitmap = bmp };
		}

		public override void SetBitmapPixel (object handle, int x, int y, Color color)
			=> GetBitmap (handle)?.SetPixel (x, y, SkDrawExtensions.ToSkColor (color));

		public override Color GetBitmapPixel (object handle, int x, int y)
		{
			var c = GetBitmap (handle)?.GetPixel (x, y) ?? new SKColor ();
			return new Color (c.Red / 255d, c.Green / 255d, c.Blue / 255d, c.Alpha / 255d);
		}

		public override object CreateBackend () => new AvaloniaImageData ();

		public override object CreateCustomDrawn (ImageDrawCallback drawCallback)
			=> new AvaloniaImageData { CustomDraw = drawCallback };

		public override object CreateMultiResolutionImage (IEnumerable<object> images)
			=> new AvaloniaImageData { MultiResolution = images.Cast<AvaloniaImageData> ().ToList () };

		public override void Dispose (object backend) => ((AvaloniaImageData)backend).Dispose ();
	}

	static class SkiaExtensions
	{
		/// <summary>Copies a bitmap into a fresh one (SKBitmap.CopyTo with
		/// allocation helper).</summary>
		public static SKBitmap Tap (this SKBitmap dst, Action<SKBitmap> copy)
		{
			copy (dst);
			return dst;
		}
	}
}
