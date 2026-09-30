//
// ListBackends.cs — third-wave widget backends: ListView/ListBox/TreeView
// (the MonoDevelop Pads) and ComboBox.
//
// The rows render through plain Avalonia controls (a StackPanel of per-row
// Grids inside a ScrollViewer) instead of ItemsControl data templates: the
// Xwt contract needs TreePosition-keyed APIs (SelectRow, ExpandRow,
// GetCellBounds…) against a data source the backend does not own, and the
// Avalonia 12 tree-template API (ITreeDataTemplate.BindChildren) drops the
// per-node identity we must keep. Cell CONTENT maps to the standard native
// controls the way Xwt.WPF's CellUtil does (TextCellView→TextBlock,
// ImageCellView→Image, CheckBoxCellView→CheckBox).
//
// TreePosition: rows are addressed by their OWN TreePosition handles (the
// DefaultListStoreBackend's rows and the DefaultTreeStoreBackend's nodes
// implement TreePosition directly), exactly like the Gtk backend addresses
// GtkTreeIter pointers. The store is the source of truth; the backend only
// reads it (GetChildrenCount/GetValue/…) to rebuild its visual rows.
//
// Avalonia type names that collide with the Xwt frontend are used through
// explicit aliases (the enclosing Xwt.* namespaces shadow them).
//

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Layout;
using Avalonia;
using Avalonia.Media;
using SkiaSharp;
using Xwt.Backends;
using ACheckBox = Avalonia.Controls.CheckBox;
using AGridUnitType = Avalonia.Controls.GridUnitType;
using AColumnDefinition = Avalonia.Controls.ColumnDefinition;
using AControl = Avalonia.Controls.Control;
using AGrid = Avalonia.Controls.Grid;
using AGridLength = Avalonia.Controls.GridLength;
using AImage = Avalonia.Controls.Image;
using AScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility;
using AComboBox = Avalonia.Controls.ComboBox;
using AScrollViewer = Avalonia.Controls.ScrollViewer;
using ASelectingItemsControl = Avalonia.Controls.Primitives.SelectingItemsControl;
using AStackPanel = Avalonia.Controls.StackPanel;
using ATextBlock = Avalonia.Controls.TextBlock;

namespace Xwt.AvaloniaBackend
{
	// ----- Cell renderers (the Xwt.WPF CellUtil mapping) -----

	static class CellRenderer
	{
		/// <summary>Creates the visual for one cell view (static values only;
		/// bound values are re-read by CellRenderer.Refresh on every rebuild).</summary>
		public static AControl Create (CellView view)
		{
			switch (view) {
			case TextCellView text:
				var tb = new ATextBlock { VerticalAlignment = VerticalAlignment.Center };
				ApplyText (tb, text);
				return tb;
			case ImageCellView image:
				var img = new AImage { Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Center };
				ApplyImage (img, image);
				return img;
			case CheckBoxCellView checkbox:
				var cb = new ACheckBox { IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
				ApplyCheckBox (cb, checkbox);
				return cb;
			default:
				// AVALONIA-PORT: CanvasCellView (custom cell drawing), RadioButtonCellView
				// and ComboBoxCellView land with the custom-drawn cell wave.
				return new ATextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center };
			}
		}

		/// <summary>Re-reads the bound values of one cell from the data
		/// source (the visual and the view are aligned by index).</summary>
		public static void Refresh (AControl control, CellView view, Func<IDataField, object> readValue)
		{
			if (control == null)
				return;
			switch (view) {
			case TextCellView text:
				var tb = (ATextBlock)control;
				if (text.TextField != null)
					tb.Text = Convert.ToString (readValue (text.TextField)) ?? "";
				else if (text.MarkupField != null)
					tb.Text = Convert.ToString (readValue (text.MarkupField)) ?? "";
				else
					ApplyText (tb, text);
				return;
			case ImageCellView image:
				if (image.ImageField != null)
					((AImage)control).Source = ImageToSource (readValue (image.ImageField) as Xwt.Drawing.Image);
				return;
			case CheckBoxCellView checkbox:
				var cb = (ACheckBox)control;
				if (checkbox.ActiveField != null && readValue (checkbox.ActiveField) is bool b)
					cb.IsChecked = b;								else if (checkbox.StateField != null && readValue (checkbox.StateField) is CheckBoxState st)
									cb.IsChecked = st == CheckBoxState.On ? true : st == CheckBoxState.Off ? false : (bool?)null;
				return;
			}
		}

		public static void ApplyText (ATextBlock tb, TextCellView text)
			=> tb.Text = text.Markup ?? text.Text ?? "";

		public static void ApplyImage (AImage img, ImageCellView image)
			=> img.Source = ImageToSource (image.Image);

		public static void ApplyCheckBox (ACheckBox cb, CheckBoxCellView checkbox)
			=> cb.IsChecked = checkbox.Active;

		/// <summary>Rasterizes a Xwt image with the wave-1 image backend (the
		/// custom-drawn/vector images replay at their declared size).</summary>
		public static Avalonia.Media.IImage ImageToSource (Xwt.Drawing.Image image)
		{
			if (image == null)
				return null;
			try {
				var backend = Toolkit.GetBackend (image);
				SKBitmap bmp = AvaloniaImageBackend.GetBitmap (backend);
				if (bmp == null) {
					// Vector/custom-drawn image: rasterize once at its declared
					// size (the wave-1 ConvertToBitmap replay) and show that.
					using var converted = image.ToBitmap ();
					bmp = AvaloniaImageBackend.GetBitmap (Toolkit.GetBackend (converted));
					return bmp?.ToAvaloniaBitmap ();
				}
				return bmp.ToAvaloniaBitmap ();
			} catch {
				return null;
			}
		}
	}

	/// <summary>SKBitmap → Avalonia bitmap (both Bgra8888 premultiplied;
	/// the rows are copied in case the row pitches differ).</summary>
	public static class SkBitmapExtensions
	{
		public static Avalonia.Media.Imaging.WriteableBitmap ToAvaloniaBitmap (this SKBitmap bmp)
		{
			var wb = new Avalonia.Media.Imaging.WriteableBitmap (
				new Avalonia.PixelSize (bmp.Width, bmp.Height), new Avalonia.Vector (96, 96),
				Avalonia.Platform.PixelFormats.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
			using var fb = wb.Lock ();
			using var surface = SKSurface.Create (
				new SKImageInfo (bmp.Width, bmp.Height, SKColorType.Bgra8888, SKAlphaType.Opaque),
				fb.Address, fb.RowBytes);
			surface?.Canvas.Clear (SKColors.Transparent);
			surface?.Canvas.DrawBitmap (bmp, 0, 0);
			surface?.Canvas.Flush ();
			return wb;
		}
	}

	// ----- The shared row-panel engine -----

	/// <summary>One rendered row: the Grid of cells plus the position it was
	/// built from (re-resolved at every rebuild, so identity stays fresh).</summary>
	public class RowPresenter
	{
		public AGrid Visual = new ();
		public List<(CellView View, AControl Control)> Cells = new ();
		public TreePosition Position;
		public int Depth;
		public bool HasExpander;
		public bool IsExpanded;
	}

	/// <summary>One line of the flatten pass: a position with its tree depth.</summary>
	public struct RowSpec
	{
		public TreePosition Position;
		public int Depth;
		public bool HasExpander;
		public bool IsExpanded;
	}

	/// <summary>Shared engine of the list-family backends: a ScrollViewer →
	/// StackPanel of RowPresenters rebuilt from the data source.</summary>
	public class RowPanelHost
	{
		public readonly AScrollViewer ScrollViewer = new ();
		public readonly AStackPanel Panel = new ();
		public readonly List<RowPresenter> Rows = new ();
		public ListViewColumn[] Columns = Array.Empty<ListViewColumn> ();
		public int CurrentEventRow = -1;
		public ScrollPolicy VerticalPolicy = ScrollPolicy.Automatic;
		public ScrollPolicy HorizontalPolicy = ScrollPolicy.Automatic;

		public RowPanelHost ()
			=> ScrollViewer.Content = Panel;

		public void ApplyScrollPolicy ()
			=> SetScrollPolicy (VerticalPolicy, HorizontalPolicy);

		/// <summary>Rebuilds the whole visual panel from the specs (the Gtk
		/// backend rebuilds the visible tree on every structural change as
		/// well, so a full rebuild matches the platform contract).</summary>
		public void RebuildRows (IEnumerable<RowSpec> specs)
		{
			Rows.Clear ();
			Panel.Children.Clear ();
			foreach (var spec in specs) {
				var row = new RowPresenter { Position = spec.Position, Depth = spec.Depth, HasExpander = spec.HasExpander, IsExpanded = spec.IsExpanded };
				var grid = row.Visual;
				int columnIndex = 0;
				foreach (var col in Columns) {
					grid.ColumnDefinitions.Add (new AColumnDefinition {
						Width = new AGridLength (1, col.Expands ? AGridUnitType.Star : AGridUnitType.Auto)
					});
					foreach (var view in col.Views) {
						var control = CellRenderer.Create (view);
						AGrid.SetColumn (control, columnIndex);
						control.Margin = new Thickness (2);
						grid.Children.Add (control);
						row.Cells.Add ((view, control));
					}
					columnIndex++;
				}
				Panel.Children.Add (grid);
				Rows.Add (row);
			}
		}

		public RowPresenter RowOf (TreePosition pos)
			=> pos == null ? null : Rows.FirstOrDefault (r => ReferenceEquals (r.Position, pos));

		public int IndexOf (TreePosition pos)
		{
			for (int i = 0; i < Rows.Count; i++)
				if (ReferenceEquals (Rows [i].Position, pos))
					return i;
			return -1;
		}

		/// <summary>Cumulative Y offset of a visual row (DesiredSize before
		/// the first layout pass, Bounds once arranged).</summary>
		public double YOf (int index)
		{
			double y = 0;
			for (int i = 0; i < index && i < Rows.Count; i++)
				y += RowHeight (i);
			return y;
		}

		public double RowHeight (int index)
		{
			var v = Rows [index].Visual;
			return v.Bounds.Height > 0 ? v.Bounds.Height : v.DesiredSize.Height;
		}

		public void ScrollTo (int index)
		{
			if (index < 0 || index >= Rows.Count)
				return;
			double y = YOf (index);
			double rowH = RowHeight (index);
			double viewH = ScrollViewer.Viewport.Height;
			if (y < ScrollViewer.Offset.Y)
				ScrollViewer.Offset = new Avalonia.Vector (ScrollViewer.Offset.X, y);
			else if (viewH > 0 && y + rowH > ScrollViewer.Offset.Y + viewH)
				ScrollViewer.Offset = new Avalonia.Vector (ScrollViewer.Offset.X, y + rowH - viewH);
		}

		public void SetScrollPolicy (ScrollPolicy vertical, ScrollPolicy horizontal)
		{
			ScrollViewer.VerticalScrollBarVisibility = vertical switch {
				ScrollPolicy.Always => AScrollBarVisibility.Visible,
				ScrollPolicy.Never => AScrollBarVisibility.Hidden,
				_ => AScrollBarVisibility.Auto,
			};
			ScrollViewer.HorizontalScrollBarVisibility = horizontal switch {
				ScrollPolicy.Always => AScrollBarVisibility.Visible,
				ScrollPolicy.Never => AScrollBarVisibility.Hidden,
				_ => AScrollBarVisibility.Auto,
			};
		}
	}

	/// <summary>Base of the list-family backends: selection state, the
	/// selection-mode/scroll plumbing of ITableViewBackend, and the row
	/// hit-testing both list and tree share.</summary>
	public abstract class TableBackendBase : AvaloniaWidgetBackend, ITableViewBackend
	{
		protected readonly RowPanelHost Host = new ();
		protected readonly List<TreePosition> Selected = new ();
		protected SelectionMode selectionMode = SelectionMode.Single;
		protected bool useAlternatingRowColors;

		protected ITableViewEventSink TableSink => EventSink as ITableViewEventSink;

		public abstract object AddColumn (ListViewColumn col);

		public abstract void RemoveColumn (ListViewColumn col, object handle);

		public abstract void UpdateColumn (ListViewColumn col, object handle, ListViewColumnChange change);

		public virtual void SetSelectionMode (SelectionMode mode) => selectionMode = mode;

		public virtual void SelectAll () { } // AVALONIA-PORT: rubber-band selection

		public virtual void UnselectAll ()
		{
			Selected.Clear ();
			ApplySelectionVisuals ();
		}

		public ScrollPolicy VerticalScrollPolicy {
			get => Host.VerticalPolicy;
			set {
				Host.VerticalPolicy = value;
				Host.ApplyScrollPolicy ();
			}
		}

		public ScrollPolicy HorizontalScrollPolicy {
			get => Host.HorizontalPolicy;
			set {
				Host.HorizontalPolicy = value;
				Host.ApplyScrollPolicy ();
			}
		}

		public IScrollControlBackend CreateVerticalScrollControl () => new ScrollControlBackend (Host.ScrollViewer, vertical: true);

		public IScrollControlBackend CreateHorizontalScrollControl () => new ScrollControlBackend (Host.ScrollViewer, vertical: false);

		/// <summary>Row background alternation + selection highlight.</summary>
		protected virtual void ApplySelectionVisuals ()
		{
			for (int i = 0; i < Host.Rows.Count; i++) {
				var row = Host.Rows [i];
				bool selected = Selected.Any (p => ReferenceEquals (row.Position, p));
				row.Visual.Background = selected
					? new SolidColorBrush (Avalonia.Media.Color.FromRgb (0x35, 0x63, 0xa3))
					: useAlternatingRowColors && i % 2 == 1
						? new SolidColorBrush (Avalonia.Media.Color.FromRgb (0xf4, 0xf4, 0xf4))
						: null;
			}
		}

		/// <summary>The shared scroll-control backend: reads and writes the
		/// offsets of the host ScrollViewer (the platform-agnostic default
		/// Xwt scroll adjustment drives it through the event sink).</summary>
		protected class ScrollControlBackend : IScrollControlBackend
		{
			readonly AScrollViewer viewer;
			readonly bool vertical;
			IScrollControlEventSink sink;

			public ScrollControlBackend (AScrollViewer viewer, bool vertical)
			{
				this.viewer = viewer;
				this.vertical = vertical;
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
	}

	// ----- ListBox / ListView (Xwt.ListView ↔ multi-column row panel) -----

	public class ListViewBackend : TableBackendBase, IListViewBackend, IListBoxBackend
	{
		IBackend sourceBackend;
		protected IListDataSource source;
		protected readonly List<(ListViewColumn Column, object Handle)> columns = new ();

		/// <summary>IListBoxBackend contract: one column of cell views.</summary>
		public void SetViews (CellViewCollection views)
		{
			var col = new ListViewColumn { Title = "" };
			foreach (CellView v in views)
				col.Views.Add (v);
			AddColumn (col);
		}

		public ListViewBackend () { }

		protected override AControl CreateNativeWidget ()
		{
			Host.ApplyScrollPolicy ();
			Host.Panel.PointerPressed += HandleRowClick;
			Host.Panel.DoubleTapped += (s, e) => {
				var p = e.GetPosition (Host.Panel);
				int row = RowAtPosition (p.X, p.Y);
				Host.CurrentEventRow = row;
				int captured = row;
				if (row >= 0 && EventSink is IListViewEventSink sink)
					InvokeUser (() => sink.OnRowActivated (captured));
			};
			return Host.ScrollViewer;
		}

		protected int RowAtPosition (double x, double y)
		{
			double acc = 0;
			for (int i = 0; i < Host.Rows.Count; i++) {
				double h = Host.RowHeight (i);
				if (y >= acc && y < acc + h)
					return i;
				acc += h;
			}
			return -1;
		}

		/// <summary>Y offset of a row inside the widget (top coordinate of the
		/// row), for event args raised from row indices.</summary>
		protected double RowOffset (int index)
		{
			double acc = 0;
			for (int i = 0; i < index && i < Host.Rows.Count; i++)
				acc += Host.RowHeight (i);
			return acc;
		}

		void HandleRowClick (object sender, Avalonia.Input.PointerPressedEventArgs e)
		{
			var p = e.GetPosition (Host.Panel);
			int row = RowAtPosition (p.X, p.Y);
			Host.CurrentEventRow = row;
			if (row < 0)
				return;
			var pos = Host.Rows [row].Position;
			if (pos == null)
				return;
			if (selectionMode != SelectionMode.Multiple || !e.KeyModifiers.HasFlag (Avalonia.Input.KeyModifiers.Control))
				Selected.Clear ();
			if (Selected.Any (s => ReferenceEquals (s, pos)))
				Selected.Remove (pos);
			else
				Selected.Add (pos);
			ApplySelectionVisuals ();
			if (TableSink != null)
				InvokeUser (TableSink.OnSelectionChanged);
		}

		public void SetSource (IListDataSource dataSource, IBackend sourceBackend)
		{
			Unsubscribe ();
			source = dataSource;
			this.sourceBackend = sourceBackend;
			Subscribe ();
			Rebuild ();
		}

		void Subscribe ()
		{
			if (source == null)
				return;
			source.RowInserted += OnSourceChanged;
			source.RowDeleted += OnSourceChanged;
			source.RowChanged += OnSourceChanged;
			source.RowsReordered += OnSourceChanged;
		}

		void Unsubscribe ()
		{
			if (source == null)
				return;
			source.RowInserted -= OnSourceChanged;
			source.RowDeleted -= OnSourceChanged;
			source.RowChanged -= OnSourceChanged;
			source.RowsReordered -= OnSourceChanged;
		}

		void OnSourceChanged (object sender, EventArgs e) => Rebuild ();

		protected void Rebuild ()
		{
			if (source == null || columns.Count == 0)
				return;
			Host.Columns = columns.Select (c => c.Column).ToArray ();
			Host.RebuildRows (FlattenRows ());
			RefreshCells ();
			ApplySelectionVisuals ();
		}

		IEnumerable<RowSpec> FlattenRows ()
		{
			// Prefer the store's OWN row handles (ListStoreBackend.HandleOf:
			// identity survives reorders/removals); fall back to index handles
			// for custom IListDataSource sources (MonoDevelop pad view models).
			for (int i = 0; i < source.RowCount; i++)
				yield return new RowSpec {
					Position = sourceBackend is ListStoreBackend store ? store.HandleOf (i) : (TreePosition)new IndexRowHandle (i),
				};
		}

		/// <summary>Reads a bound field for a visual row: store handles map
		/// back through the store, index handles carry the row index.</summary>
		protected object ReadListValue (TreePosition pos, IDataField field)
		{
			if (source == null || field == null)
				return null;
			int row = pos is IndexRowHandle ih ? ih.Index
				: sourceBackend is ListStoreBackend store ? store.IndexOf (pos)
				: RowIndexOf (pos);
			if (row < 0 || row >= source.RowCount)
				return null;
			return source.GetValue (row, field.Index);
		}

		/// <summary>Store-provided rows address the source directly; the
		/// fallback wrapper only knows its index.</summary>
		protected virtual int RowIndexOf (TreePosition pos) => -1;

		void RefreshCells ()
		{
			foreach (var row in Host.Rows) {
				int cellIndex = 0;
				foreach (var col in columns) {
					foreach (var view in col.Column.Views) {
						if (cellIndex < row.Cells.Count && ReferenceEquals (row.Cells [cellIndex].View, view)) {
							var v = view;
							var rowPos = row.Position;
							CellRenderer.Refresh (row.Cells [cellIndex].Control, v, f => ReadListValue (rowPos, f));
						}
						cellIndex++;
					}
				}
			}
		}

		public override object AddColumn (ListViewColumn col)
		{
			columns.Add ((col, col));
			Rebuild ();
			return col;
		}

		public override void RemoveColumn (ListViewColumn col, object handle)
		{
			columns.RemoveAll (c => ReferenceEquals (c.Column, col));
			Rebuild ();
		}

		public override void UpdateColumn (ListViewColumn col, object handle, ListViewColumnChange change)
			=> Rebuild ();

		public int[] SelectedRows => Selected.Select (IndexOfPosition).Where (i => i >= 0).ToArray ();

		/// <summary>Row index of a position: store handles map back through
		/// the store, index handles carry it directly.</summary>
		protected virtual int IndexOfPosition (TreePosition pos)
			=> pos is IndexRowHandle ih ? ih.Index
				: sourceBackend is ListStoreBackend store ? store.IndexOf (pos)
				: Host.IndexOf (pos);

		public int FocusedRow { get => -1; set { } }

		public void SelectRow (int row)
		{
			if (row < 0 || row >= Host.Rows.Count)
				return;
			var pos = Host.Rows [row].Position;
			if (!Selected.Any (s => ReferenceEquals (s, pos)))
				Selected.Add (pos);
			ApplySelectionVisuals ();
		}

		public void UnselectRow (int row)
		{
			if (row < 0 || row >= Host.Rows.Count)
				return;
			var pos = Host.Rows [row].Position;
			Selected.RemoveAll (s => ReferenceEquals (s, pos));
			ApplySelectionVisuals ();
		}

		public void ScrollToRow (int row) => Host.ScrollTo (row);

		public void StartEditingCell (int row, CellView cell) { } // AVALONIA-PORT: editable cells

		public bool BorderVisible { get; set; } = true;

		public bool GridLinesVisible {
			get => false;
			set { } // AVALONIA-PORT: row separators (IListBoxBackend: bool)
		}

		// IListViewBackend types GridLinesVisible as the GridLines enum.
		GridLines IListViewBackend.GridLinesVisible {
			get => GridLines.None;
			set { }
		}

		public bool HeadersVisible { get; set; } = true;

		public int GetRowAtPosition (Point p) => RowAtPosition (p.X, p.Y);

		public Rectangle GetCellBounds (int row, CellView cell, bool includeMargin)
		{
			if (row < 0 || row >= Host.Rows.Count)
				return Rectangle.Zero;
			var gridBounds = GetRowBounds (row, includeMargin);
			var presenter = Host.Rows [row];
			for (int i = 0; i < presenter.Cells.Count; i++) {
				if (ReferenceEquals (presenter.Cells [i].View, cell)) {
					var b = presenter.Cells [i].Control.Bounds;
					return new Rectangle (gridBounds.X + b.X, gridBounds.Y + b.Y, b.Width, b.Height);
				}
			}
			return gridBounds;
		}

		public Rectangle GetRowBounds (int row, bool includeMargin)
		{
			if (row < 0 || row >= Host.Rows.Count)
				return Rectangle.Zero;
			return new Rectangle (0, Host.YOf (row), Host.Panel.Bounds.Width, Host.RowHeight (row));
		}

		public int CurrentEventRow => Host.CurrentEventRow;

		public override void Dispose () => Unsubscribe ();
	}

	// ----- TreeView (Xwt.TreeView ↔ flattened tree row panel) -----

	public class TreeViewBackend : TableBackendBase, ITreeViewBackend
	{
		ITreeDataSource treeSource;
		readonly HashSet<TreePosition> expanded = new ();
		readonly HashSet<TreePosition> collapsed = new (); // default-expanded semantics
		readonly List<(ListViewColumn Column, object Handle)> columns = new ();
		TreePosition focused;
		bool headersVisible = true;
		bool borderVisible = true;

		public TreeViewBackend () { }

		protected override AControl CreateNativeWidget ()
		{
			Host.ApplyScrollPolicy ();
			Host.Panel.PointerPressed += HandleRowClick;
			Host.Panel.DoubleTapped += (s, e) => {
				var p = e.GetPosition (Host.Panel);
				int row = RowAtPosition (p.X, p.Y);
				Host.CurrentEventRow = row;
				if (row < 0)
					return;
				var pos = Host.Rows [row].Position;
				if (EventSink is ITreeViewEventSink sink)
					InvokeUser (() => sink.OnRowActivated (pos));
			};
			return Host.ScrollViewer;
		}

		protected int RowAtPosition (double x, double y)
		{
			double acc = 0;
			for (int i = 0; i < Host.Rows.Count; i++) {
				double h = Host.RowHeight (i);
				if (y >= acc && y < acc + h)
					return i;
				acc += h;
			}
			return -1;
		}

		protected ITreeViewEventSink TreeSink => EventSink as ITreeViewEventSink;

		/// <summary>Y offset of a row inside the widget (top coordinate of the
		/// row), for event args raised from row indices.</summary>
		protected double RowOffset (int index)
		{
			double acc = 0;
			for (int i = 0; i < index && i < Host.Rows.Count; i++)
				acc += Host.RowHeight (i);
			return acc;
		}

		void HandleRowClick (object sender, Avalonia.Input.PointerPressedEventArgs e)
		{
			var p = e.GetPosition (Host.Panel);
			int row = RowAtPosition (p.X, p.Y);
			Host.CurrentEventRow = row;
			var props = e.GetCurrentPoint (Host.Panel).Properties;
			// Right click: select the row under the pointer (legacy pads select
			// before showing the context menu) and raise ButtonPressed with the
			// context-menu flag — no selection change beyond that.
			if (props.IsRightButtonPressed) {
				if (row < 0)
					return;
				var posR = Host.Rows [row].Position;
				focused = posR;
				if (!Selected.Any (s => ReferenceEquals (s, posR))) {
					Selected.Clear ();
					Selected.Add (posR);
					ApplySelectionVisuals ();
					if (TableSink != null)
						InvokeUser (TableSink.OnSelectionChanged);
				}
				InvokeUser (() => {
					var args = new ButtonEventArgs {
						Button = PointerButton.Right,
						X = p.X,
						Y = p.Y + RowOffset (row),
						IsContextMenuTrigger = true,
					};
					EventSink.OnButtonPressed (args);
				});
				return;
			}
			if (row < 0)
				return;
			var presenter = Host.Rows [row];
			var pos = presenter.Position;
			focused = pos;
			if (selectionMode != SelectionMode.Multiple || !e.KeyModifiers.HasFlag (Avalonia.Input.KeyModifiers.Control))
				Selected.Clear ();
			if (Selected.Any (s => ReferenceEquals (s, pos)))
				Selected.Remove (pos);
			else
				Selected.Add (pos);
			ApplySelectionVisuals ();
			if (TableSink != null)
				InvokeUser (TableSink.OnSelectionChanged);
			// The expander gutter toggles expansion (Xwt tree behaviour).
			if (presenter.HasExpander && p.X < 18 + presenter.Depth * 16) {
				ToggleExpansion (pos);
			}
		}

		void ToggleExpansion (TreePosition pos)
		{
			if (collapsed.Contains (pos)) {
				if (TreeSink != null)
					InvokeUser (() => TreeSink.OnRowExpanding (pos));
				collapsed.Remove (pos);
				Rebuild ();
				if (TreeSink != null)
					InvokeUser (() => TreeSink.OnRowExpanded (pos));
			} else {
				if (TreeSink != null)
					InvokeUser (() => TreeSink.OnRowCollapsing (pos));
				collapsed.Add (pos);
				Rebuild ();
				if (TreeSink != null)
					InvokeUser (() => TreeSink.OnRowCollapsed (pos));
			}
		}

		public void SetSource (ITreeDataSource dataSource, IBackend sourceBackend)
		{
			Unsubscribe ();
			treeSource = dataSource;
			Subscribe ();
			Rebuild ();
		}

		void Subscribe ()
		{
			if (treeSource == null)
				return;
			treeSource.NodeInserted += OnTreeChanged;
			treeSource.NodeDeleted += OnTreeChanged;
			treeSource.NodeChanged += OnTreeChanged;
			treeSource.NodesReordered += OnTreeChanged;
			treeSource.Cleared += OnTreeChanged;
		}

		void Unsubscribe ()
		{
			if (treeSource == null)
				return;
			treeSource.NodeInserted -= OnTreeChanged;
			treeSource.NodeDeleted -= OnTreeChanged;
			treeSource.NodeChanged -= OnTreeChanged;
			treeSource.NodesReordered -= OnTreeChanged;
			treeSource.Cleared -= OnTreeChanged;
		}

		void OnTreeChanged (object sender, EventArgs e) => Rebuild ();

		void Rebuild ()
		{
			if (treeSource == null || columns.Count == 0)
				return;
			Host.Columns = columns.Select (c => c.Column).ToArray ();
			Host.RebuildRows (FlattenTree ());
			RefreshCells ();
			ApplySelectionVisuals ();
		}

		/// <summary>Depth-first flatten of the VISIBLE tree: children of an
		/// unexpanded node are skipped; root children are GetChild(null, i).</summary>
		IEnumerable<RowSpec> FlattenTree ()
		{
			var specs = new List<RowSpec> ();
			Walk (null, 0);
			return specs;

			void Walk (TreePosition pos, int depth)
			{
				int count = treeSource.GetChildrenCount (pos);
				for (int i = 0; i < count; i++) {
					var child = treeSource.GetChild (pos, i);
					if (child == null)
						continue;
					bool hasKids = treeSource.GetChildrenCount (child) > 0;
					bool isOpen = !collapsed.Contains (child); // default-expanded tree (Solution pad)
					specs.Add (new RowSpec { Position = child, Depth = depth, HasExpander = hasKids, IsExpanded = isOpen });
					if (isOpen)
						Walk (child, depth + 1);
				}
			}
		}

		void RefreshCells ()
		{
			foreach (var row in Host.Rows) {
				int cellIndex = 0;
				foreach (var col in columns) {
					foreach (var view in col.Column.Views) {
						if (cellIndex < row.Cells.Count && ReferenceEquals (row.Cells [cellIndex].View, view)) {
							var v = view;
							var rowPos = row.Position;
							CellRenderer.Refresh (row.Cells [cellIndex].Control, v, f => treeSource.GetValue (rowPos, f.Index));
						}
						cellIndex++;
					}
				}
			}
		}

		protected override void ApplySelectionVisuals ()
		{
			base.ApplySelectionVisuals ();
			// Indentation + expander gutter are part of the tree look; the
			// expander glyph itself lands with the cell-margin wave.
			foreach (var row in Host.Rows)
				row.Visual.Margin = new Thickness (row.Depth * 16, 0, 0, 0);
		}

		public override object AddColumn (ListViewColumn col)
		{
			columns.Add ((col, col));
			Rebuild ();
			return col;
		}

		public override void RemoveColumn (ListViewColumn col, object handle)
		{
			columns.RemoveAll (c => ReferenceEquals (c.Column, col));
			Rebuild ();
		}

		public override void UpdateColumn (ListViewColumn col, object handle, ListViewColumnChange change)
			=> Rebuild ();

		public TreePosition[] SelectedRows => Selected.ToArray ();

		/// <summary>The data source of the store backing this tree — lets callers
		/// resolve a TreePosition back to its values (tag lookups in the shell).</summary>
		public ITreeDataSource TreeSource => treeSource;

		public void SelectRow (TreePosition pos)
		{
			// Single mode REPLACES the selection (gtk_tree_selection_select_path);
			// Multiple adds.
			if (selectionMode != SelectionMode.Multiple)
				Selected.Clear ();
			if (!Selected.Any (s => ReferenceEquals (s, pos)))
				Selected.Add (pos);
			ApplySelectionVisuals ();
			// Programmatic selection drives the same sink event a user click does
			// (the Gtk backend raises "changed" on gtk_tree_selection_select_path too).
			if (TableSink != null)
				InvokeUser (TableSink.OnSelectionChanged);
		}

		public void UnselectRow (TreePosition pos)
		{
			Selected.RemoveAll (s => ReferenceEquals (s, pos));
			ApplySelectionVisuals ();
		}

		public TreePosition FocusedRow {
			get => focused;
			set {
				focused = value;
				if (value != null)
					Host.ScrollTo (Host.IndexOf (value));
			}
		}

		public bool IsRowSelected (TreePosition pos) => Selected.Any (s => ReferenceEquals (s, pos));

		public bool IsRowExpanded (TreePosition pos) => !collapsed.Contains (pos);

		public void ExpandRow (TreePosition pos, bool expandChildren)
		{
			if (pos == null)
				return;
			collapsed.Remove (pos); // default-expanded: expanding = ensure not collapsed
			if (expandChildren && treeSource != null) {
				int count = treeSource.GetChildrenCount (pos);
				for (int i = 0; i < count; i++)
					ExpandRow (treeSource.GetChild (pos, i), true);
			}
			Rebuild ();
		}

		public void CollapseRow (TreePosition pos)
		{
			expanded.Remove (pos);
			Rebuild ();
		}

		public void ScrollToRow (TreePosition pos) => Host.ScrollTo (Host.IndexOf (pos));

		public void ExpandToRow (TreePosition pos)
		{
			if (treeSource == null)
				return;
			var parent = treeSource.GetParent (pos);
			while (parent != null) {
				expanded.Add (parent);
				parent = treeSource.GetParent (parent);
			}
			Rebuild ();
			Host.ScrollTo (Host.IndexOf (pos));
		}

		public bool BorderVisible {
			get => borderVisible;
			set => borderVisible = value;
		}

		public bool HeadersVisible {
			get => headersVisible;
			set => headersVisible = value;
		}

		public GridLines GridLinesVisible {
			get => GridLines.None;
			set { } // AVALONIA-PORT: row separators
		}

		public bool UseAlternatingRowColors {
			get => useAlternatingRowColors;
			set {
				useAlternatingRowColors = value;
				ApplySelectionVisuals ();
			}
		}

		public bool AnimationsEnabled { get; set; }

		public bool GetDropTargetRow (double x, double y, out RowDropPosition pos, out TreePosition nodePosition)
		{
			pos = RowDropPosition.Into;
			nodePosition = null;
			int row = RowAtPosition (x, y);
			if (row < 0)
				return false;
			nodePosition = Host.Rows [row].Position;
			return true;
		}

		public TreePosition GetRowAtPosition (Point p)
		{
			int row = RowAtPosition (p.X, p.Y);
			return row >= 0 ? Host.Rows [row].Position : null;
		}

		public Rectangle GetCellBounds (TreePosition pos, CellView cell, bool includeMargin)
		{
			var rowBounds = GetRowBounds (pos, includeMargin);
			if (rowBounds.IsEmpty)
				return rowBounds;
			var presenter = Host.RowOf (pos);
			for (int i = 0; i < presenter.Cells.Count; i++) {
				if (ReferenceEquals (presenter.Cells [i].View, cell)) {
					var b = presenter.Cells [i].Control.Bounds;
					return new Rectangle (rowBounds.X + b.X, rowBounds.Y + b.Y, b.Width, b.Height);
				}
			}
			return rowBounds;
		}

		public Rectangle GetRowBounds (TreePosition pos, bool includeMargin)
		{
			int row = Host.IndexOf (pos);
			if (row < 0)
				return Rectangle.Zero;
			return new Rectangle (0, Host.YOf (row), Host.Panel.Bounds.Width, Host.RowHeight (row));
		}

		public TreePosition CurrentEventRow {
			get {
				int row = Host.CurrentEventRow;
				return row >= 0 && row < Host.Rows.Count ? Host.Rows [row].Position : null;
			}
		}

		public override void Dispose () => Unsubscribe ();
	}

	// ----- Core data stores (the "default" implementations the Xwt core
	// ships internally are not registered by any engine: ListStore REQUIRES
	// an IListStoreBackend and TreeStore's fallback is internal. These
	// implementations keep row/node identity — the handles ARE TreePositions,
	// which is what makes backend selection/expansion APIs meaningful) -----

	/// <summary>Fallback position for custom IListDataSource sources (the
	/// handle carries the row index; store rows use ListRowHandle + store
	/// mapping below).</summary>
	public class IndexRowHandle : TreePosition
	{
		public readonly int Index;
		public IndexRowHandle (int index) => Index = index;
	}

	/// <summary>One row of the ListStore backend: identity + values. The
	/// handle object itself is the TreePosition the list backends use.</summary>
	public class ListRowHandle : TreePosition
	{
		public object[] Values;
	}

	public class ListStoreBackend : IListStoreBackend
	{
		readonly List<ListRowHandle> rows = new ();
		Type[] columnTypes = Type.EmptyTypes;

		public event EventHandler<ListRowEventArgs> RowInserted;
		public event EventHandler<ListRowEventArgs> RowDeleted;
		public event EventHandler<ListRowEventArgs> RowChanged;
		public event EventHandler<ListRowOrderEventArgs> RowsReordered;

		public void InitializeBackend (object frontend, ApplicationContext context) { }

		public void EnableEvent (object eventId) { }

		public void DisableEvent (object eventId) { }

		public void Initialize (Type[] types) => columnTypes = types;

		public int RowCount => rows.Count;

		public Type[] ColumnTypes => columnTypes;

		public object GetValue (int row, int column) => rows [row].Values [column];

		public void SetValue (int row, int column, object value)
		{
			rows [row].Values [column] = value;
			RowChanged?.Invoke (this, new ListRowEventArgs (row));
		}

		/// <summary>The TreePosition of a row (identity survives appends of
		/// other rows; indices shift only when rows before it are removed).</summary>
		public ListRowHandle HandleOf (int row) => rows [row];

		/// <summary>Row index of a position handle (-1 when foreign).</summary>
		public int IndexOf (TreePosition pos) => pos is ListRowHandle h ? rows.IndexOf (h) : -1;

		public int AddRow ()
		{
			var handle = new ListRowHandle { Values = new object[columnTypes.Length] };
			rows.Add (handle);
			RowInserted?.Invoke (this, new ListRowEventArgs (rows.Count - 1));
			return rows.Count - 1;
		}

		public int InsertRowAfter (int row)
		{
			var handle = new ListRowHandle { Values = new object[columnTypes.Length] };
			rows.Insert (row + 1, handle);
			RowInserted?.Invoke (this, new ListRowEventArgs (row + 1));
			return row + 1;
		}

		public int InsertRowBefore (int row)
		{
			var handle = new ListRowHandle { Values = new object[columnTypes.Length] };
			rows.Insert (row, handle);
			RowInserted?.Invoke (this, new ListRowEventArgs (row));
			return row;
		}

		public void RemoveRow (int row)
		{
			rows.RemoveAt (row);
			RowDeleted?.Invoke (this, new ListRowEventArgs (row));
		}

		public void Clear ()
		{
			rows.Clear ();
			RowsReordered?.Invoke (this, new ListRowOrderEventArgs (-1, new int[0]));
		}
	}

	/// <summary>One node of the TreeStore backend: identity + hierarchy +
	/// values. The node object itself is the TreePosition.</summary>
	public class TreeNodeHandle : TreePosition
	{
		public object[] Values;
		public TreeNodeHandle Parent;
		public readonly List<TreeNodeHandle> Children = new ();
	}

	public class TreeStoreBackend : ITreeStoreBackend
	{
		readonly List<TreeNodeHandle> roots = new ();
		Type[] columnTypes = Type.EmptyTypes;

		public event EventHandler<TreeNodeEventArgs> NodeInserted;
		public event EventHandler<TreeNodeChildEventArgs> NodeDeleted;
		public event EventHandler<TreeNodeEventArgs> NodeChanged;
		public event EventHandler<TreeNodeOrderEventArgs> NodesReordered;
		public event EventHandler Cleared;

		public void InitializeBackend (object frontend, ApplicationContext context) { }

		public void EnableEvent (object eventId) { }

		public void DisableEvent (object eventId) { }

		public void Initialize (Type[] types) => columnTypes = types;

		public Type[] ColumnTypes => columnTypes;

		public TreePosition GetParent (TreePosition pos)
			=> pos is TreeNodeHandle n ? n.Parent : null;

		public TreePosition GetChild (TreePosition pos, int index)
			=> pos is TreeNodeHandle n ? n.Children [index] : roots [index];

		public int GetChildrenCount (TreePosition pos)
			=> pos is TreeNodeHandle n ? n.Children.Count : roots.Count;

		public object GetValue (TreePosition pos, int column)
			=> pos is TreeNodeHandle n ? n.Values [column] : null;

		public void SetValue (TreePosition pos, int column, object value)
		{
			if (pos is TreeNodeHandle n) {
				n.Values [column] = value;
				NodeChanged?.Invoke (this, new TreeNodeEventArgs (n));
			}
		}

		TreeNodeHandle NewNode (TreeNodeHandle parent, int index)
		{
			var node = new TreeNodeHandle { Values = new object[columnTypes.Length], Parent = parent };
			if (parent == null)
				roots.Insert (index, node);
			else
				parent.Children.Insert (index, node);
			NodeInserted?.Invoke (this, new TreeNodeEventArgs (node, index));
			return node;
		}

		public TreePosition AddChild (TreePosition pos)
		{
			var parent = pos as TreeNodeHandle;
			return NewNode (parent, parent?.Children.Count ?? roots.Count);
		}

		public TreePosition InsertBefore (TreePosition pos)
		{
			var node = (TreeNodeHandle)pos;
			int index = IndexWithin (node);
			return NewNode (node.Parent, index);
		}

		public TreePosition InsertAfter (TreePosition pos)
		{
			var node = (TreeNodeHandle)pos;
			return NewNode (node.Parent, IndexWithin (node) + 1);
		}

		public void Remove (TreePosition pos)
		{
			if (pos is not TreeNodeHandle node)
				return;
			int index = IndexWithin (node);
			if (node.Parent == null)
				roots.Remove (node);
			else
				node.Parent.Children.Remove (node);
			NodeDeleted?.Invoke (this, new TreeNodeChildEventArgs (node.Parent, index, node));
		}

		public TreePosition GetNext (TreePosition pos)
		{
			if (pos is not TreeNodeHandle node)
				return null;
			var siblings = node.Parent?.Children ?? roots;
			int i = siblings.IndexOf (node);
			return i + 1 < siblings.Count ? siblings [i + 1] : null;
		}

		public TreePosition GetPrevious (TreePosition pos)
		{
			if (pos is not TreeNodeHandle node)
				return null;
			var siblings = node.Parent?.Children ?? roots;
			int i = siblings.IndexOf (node);
			return i > 0 ? siblings [i - 1] : null;
		}

		public void Clear ()
		{
			roots.Clear ();
			Cleared?.Invoke (this, EventArgs.Empty);
		}

		int IndexWithin (TreeNodeHandle node)
			=> (node.Parent?.Children ?? roots).IndexOf (node);
	}

	// ----- ComboBox (Xwt.ComboBox ↔ Avalonia ComboBox) -----

	public class ComboBoxBackend : AvaloniaWidgetBackend, IComboBoxBackend
	{
		AComboBox combo;
		IListDataSource source;

		public ComboBoxBackend () { }

		protected override AControl CreateNativeWidget ()
		{
			combo = new AComboBox ();
			combo.SelectionChanged += (s, e) => {
				if (EventSink is IComboBoxEventSink sink)
					InvokeUser (sink.OnSelectionChanged);
			};
			return combo;
		}

		public void SetViews (CellViewCollection views)
		{
			// MonoDevelop combos use a single TextCellView over column 0;
			// the display member is just the item string.
		}

		public void SetSource (IListDataSource dataSource, IBackend sourceBackend)
		{
			source = dataSource;
			var items = new List<string> ();
			if (source != null) {
				for (int i = 0; i < source.RowCount; i++) {
					var v = source.GetValue (i, 0);
					items.Add (v is Xwt.Drawing.Image ? "" : Convert.ToString (v) ?? "");
				}
			}
			combo.ItemsSource = items;
		}

		public int SelectedRow {
			get => combo.SelectedIndex;
			set => combo.SelectedIndex = value;
		}

		public override void Dispose () { }
	}
}
