//
// Widgets2Backends.cs — second-wave widget backends: ScrollView,
// CheckBox/RadioButton/ToggleButton, Frame, Separator and ImageView.
//
// Each wraps exactly one Avalonia control (the one-to-one mapping the rest
// of the backend uses); events route through the Xwt event sink inside
// InvokeUserCode. Avalonia type names that collide with the Xwt frontend
// are aliased.
//
// Avalonia type names that collide with the Xwt frontend are used through
// explicit aliases (the enclosing Xwt.* namespaces shadow them).
//

using System;
using System.Collections.Generic;
using Avalonia.Layout;
using Avalonia.Media;
using SkiaSharp;
using Xwt.Backends;
using ABorder = Avalonia.Controls.Border;
using ACheckBox = Avalonia.Controls.CheckBox;
using AControl = Avalonia.Controls.Control;
using AImage = Avalonia.Controls.Image;
using ARadioButton = Avalonia.Controls.RadioButton;
using AToggleButton = Avalonia.Controls.Primitives.ToggleButton;
using AScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility;
using AScrollViewer = Avalonia.Controls.ScrollViewer;
using ASeparator = Avalonia.Controls.Separator;
using AStackPanel = Avalonia.Controls.StackPanel;
using ATextBlock = Avalonia.Controls.TextBlock;

namespace Xwt.AvaloniaBackend
{
	// ----- ScrollView (Xwt.ScrollView ↔ Avalonia ScrollViewer) -----

	public class ScrollViewBackend : AvaloniaWidgetBackend, IScrollViewBackend
	{
		AScrollViewer viewer;
		AControl content;

		public ScrollViewBackend () { }

		protected override AControl CreateNativeWidget () => viewer = new AScrollViewer ();

		public void SetChild (IWidgetBackend childBackend)
		{
			// Remove the previous child first (SetChild can be called again
			// after a change, like the WPF backend does).
			if (content is not null)
				viewer.Content = null;
			content = childBackend is null ? null : childBackend.Native<AControl> ();
			viewer.Content = content;
		}

		public bool BorderVisible {
			get => viewer.BorderThickness != default;
			set => viewer.BorderThickness = value ? new Avalonia.Thickness (1) : default;
		}

		public Rectangle VisibleRect => new Rectangle (viewer.Offset.X, viewer.Offset.Y, viewer.Viewport.Width, viewer.Viewport.Height);

		public void SetChildSize (Size size) { } // the Avalonia layout measures the child itself

		public void UpdateChildPlacement (IWidgetBackend childBackend) { }

		public ScrollPolicy VerticalScrollPolicy {
			get => FromAvalonia (viewer.VerticalScrollBarVisibility);
			set => viewer.VerticalScrollBarVisibility = ToAvalonia (value);
		}

		public ScrollPolicy HorizontalScrollPolicy {
			get => FromAvalonia (viewer.HorizontalScrollBarVisibility);
			set => viewer.HorizontalScrollBarVisibility = ToAvalonia (value);
		}

		public IScrollControlBackend CreateVerticalScrollControl () => new ScrollControlBackend (viewer, vertical: true);

		public IScrollControlBackend CreateHorizontalScrollControl () => new ScrollControlBackend (viewer, vertical: false);

		/// <summary>The scroll-control backend of the wave-3 lists, reused:
		/// reads/writes the offsets of the wrapped ScrollViewer.</summary>
		protected class ScrollControlBackend : IScrollControlBackend
		{
			readonly AScrollViewer viewer;
			readonly bool vertical;
			IScrollControlEventSink sink;

			public ScrollControlBackend (AScrollViewer viewer, bool vertical)
			{
				this.viewer = viewer;
				this.vertical = vertical;
				viewer.ScrollChanged += (s, e) => sink?.OnValueChanged ();
			}

			public void Initialize (IScrollControlEventSink eventSink) => sink = eventSink;

			double Extent => vertical ? viewer.ScrollBarMaximum.Y : viewer.ScrollBarMaximum.X;

			double Offset {
				get => vertical ? viewer.Offset.Y : viewer.Offset.X;
				set => viewer.Offset = vertical ? new Avalonia.Vector (viewer.Offset.X, value) : new Avalonia.Vector (value, viewer.Offset.Y);
			}

			public double Value {
				get => Offset;
				set => Offset = Math.Min (value, Math.Max (0, Extent));
			}

			public double LowerValue => 0;

			public double UpperValue => Extent;

			public double PageSize => vertical ? viewer.Viewport.Height : viewer.Viewport.Width;

			public double StepIncrement => 16;

			public double PageIncrement => PageSize;

			public void InitializeBackend (object frontend, ApplicationContext context) { }

			public void EnableEvent (object eventId) { }

			public void DisableEvent (object eventId) { }
		}

		static ScrollPolicy FromAvalonia (AScrollBarVisibility v) => v switch {
			AScrollBarVisibility.Visible => ScrollPolicy.Always,
			AScrollBarVisibility.Hidden => ScrollPolicy.Never,
			_ => ScrollPolicy.Automatic,
		};

		static AScrollBarVisibility ToAvalonia (ScrollPolicy p) => p switch {
			ScrollPolicy.Always => AScrollBarVisibility.Visible,
			ScrollPolicy.Never => AScrollBarVisibility.Hidden,
			_ => AScrollBarVisibility.Auto,
		};

		public override void Dispose () { }
	}

	// ----- CheckBox / RadioButton / ToggleButton -----

	public class CheckBoxBackend : AvaloniaWidgetBackend, ICheckBoxBackend
	{
		ACheckBox checkbox;
		bool allowMixed;
		// Suppress the recursion from the State setter when the toggled
		// event re-broadcasts the new state back to the frontend.
		bool syncing;

		public CheckBoxBackend () { }

		protected override AControl CreateNativeWidget () => checkbox = new ACheckBox ();

		protected override void HookNativeEvents ()
		{
			checkbox.Click += (_, _) => {
				if (EventSink is ICheckBoxEventSink sink)
					InvokeUser (sink.OnClicked);
			};
			checkbox.IsCheckedChanged += (_, _) => {
				if (syncing || EventSink is not ICheckBoxEventSink sink)
					return;
				InvokeUser (sink.OnToggled);
			};
		}

		public void SetContent (IWidgetBackend widget)
			=> checkbox.Content = widget is null ? null : widget.Native<AControl> ();

		public void SetContent (string label, bool useMnemonic) => checkbox.Content = label ?? "";

		public CheckBoxState State {
			get => checkbox.IsChecked is null ? CheckBoxState.Mixed : (checkbox.IsChecked == true ? CheckBoxState.On : CheckBoxState.Off);
			set {
				syncing = true;
				try {
					checkbox.IsChecked = value switch {
						CheckBoxState.On => true,
						CheckBoxState.Off => false,
						_ => null,
					};
				} finally {
					syncing = false;
				}
			}
		}

		public bool AllowMixed {
			get => allowMixed;
			set {
				allowMixed = value;
				if (!value && checkbox.IsChecked is null)
					checkbox.IsChecked = false;
			}
		}

		public override void Dispose () { }
	}

	public class RadioButtonBackend : AvaloniaWidgetBackend, IRadioButtonBackend
	{
		// The group is managed HERE (like Xwt.WPF does): detached controls do
		// not share a native namescope, so a GroupName alone would not uncheck
		// peers. The registry maps the frontend group object to its radios.
		static readonly Dictionary<object, List<RadioButtonBackend>> groups = new ();

		ARadioButton radio;
		object group;

		public RadioButtonBackend () { }

		protected override AControl CreateNativeWidget () => radio = new ARadioButton ();

		protected override void HookNativeEvents ()
		{
			radio.Click += (_, _) => {
				if (EventSink is IRadioButtonEventSink sink)
					InvokeUser (sink.OnClicked);
			};
			radio.IsCheckedChanged += (_, _) => {
				if (EventSink is IRadioButtonEventSink sink)
					InvokeUser (sink.OnToggled);
			};
		}

		public void SetContent (IWidgetBackend widget)
			=> radio.Content = widget is null ? null : widget.Native<AControl> ();

		public void SetContent (string label) => radio.Content = label ?? "";

		public object Group {
			// Never null: the frontend stores the getter result as its
			// GroupBackend and feeds it back on later assignments (a null here
			// would collapse all groups into one indistinguishable "null").
			// The getter also REGISTERS this radio: the frontend's first
			// Group assignment is a self-assign (radioGroup.GroupBackend =
			// Backend.Group) that never reaches the setter.
			get {
				if (group is null) {
					group = new ();
					if (!groups.TryGetValue (group, out var joined))
						groups [group] = joined = new ();
					joined.Add (this);
				}
				return group;
			}
			set {
				if (ReferenceEquals (group, value))
					return;
				if (group is not null && groups.TryGetValue (group, out var current)) {
					current.Remove (this);
					if (current.Count == 0)
						groups.Remove (group);
				}
				group = value;
				if (group is not null) {
					if (!groups.TryGetValue (group, out var joined))
						groups [group] = joined = new ();
					joined.Add (this);
				}
			}
		}

		public bool Active {
			get => radio.IsChecked == true;
			set {
				if (value && group is not null && groups.TryGetValue (group, out var peers)) {
					foreach (var peer in peers.ToArray ()) {
						if (!ReferenceEquals (peer, this))
							peer.radio.IsChecked = false;
					}
				}
				radio.IsChecked = value;
			}
		}

		public override void Dispose ()
		{
			// Leave the group registry on dispose (the frontend may recreate).
			if (group is not null && groups.TryGetValue (group, out var current)) {
				current.Remove (this);
				if (current.Count == 0)
					groups.Remove (group);
			}
		}
	}

	public class ToggleButtonBackend : ButtonBackend, IToggleButtonBackend
	{
		AToggleButton toggle;

		public ToggleButtonBackend () { }

		protected override AControl CreateNativeWidget ()
		{
			toggle = new AToggleButton ();
			return toggle;
		}

		protected override void HookNativeEvents ()
		{
			toggle.Click += (_, _) => {
				if (EventSink is IButtonEventSink sink)
					InvokeUser (sink.OnClicked);
			};
			toggle.IsCheckedChanged += (_, _) => {
				if (EventSink is IToggleButtonEventSink sink)
					InvokeUser (sink.OnToggled);
			};
		}

		public bool Active {
			get => toggle.IsChecked == true;
			set => toggle.IsChecked = value;
		}

		public void SetButtonStyle (ButtonStyle style) { }

		public void SetButtonType (ButtonType type) { }

		public void SetContent (string label, bool useMnemonic, ImageDescription image, ContentPosition position)
			=> toggle.Content = label ?? "";

		public void SetFormattedText (FormattedText text) { }

		public Xwt.Drawing.Color LabelColor {
			get => default;
			set { }
		}

		public bool IsDefault {
			get => false;
			set { }
		}

		public override void Dispose () { }
	}

	// ----- Frame / Separator -----

	public class FrameBackend : AvaloniaWidgetBackend, IFrameBackend
	{
		ABorder border;
		ATextBlock? labelBlock;
		AStackPanel? root;

		public FrameBackend () { }

		protected override AControl CreateNativeWidget ()
		{
			root = new AStackPanel ();
			labelBlock = new ATextBlock { Margin = new Avalonia.Thickness (4, 0) };
			border = new ABorder {
				BorderBrush = new SolidColorBrush (Avalonia.Media.Color.FromRgb (0xd0, 0xd0, 0xd0)),
				BorderThickness = new Avalonia.Thickness (1),
				Child = null,
			};
			root.Children.Add (labelBlock);
			root.Children.Add (border);
			return root;
		}

		public string Label {
			get => labelBlock?.Text ?? "";
			set {
				if (labelBlock is not null) {
					labelBlock.Text = value ?? "";
					labelBlock.IsVisible = !string.IsNullOrEmpty (value);
				}
			}
		}

		public Xwt.Drawing.Color BorderColor {
			get => (border.BorderBrush is SolidColorBrush b)
				? new Xwt.Drawing.Color (b.Color.R / 255d, b.Color.G / 255d, b.Color.B / 255d, b.Color.A / 255d)
				: default;
			set => border.BorderBrush = new SolidColorBrush (Avalonia.Media.Color.FromArgb (
				(byte)Math.Round (value.Alpha * 255), (byte)Math.Round (value.Red * 255),
				(byte)Math.Round (value.Green * 255), (byte)Math.Round (value.Blue * 255)));
		}

		public void SetFrameType (FrameType type) { } // widget etching is a theme concern; the border draws flat

		public void SetContent (IWidgetBackend child)
			=> border.Child = child is null ? null : child.Native<AControl> ();

		public void SetBorderSize (double left, double right, double top, double bottom)
			=> border.Padding = new Avalonia.Thickness (left, top, right, bottom);

		public void SetPadding (double left, double right, double top, double bottom)
			=> root?.Margin = new Avalonia.Thickness (left, top, right, bottom);

		public void UpdateChildPlacement (IWidgetBackend childBackend) { }

		public override void Dispose () { }
	}

	public class SeparatorBackend : AvaloniaWidgetBackend, ISeparatorBackend
	{
		ASeparator separator;

		public SeparatorBackend () { }

		protected override AControl CreateNativeWidget () => separator = new ASeparator ();

		public void Initialize (Xwt.Backends.Orientation dir)
		{
			// Initialize() can run BEFORE CreateNativeWidget (the frontend
			// calls it inside OnCreateBackend) — materialize on demand.
			separator ??= (ASeparator)CreateNativeWidget ();
			// Avalonia's Separator draws a themed line; direction is a layout
			// concern: a vertical separator is a 1px-wide vertical line.
			if (dir == Xwt.Backends.Orientation.Vertical) {
				separator.Width = 1;
				separator.Height = double.NaN;
			} else {
				separator.Height = 1;
				separator.Width = double.NaN;
			}
		}

		public override void Dispose () { }
	}

	// ----- ImageView (Xwt.ImageView ↔ Avalonia Image) -----

	public class ImageViewBackend : AvaloniaWidgetBackend, IImageViewBackend
	{
		AImage image;

		public ImageViewBackend () { }

		protected override AControl CreateNativeWidget ()
			=> image = new AImage { Stretch = Stretch.None };

		public void SetImage (ImageDescription image)
		{
			// Rasterize through the wave-1 image backend (vector/custom-drawn
			// images replay at their declared size) and hand the pixels to the
			// native Image control. ImageDescription is a struct (no null);
			// an empty backend clears the view.
			if (image.Backend is null) {
				this.image.Source = null;
				return;
			}
			var bmp = AvaloniaImageBackend.GetBitmap (image.Backend);
			if (bmp is null) {
				// Vector/custom-drawn image: rasterize through our own image
				// handler (stateless — the wave-1 replay path).
				var converted = new AvaloniaImageBackend ().ConvertToBitmap (
					image, 1d, Xwt.Drawing.ImageFormat.ARGB32);
				bmp = AvaloniaImageBackend.GetBitmap (converted);
			}
			this.image.Source = bmp?.ToAvaloniaBitmap ();
		}

		public override void Dispose () { }
	}
}
