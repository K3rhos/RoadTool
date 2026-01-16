using Sandbox;
using Editor;

namespace RedSnail.RoadTool.Editor;

public partial class RoadToolWindow
{
	private const int HEADER_HEIGHT = 32;



	private void Rebuild()
	{
		Layout.Clear(true);
		Layout.Margin = 0;
		Icon = _isClosed ? "" : "route";
		UpdateWindowTitle();
		IsGrabbable = !_isClosed;

		if (_isClosed)
		{
			BuildClosedState();
			return;
		}

		MinimumWidth = 400;
		BuildHeader();

		if (_targetComponent.IsValid())
		{
			BuildControlSheet();
		}

		Layout.Margin = 4;
	}



	private void BuildClosedState()
	{
		var closedRow = Layout.AddRow();

		closedRow.Add(new IconButton("route", () => { _isClosed = false; Rebuild(); })
		{
			ToolTip = "Open Spline Point Editor",
			FixedHeight = HEADER_HEIGHT,
			FixedWidth = HEADER_HEIGHT,
			Background = Color.Transparent
		});

		MinimumWidth = 0;
	}



	private void BuildHeader()
	{
		var headerRow = Layout.AddRow();

		headerRow.AddStretchCell();

		headerRow.Add(new IconButton("info")
		{
			ToolTip = GetInfoTooltip(),
			FixedHeight = HEADER_HEIGHT,
			FixedWidth = HEADER_HEIGHT,
			Background = Color.Transparent
		});

		headerRow.Add(new IconButton("close", CloseWindow)
		{
			ToolTip = "Close Editor",
			FixedHeight = HEADER_HEIGHT,
			FixedWidth = HEADER_HEIGHT,
			Background = Color.Transparent
		});
	}



	private string GetInfoTooltip()
	{
		return "Controls to edit the spline points.\n" +
			   "In addition to modifying the properties in the control sheet, you can also use the 3D Gizmos.\n" +
			   "Clicking on the spline between points will split the spline at that position.\n" +
			   "Holding shift while dragging a point's position will drag out a new point.";
	}



	private void BuildControlSheet()
	{
		var serialized = this.GetSerialized();
		var controlSheet = new ControlSheet();

		// Add property rows
		controlSheet.AddRow(serialized.GetProperty(nameof(_selectedPointTangentMode)));
		controlSheet.AddRow(serialized.GetProperty(nameof(_selectedPointPosition)));
		_inTangentControl = controlSheet.AddRow(serialized.GetProperty(nameof(_selectedPointIn)));
		_outTangentControl = controlSheet.AddRow(serialized.GetProperty(nameof(_selectedPointOut)));

		// Add advanced group
		var roll = serialized.GetProperty(nameof(_selectedPointRoll));
		var scale = serialized.GetProperty(nameof(_selectedPointScale));
		var up = serialized.GetProperty(nameof(_selectedPointUp));
		controlSheet.AddGroup("Advanced", [roll, scale, up]);

		// Add control buttons
		controlSheet.AddLayout(BuildControlButtons());

		Layout.Add(controlSheet);
		ToggleTangentInput();
	}



	private Layout BuildControlButtons()
	{
		var row = Layout.Row();
		row.Spacing = 16;
		row.Margin = 8;

		row.Add(CreateNavigationButton("skip_previous", -1, "Go to previous point"));
		row.Add(CreateNavigationButton("skip_next", 1, "Go to next point"));
		row.Add(CreateDeleteButton());
		row.Add(CreateAddButton());

		return row;
	}



	private IconButton CreateNavigationButton(string icon, int direction, string tooltip)
	{
		return new IconButton(icon, () =>
		{
			if (direction < 0)
				SelectedPointIndex = int.Max(0, SelectedPointIndex - 1);
			else
				SelectedPointIndex = int.Min(_targetComponent.Spline.PointCount - 1, SelectedPointIndex + 1);

			UpdateWindowTitle();
			Focus();
		})
		{ ToolTip = tooltip };
	}



	private IconButton CreateDeleteButton()
	{
		return new IconButton("delete", () =>
		{
			using (CreateUndoScope("Delete Spline Point"))
			{
				_targetComponent.Spline.RemovePoint(SelectedPointIndex);
				SelectedPointIndex = int.Max(0, SelectedPointIndex - 1);
			}
			UpdateWindowTitle();
			Focus();
		})
		{ ToolTip = "Delete point" };
	}



	private IconButton CreateAddButton()
	{
		return new IconButton("add", () =>
		{
			using (CreateUndoScope("Added Spline Point"))
			{
				InsertNewPoint();
			}
			SelectedPointIndex++;
			UpdateWindowTitle();
			Focus();
		})
		{
			ToolTip = "Insert point after current point.\n" +
					  "You can also hold shift while dragging a point to create a new point."
		};
	}



	private void InsertNewPoint()
	{
		var spline = _targetComponent.Spline;

		if (SelectedPointIndex == spline.PointCount - 1)
		{
			var distance = spline.GetDistanceAtPoint(SelectedPointIndex);
			var tangent = spline.SampleAtDistance(distance).Tangent;
			var newPosition = _selectedPoint.Position + tangent * 200;

			spline.InsertPoint(SelectedPointIndex + 1, _selectedPoint with { Position = newPosition });
		}
		else
		{
			var currentDist = spline.GetDistanceAtPoint(SelectedPointIndex);
			var nextDist = spline.GetDistanceAtPoint(SelectedPointIndex + 1);
			var midDist = (currentDist + nextDist) / 2;

			spline.AddPointAtDistance(midDist, true);
		}
	}



	private void UpdateWindowTitle()
	{
		WindowTitle = _isClosed ? "" : $"Spline Point [{SelectedPointIndex}] Editor - {_targetComponent?.GameObject?.Name ?? ""}";
	}



	private void CloseWindow()
	{
		_isClosed = true;
		Rebuild();
		Position = Parent.Size - 32;
	}
}
