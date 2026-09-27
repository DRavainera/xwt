//
// WidgetBackends.cs — first-wave widget backends (Label, Button, Box,
// TextEntry, Canvas). Each wraps exactly one Avalonia control and routes
// its events to the Xwt frontend through the event sink, mirroring the
// GtkBackend implementations widget by widget.
//
// Avalonia type names that collide with the Xwt frontend are used through
// explicit aliases (the enclosing Xwt.* namespaces shadow them).
//

using System;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Xwt.Backends;
using AButton = Avalonia.Controls.Button;
using ACanvas = Avalonia.Controls.Canvas;
using AControl = Avalonia.Controls.Control;
using AMediaAlignment = Avalonia.Media.TextAlignment;
using AOrientation = Avalonia.Layout.Orientation;
using AStackPanel = Avalonia.Controls.StackPanel;
using ATextBox = Avalonia.Controls.TextBox;
using ATextBlock = Avalonia.Controls.TextBlock;
using ABrush = Avalonia.Media.SolidColorBrush;
using AColors = Avalonia.Media.Colors;

namespace Xwt.AvaloniaBackend
{
	// ----- Label (Xwt.Label ↔ Avalonia TextBlock) -----

	public class LabelBackend : AvaloniaWidgetBackend, ILabelBackend
	{
		ATextBlock label;

		public LabelBackend () => label = new ATextBlock ();

		protected override AControl CreateNativeWidget () => label = new ATextBlock ();

		public string Text {
			get => label.Text ?? "";
			set => label.Text = value;
		}

		public bool Selectable {
			get => false; // TextBlock has no selection; wave 2: SelectableTextBlock
			set { }
		}

		public Xwt.Drawing.Color TextColor {
			get => FromAvalonia ((label.Foreground as ABrush)?.Color ?? AColors.Black);
			set => label.Foreground = new ABrush (ToAvalonia (value));
		}

		public Alignment TextAlignment {
			get => label.TextAlignment switch {
				AMediaAlignment.Center => Alignment.Center,
				AMediaAlignment.Right => Alignment.End,
				_ => Alignment.Start,
			};
			set => label.TextAlignment = value switch {
				Alignment.Center => AMediaAlignment.Center,
				Alignment.End => AMediaAlignment.Right,
				_ => AMediaAlignment.Left,
			};
		}

		public EllipsizeMode Ellipsize {
			get => label.TextTrimming == TextTrimming.CharacterEllipsis ? EllipsizeMode.End : EllipsizeMode.None;
			set => label.TextTrimming = value == EllipsizeMode.None ? TextTrimming.None : TextTrimming.CharacterEllipsis;
		}

		public WrapMode Wrap {
			get => label.TextWrapping == TextWrapping.Wrap ? WrapMode.Word : WrapMode.None;
			set => label.TextWrapping = value == WrapMode.None ? TextWrapping.NoWrap : TextWrapping.Wrap;
		}

		public void SetFormattedText (FormattedText text) { } // AVALONIA-PORT: drawing wave

		public override void Dispose () { }
	}

	// ----- Button (Xwt.Button ↔ Avalonia Button) -----

	public class ButtonBackend : AvaloniaWidgetBackend, IButtonBackend
	{
		AButton button;

		public ButtonBackend () => button = new AButton ();

		protected override AControl CreateNativeWidget () => button = new AButton ();

		protected override void HookNativeEvents ()
		{
			button.Click += (_, _) => {
				if (EventSink is IButtonEventSink sink)
					InvokeUser (sink.OnClicked);
			};
		}

		public Xwt.Drawing.Color LabelColor {
			get => FromAvalonia ((button.Foreground as ABrush)?.Color ?? AColors.Black);
			set => button.Foreground = new ABrush (ToAvalonia (value));
		}

		public bool IsDefault {
			get => button.IsDefault;
			set => button.IsDefault = value;
		}

		public void SetButtonStyle (ButtonStyle style) { } // AVALONIA-PORT: theme mapping

		public void SetButtonType (ButtonType type) { }

		public void SetContent (string label, bool useMnemonic, ImageDescription image, ContentPosition position)
			=> button.Content = label ?? "";

		public void SetFormattedText (FormattedText text) { } // AVALONIA-PORT: drawing wave

		public override void Dispose () { }
	}

	// ----- Box (Xwt.VBox/HBox ↔ Avalonia Canvas as fixed container) -----

	public class BoxBackend : AvaloniaWidgetBackend, IBoxBackend
	{
		ACanvas panel;

		public BoxBackend () { }

		protected override AControl CreateNativeWidget () => panel = new ACanvas ();

		public void Add (IWidgetBackend widget)
			=> panel.Children.Add (widget.Native<AControl> ());

		public void Remove (IWidgetBackend widget)
			=> panel.Children.Remove (widget.Native<AControl> ());

		// The Box FRONTEND owns the packing logic (orientation, spacing,
		// expand/fill) and hands out the final child rectangles here — the
		// same contract the Gtk backend's CustomContainer implements.
		public void SetAllocation (IWidgetBackend[] widgets, Rectangle[] rects)
		{
			for (int i = 0; i < widgets.Length && i < rects.Length; i++) {
				var control = widgets [i].Native<AControl> ();
				ACanvas.SetLeft (control, rects [i].X);
				ACanvas.SetTop (control, rects [i].Y);
				control.Width = rects [i].Width;
				control.Height = rects [i].Height;
			}
		}

		public override void Dispose () { }
	}

	// ----- TextEntry (Xwt.TextEntry ↔ Avalonia TextBox) -----

	public class TextEntryBackend : AvaloniaWidgetBackend, ITextEntryBackend
	{
		ATextBox entry;

		public TextEntryBackend () => entry = new ATextBox ();

		protected override AControl CreateNativeWidget () => entry = new ATextBox ();

		protected override void HookNativeEvents ()
		{
			entry.TextChanged += (_, _) => {
				if (EventSink is ITextEntryEventSink sink)
					InvokeUser (sink.OnChanged);
			};
			entry.KeyDown += (_, e) => {
				if (e.Key == Avalonia.Input.Key.Enter && EventSink is ITextEntryEventSink sink)
					InvokeUser (sink.OnActivated);
			};
		}

		public string Text {
			get => entry.Text ?? "";
			set => entry.Text = value;
		}

		public Alignment TextAlignment {
			get => entry.TextAlignment switch {
				AMediaAlignment.Center => Alignment.Center,
				AMediaAlignment.Right => Alignment.End,
				_ => Alignment.Start,
			};
			set => entry.TextAlignment = value switch {
				Alignment.Center => AMediaAlignment.Center,
				Alignment.End => AMediaAlignment.Right,
				_ => AMediaAlignment.Left,
			};
		}

		public string PlaceholderText {
			get => entry.Watermark ?? "";
			set => entry.Watermark = value;
		}

		public bool ReadOnly {
			get => entry.IsReadOnly;
			set => entry.IsReadOnly = value;
		}

		public bool ShowFrame {
			get => true;
			set { }
		}

		public bool MultiLine {
			get => entry.AcceptsReturn;
			set => entry.AcceptsReturn = value;
		}

		public int CursorPosition {
			get => entry.CaretIndex;
			set => entry.CaretIndex = value;
		}

		public int SelectionStart {
			get => entry.SelectionStart;
			set => entry.SelectionStart = value;
		}

		public int SelectionLength {
			get => entry.SelectionEnd - entry.SelectionStart;
			set => entry.SelectionEnd = entry.SelectionStart + value;
		}

		public string SelectedText {
			get => entry.SelectedText;
			set { }
		}

		public bool HasCompletions => false; // AVALONIA-PORT: completion wave

		public void SetCompletions (string[] completions) { }

		public void SetCompletionMatchFunc (Func<string, string, bool> matchFunc) { }

		public override void Dispose () { }
	}

	// ----- Canvas (Xwt.Canvas ↔ render host + child overlay) -----

	/// <summary>
	/// Canvas backend: renders the frontend's OnDraw into an offscreen Skia
	/// surface each frame (the same WriteableBitmap + SKSurface pattern the
	/// MonoDevelop SkTextEditor proves in production), then presents it via
	/// DrawImage from a Render override. The Xwt Context the frontend receives
	/// IS the SkDrawContext created for that surface, so the wave-1 drawing
	/// handlers apply. Child widgets live on an overlay Canvas above the
	/// render host.
	/// </summary>
	public class CanvasBackend : AvaloniaWidgetBackend, ICanvasBackend
	{
		/// <summary>The drawing surface host: a plain Control with a Render
		/// override — Avalonia 12 seals Panel.Render but NOT Control.Render
		/// (the same pattern SkTextEditor uses in the shell).</summary>
		class CanvasRenderHost : Avalonia.Controls.Control
		{
			public Action<Avalonia.Media.DrawingContext> OnFrame;

			public override void Render (Avalonia.Media.DrawingContext context)
				=> OnFrame?.Invoke (context);
		}

		Avalonia.Controls.Grid grid;
		CanvasRenderHost host;
		ACanvas overlay;
		WriteableBitmap front;
		int bufferW, bufferH;
		WriteableBitmap pendingDispose;

		public CanvasBackend () { }

		protected override AControl CreateNativeWidget ()
		{
			host = new CanvasRenderHost { OnFrame = RenderFrame };
			overlay = new ACanvas ();
			grid = new Avalonia.Controls.Grid ();
			grid.Children.Add (host);
			grid.Children.Add (overlay);
			return grid;
		}

		public void QueueDraw () => host.InvalidateVisual ();

		public void QueueDraw (Rectangle rect) => host.InvalidateVisual ();

		public void AddChild (IWidgetBackend widget, Rectangle bounds)
		{
			var control = widget.Native<AControl> ();
			ACanvas.SetLeft (control, bounds.X);
			ACanvas.SetTop (control, bounds.Y);
			overlay.Children.Add (control);
		}

		public void SetChildBounds (IWidgetBackend widget, Rectangle bounds)
		{
			var control = widget.Native<AControl> ();
			ACanvas.SetLeft (control, bounds.X);
			ACanvas.SetTop (control, bounds.Y);
			control.Width = bounds.Width;
			control.Height = bounds.Height;
		}

		public void RemoveChild (IWidgetBackend widget)
			=> overlay.Children.Remove (widget.Native<AControl> ());

		void RenderFrame (Avalonia.Media.DrawingContext context)
		{
			int w = Math.Max (1, (int)Math.Ceiling (host.Bounds.Width));
			int h = Math.Max (1, (int)Math.Ceiling (host.Bounds.Height));
			if (front is null || bufferW != w || bufferH != h) {
				var next = new WriteableBitmap (new Avalonia.PixelSize (w, h), new Avalonia.Vector (96, 96), PixelFormats.Bgra8888, AlphaFormat.Opaque);
				using (var fb = next.Lock ()) {
					var info = new SkiaSharp.SKImageInfo (w, h, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Opaque);
					using var surface = SkiaSharp.SKSurface.Create (info, fb.Address, fb.RowBytes);
					if (surface is not null) {
						var ctx = new SkDrawContext (surface.Canvas);
						try {
							InvokeUser (() => (EventSink as ICanvasEventSink)?.OnDraw (ctx, new Rectangle (0, 0, w, h)));
						} finally {
							surface.Canvas.Flush ();
						}
					}
				}
				var old = front;
				front = next;
				bufferW = w;
				bufferH = h;
				// The just-replaced frame may still be in flight at the
				// compositor: release it one render later (SkTextEditor's
				// ghost-frame fix).
				pendingDispose?.Dispose ();
				pendingDispose = old;
			}
			if (front is not null)
				context.DrawImage (front, new Avalonia.Rect (0, 0, host.Bounds.Width, host.Bounds.Height));
		}

		public override void Dispose ()
		{
			front?.Dispose ();
			front = null;
			if (host is not null)
				host.OnFrame = null;
		}
	}
}
