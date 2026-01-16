using Sandbox;

namespace RedSnail.RoadTool.Editor;

public partial class RoadToolWindow
{
	[Title("Position")]
	private Vector3 _selectedPointPosition
	{
		get => IsSelectedPointValid() ? _selectedPoint.Position : Vector3.Zero;
		set
		{
			using (CreateUndoScope("Updated Spline Point"))
			{
				_selectedPoint = _selectedPoint with { Position = value };
			}
		}
	}

	[Title("In")]
	private Vector3 _selectedPointIn
	{
		get => IsSelectedPointValid() ? _selectedPoint.In : Vector3.Zero;
		set
		{
			using (CreateUndoScope("Updated Spline Point"))
			{
				_selectedPoint = _selectedPoint with { In = value };
			}
		}
	}

	[Title("Out")]
	private Vector3 _selectedPointOut
	{
		get => IsSelectedPointValid() ? _selectedPoint.Out : Vector3.Zero;
		set
		{
			using (CreateUndoScope("Updated Spline Point"))
			{
				_selectedPoint = _selectedPoint with { Out = value };
			}
		}
	}

	[Title("Tangent Mode")]
	private HandleModeTemp _selectedPointTangentMode
	{
		get => IsSelectedPointValid() ? (HandleModeTemp)_selectedPoint.Mode : HandleModeTemp.Auto;
		set
		{
			using (CreateUndoScope("Updated Spline Point"))
			{
				_selectedPoint = _selectedPoint with { Mode = (Spline.HandleMode)value };
				ToggleTangentInput();
			}
		}
	}

	[Title("Roll (Degrees)")]
	private float _selectedPointRoll
	{
		get => IsSelectedPointValid() ? _selectedPoint.Roll : 0f;
		set
		{
			using (CreateUndoScope("Updated Spline Point"))
			{
				_selectedPoint = _selectedPoint with { Roll = value };
			}
		}
	}

	[Title("Up Vector")]
	private Vector3 _selectedPointUp
	{
		get => IsSelectedPointValid() ? _selectedPoint.Up : Vector3.Zero;
		set
		{
			using (CreateUndoScope("Updated Spline Point"))
			{
				_selectedPoint = _selectedPoint with { Up = value };
			}
		}
	}

	[Title("Scale (_, Width, Height)")]
	private Vector3 _selectedPointScale
	{
		get => IsSelectedPointValid() ? _selectedPoint.Scale : Vector3.Zero;
		set
		{
			using (CreateUndoScope("Updated Spline Point"))
			{
				_selectedPoint = _selectedPoint with { Scale = value };
			}
		}
	}

	private int SelectedPointIndex
	{
		get;
		set
		{
			field = value;
			ToggleTangentInput();
		}
	}

	private Spline.Point _selectedPoint
	{
		get => IsSelectedPointValid() ? _targetComponent.Spline.GetPoint(SelectedPointIndex) : new Spline.Point();
		set
		{
			using (CreateUndoScope("Updated Spline Point"))
			{
				_targetComponent.Spline.UpdatePoint(SelectedPointIndex, value);
			}
		}
	}



	private bool IsSelectedPointValid()
	{
		return SelectedPointIndex < _targetComponent.Spline.PointCount;
	}



	private void ToggleTangentInput()
	{
		bool isAutoOrLinear = _selectedPoint.Mode is Spline.HandleMode.Auto or Spline.HandleMode.Linear;
		_inTangentControl.Enabled = !isAutoOrLinear;
		_outTangentControl.Enabled = !isAutoOrLinear;
	}



	private void MoveSelectedPoint(Vector3 _Delta)
	{
		var updatedPoint = _selectedPoint with { Position = _selectedPoint.Position + _Delta };

		_targetComponent.Spline.UpdatePoint(SelectedPointIndex, updatedPoint);
	}



	private void MoveSelectedPointInTangent(Vector3 _Delta)
	{
		var updatedPoint = _selectedPoint;
		updatedPoint.In += _Delta;

		if (_selectedPointTangentMode == HandleModeTemp.Auto)
		{
			updatedPoint.Mode = Spline.HandleMode.Mirrored;
		}

		if (_selectedPointTangentMode is HandleModeTemp.Mirrored or HandleModeTemp.Auto)
		{
			updatedPoint.Out = -updatedPoint.In;
		}

		_targetComponent.Spline.UpdatePoint(SelectedPointIndex, updatedPoint);
	}



	private void MoveSelectedPointOutTangent(Vector3 _Delta)
	{
		var updatedPoint = _selectedPoint;
		updatedPoint.Out += _Delta;

		if (_selectedPointTangentMode == HandleModeTemp.Auto)
		{
			updatedPoint.Mode = Spline.HandleMode.Mirrored;
		}

		if (_selectedPointTangentMode is HandleModeTemp.Mirrored or HandleModeTemp.Auto)
		{
			updatedPoint.In = -updatedPoint.Out;
		}

		_targetComponent.Spline.UpdatePoint(SelectedPointIndex, updatedPoint);
	}



	private void SelectPoint(int _Index)
	{
		SelectedPointIndex = _Index;
		_inTangentSelected = false;
		_outTangentSelected = false;
	}
}
